using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Drop/Pickup intelligence composed entirely from existing projection + roster data. Never
/// reads or writes <c>IDecisionEngine</c>/knowledge state, so it cannot disturb Start/Sit or
/// Quick Picks. Ranking uses more than raw projected points: replacement value (own projection
/// vs. the best available same-position free agent), projection confidence, positional depth,
/// and a pickup role/availability sanity check (NFL team assignment, status, production-backed
/// projection inputs). In dynasty leagues, Keep Value also applies an explainable age /
/// early-career / role trade-value adjustment from data already on <see cref="Player"/> so
/// small weekly projection edges cannot alone justify dropping young, high-upside, or
/// role-backed roster pieces. Never fabricates ownership, waiver priority, betting lines, news,
/// draft capital, trade values, or matchup information — absent signals stay absent.
/// </summary>
public sealed class DropPickupService : IDropPickupService
{
    // Modest, explainable nudges around the core replacement-value signal (see BuildReasons).
    private const double ConfidenceWeightPerPoint = 0.08;
    private const double ThinPositionBonus = 6.0;
    private const double ShallowPositionBonus = 2.0;
    private const double FantasyStarterKeepBonus = 10.0;
    private const double EstablishedNflRoleKeepBonus = 8.0;
    private const int MaxSuggestions = 3;
    private const int MaxDropCandidatesConsidered = 8;

    // Pickup credibility: obscure/inactive/no-production veterans must not outrank real roles.
    private const double PickupNoProductionVeteranPenalty = 20.0;
    private const double PickupNoProductionYouthPenalty = 4.0;
    private const double PickupLowConfidencePenalty = 8.0;
    private const double PickupSoftLowConfidencePenalty = 3.0;
    private const int CredibleProductionConfidenceFloor = 40;

    // Dynasty keep-value: large enough that a small weekly projection edge cannot alone surface
    // a young/early-career/role-backed player as a drop, but not so large that aging low-upside
    // pieces are frozen on the roster forever.
    private const double DynastyEarlyCareerYears0 = 14.0;
    private const double DynastyEarlyCareerYears1 = 11.0;
    private const double DynastyEarlyCareerYears2 = 7.0;
    private const double DynastyEarlyCareerYears3 = 3.0;
    private const double DynastyYoungAgeBonus = 8.0;
    private const double DynastyNearYoungAgeBonus = 4.0;
    private const double DynastyAgingPenalty = 3.0;
    private const double DynastyOlderPenalty = 6.0;
    private const double DynastyLowConfidenceYouthBonus = 2.0;
    private const double DynastyInjuredYouthBonus = 10.0;
    private const double DynastyInjuredOpportunityBonus = 10.0;
    private const double DynastyStarterTradeValueBonus = 10.0;
    private const double DynastyProductionTradeValueBonus = 5.0;
    private const double DynastyHealthyLowRolePenalty = 8.0;
    /// <summary>Secondary only — never enough alone to sink a role/waiver-valuable piece.</summary>
    private const double DynastyEasilyReplaceablePenalty = 2.0;
    private const double DynastyHighWaiverValueBonus = 18.0;
    /// <summary>How much weekly replacement margin counts toward dynasty Keep Value (0–1).</summary>
    private const double DynastyMarginWeight = 0.25;
    /// <summary>Ceiling minus current projection that signals healthy upside / future opportunity.</summary>
    private const double DynastyCeilingOpportunityGap = 4.0;
    /// <summary>If at most this many same-position FAs outrank the player as a waiver target, protect.</summary>
    private const int DynastyHighWaiverMaxFaAhead = 1;
    /// <summary>Absolute waiver-point floor so thin free-agent pools cannot mark every rostered player "high value".</summary>
    private const double DynastyHighWaiverMinPoints = 8.0;
    /// <summary>Dynasty keep bonus at/above this requires a larger value-gain to recommend a drop.</summary>
    private const double DynastyProtectedKeepThreshold = 8.0;
    /// <summary>Minimum projected-points gain for any dynasty drop recommendation (+5/+6 is not enough).</summary>
    private const double DynastyAnyDropMinValueGain = 8.0;
    /// <summary>Minimum projected-points gain to drop a dynasty-protected player.</summary>
    private const double DynastyProtectedMinValueGain = 12.0;
    /// <summary>Minimum projected-points gain to drop a fantasy starter.</summary>
    private const double StarterProtectedMinValueGain = 6.0;
    /// <summary>Minimum projected-points gain to drop an established NFL-role player.</summary>
    private const double EstablishedRoleMinValueGain = 5.0;

    private readonly ILeagueState _leagueState;
    private readonly IPlayerService _players;
    private readonly IProjectionService _projections;
    private readonly object _gate = new();

    private DropPickupReport? _cached;
    private PersonalizedAnalysisContext _cachedContext;

    public DropPickupService(
        ILeagueState leagueState,
        IPlayerService players,
        IProjectionService projections)
    {
        _leagueState = leagueState;
        _players = players;
        _projections = projections;
        _leagueState.Changed += OnLeagueContextChanged;
    }

    public DropPickupReport GetReport()
    {
        var context = PersonalizedAnalysisContext.FromState(_leagueState);
        if (_cached is not null &&
            context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
            context.ScoringType == _cachedContext.ScoringType &&
            context.Week == _cachedContext.Week)
        {
            return _cached;
        }

        lock (_gate)
        {
            context = PersonalizedAnalysisContext.FromState(_leagueState);
            if (_cached is not null &&
                context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
                context.ScoringType == _cachedContext.ScoringType &&
                context.Week == _cachedContext.Week)
            {
                return _cached;
            }

            _cached = BuildReport(context);
            _cachedContext = context;
            return _cached;
        }
    }

    private void OnLeagueContextChanged()
    {
        lock (_gate)
        {
            _cached = null;
            _cachedContext = default;
        }
    }

    private DropPickupReport BuildReport(PersonalizedAnalysisContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var league = _leagueState.CurrentLeague;
        var team = _leagueState.CurrentUserTeam;

        if (league is null)
        {
            return Empty(context, "Select or connect a league to see Drop/Pickup suggestions.", now);
        }

        if (!context.IsSetupComplete || team is null)
        {
            return Empty(
                context, "Pick your owned team in the league switcher to generate Drop/Pickup suggestions.", now, league);
        }

        if (team.PlayerIds.Count == 0)
        {
            return Empty(
                context,
                "This team has no roster players loaded yet. Connect a live Sleeper league or wait for roster sync.",
                now,
                league,
                team);
        }

        var allPlayers = _players.GetAllPlayers().ToDictionary(p => p.Id);
        var projectionByPlayer = _projections.GetAllProjections().ToDictionary(p => p.PlayerId);

        // League-wide ownership: every player rostered by any team (taxi/IR included — still
        // owned) is unavailable. This is real data from connected league rosters, not guessed.
        var rosteredElsewhere = _leagueState.GetCurrentTeams()
            .SelectMany(t => t.PlayerIds)
            .ToHashSet();

        var starterSet = team.StarterIds.ToHashSet();
        var rosterRows = team.PlayerIds
            .Select(id => allPlayers.TryGetValue(id, out var player) ? player : null)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        var unavailable = new List<string>();
        if (rosterRows.Count < team.PlayerIds.Count)
        {
            unavailable.Add("Some roster players were not found in the player catalog.");
        }

        var depthByPosition = rosterRows
            .GroupBy(p => p.Position)
            .ToDictionary(g => g.Key, g => g.Count());

        var freeAgentsByPosition = allPlayers.Values
            .Where(p => !rosteredElsewhere.Contains(p.Id))
            .GroupBy(p => p.Position)
            .ToDictionary(g => g.Key, g => g
                .Select(p => (Player: p, Projection: projectionByPlayer.GetValueOrDefault(p.Id)))
                .Where(x => x.Projection is not null && IsCrediblePickupCandidate(x.Player, x.Projection!))
                .OrderByDescending(x => RankPickupScore(x.Player, x.Projection!))
                .ToList());

        var isDynasty = league.LeagueType == LeagueType.Dynasty;
        var dropCandidates = rosterRows
            .Select(player => BuildDropCandidate(
                player,
                starterSet.Contains(player.Id),
                projectionByPlayer.GetValueOrDefault(player.Id),
                depthByPosition.GetValueOrDefault(player.Position),
                freeAgentsByPosition.GetValueOrDefault(player.Position),
                isDynasty))
            .OrderBy(c => c.KeepValueScore)
            .ToList();

        var suggestions = new List<DropPickupSuggestion>();
        var usedPickupIds = new HashSet<Guid>();

        foreach (var drop in dropCandidates.Take(MaxDropCandidatesConsidered))
        {
            if (suggestions.Count >= MaxSuggestions)
            {
                break;
            }

            var candidates = freeAgentsByPosition.GetValueOrDefault(
                allPlayers[drop.PlayerId].Position) ?? [];
            var bestAvailable = candidates.FirstOrDefault(x => !usedPickupIds.Contains(x.Player.Id));
            if (bestAvailable.Player is null || bestAvailable.Projection is null)
            {
                continue;
            }

            var dropPoints = (double?)drop.ProjectedPoints;
            var pickupPoints = (double)bestAvailable.Projection.ProjectedFantasyPoints;
            var valueGain = pickupPoints - (dropPoints ?? 0);
            if (valueGain <= 0)
            {
                // Only recommend swaps that are a real improvement — never a lateral or worse move.
                continue;
            }

            // Fantasy starters / established NFL roles need a clear upgrade, not a small edge.
            if (drop.IsStarter && valueGain < StarterProtectedMinValueGain)
            {
                continue;
            }

            if (drop.EstablishedRoleKeep >= EstablishedNflRoleKeepBonus &&
                valueGain < EstablishedRoleMinValueGain)
            {
                continue;
            }

            // Dynasty: small weekly projection edges must not auto-justify drops. Protected
            // young/opportunity pieces need an even clearer upgrade.
            if (isDynasty && valueGain < DynastyAnyDropMinValueGain)
            {
                continue;
            }

            if (isDynasty &&
                drop.DynastyKeepAdjustment >= DynastyProtectedKeepThreshold &&
                valueGain < DynastyProtectedMinValueGain)
            {
                continue;
            }

            var pickup = BuildPickupCandidate(bestAvailable.Player, bestAvailable.Projection, drop, valueGain);
            usedPickupIds.Add(pickup.PlayerId);

            suggestions.Add(new DropPickupSuggestion
            {
                Drop = drop,
                Pickup = pickup,
                ValueGain = Math.Round(valueGain, 1),
                Reasoning =
                    $"Add {pickup.PlayerName} ({pickup.PositionLabel}) for {drop.PlayerName} — " +
                    $"+{valueGain:0.0} projected points, same roster spot."
            });
        }

        unavailable.AddRange(
        [
            "Waiver priority / FAAB budget is not modeled (not available).",
            "Opponent matchup strength and betting lines are not factored into these rankings (not available).",
            "Breaking news beyond current player status is not factored in beyond projection confidence."
        ]);

        var rosterLimitStatus = RosterLimitReconciler.Check(team, league);
        var isOverLimit = rosterLimitStatus is { IsKnown: true, IsOverLimit: true };

        // Over-limit teams always need cut guidance even when no improving pickup exists.
        // Reuse the already-ranked Keep Value list; do not invent pickups.
        IReadOnlyList<DropCandidate> dropOnlyCandidates = isOverLimit
            ? dropCandidates.Take(MaxSuggestions).ToList()
            : [];

        var statusMessage = suggestions.Count > 0
            ? $"{suggestions.Count} suggested swap(s) for {context.DisplayLabel}." +
              (isOverLimit ? $" Note: {rosterLimitStatus.Message}" : string.Empty)
            : isOverLimit && dropOnlyCandidates.Count > 0
                ? $"Roster is over the configured limit — {dropOnlyCandidates.Count} drop candidate(s) ranked by keep value. " +
                  rosterLimitStatus.Message
                : $"No improving same-position swap found for {rosterRows.Count} roster players against " +
                  $"{allPlayers.Count - rosteredElsewhere.Count} available players.";

        return new DropPickupReport
        {
            LeagueId = league.Id,
            SelectedRosterId = team.RosterId,
            LeagueName = league.Name,
            TeamName = context.TeamName ?? team.DisplayName,
            IsSetupComplete = true,
            HasRosterPlayers = true,
            RosterLimit = league.RosterLimit,
            RosterCount = team.CountedPlayerIds.Count,
            AvailablePlayerCount = allPlayers.Count - rosteredElsewhere.Count,
            Suggestions = suggestions,
            DropCandidates = dropOnlyCandidates,
            IsOverRosterLimit = isOverLimit,
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StatusMessage = statusMessage,
            GeneratedAt = now
        };
    }

    private static DropCandidate BuildDropCandidate(
        Player player,
        bool isStarter,
        PlayerProjection? projection,
        int positionDepth,
        List<(Player Player, PlayerProjection? Projection)>? freeAgentsAtPosition,
        bool isDynasty)
    {
        var ownPoints = projection is null ? (double?)null : (double)projection.ProjectedFantasyPoints;
        var confidence = projection?.Confidence;
        var bestFreeAgent = freeAgentsAtPosition?.FirstOrDefault();
        var replacementLevel = bestFreeAgent?.Projection is { } fa ? (double)fa.ProjectedFantasyPoints : 0.0;
        double? replacementMargin = ownPoints is null ? null : ownPoints.Value - replacementLevel;

        var scarcityBonus = positionDepth <= 1 ? ThinPositionBonus : positionDepth == 2 ? ShallowPositionBonus : 0.0;
        var confidenceAdjustment = confidence is int c ? (c - 50) * ConfidenceWeightPerPoint : 0.0;
        var roleReasons = new List<string>();
        var establishedRoleKeep = ComputeEstablishedRoleKeep(player, projection, isStarter, roleReasons);
        var isHighWaiverValue = isDynasty &&
            IsHighValueWaiverTargetIfReleased(player, projection, freeAgentsAtPosition);
        var dynastyReasons = new List<string>();
        var dynastyKeep = isDynasty
            ? ComputeDynastyKeepAdjustment(
                player,
                projection,
                isStarter,
                positionDepth,
                replacementMargin,
                isHighWaiverValue,
                dynastyReasons)
            : 0.0;
        // Dynasty Keep Value down-weights weekly replacement margin so short-term projection
        // swings (including injury-depressed weeks) do not dominate long-term ranking.
        var marginContribution = (replacementMargin ?? -100) * (isDynasty ? DynastyMarginWeight : 1.0);
        var keepValue =
            marginContribution +
            confidenceAdjustment +
            scarcityBonus +
            establishedRoleKeep +
            dynastyKeep;

        var reasons = new List<string>();
        if (replacementMargin is { } margin)
        {
            reasons.Add(margin <= 1
                ? $"Best available {player.Position} on waivers projects nearly as well (margin {margin:+0.0;-0.0;0.0} pts)."
                : $"Projects {margin:0.0} pts above the best available {player.Position} on waivers.");
        }
        else
        {
            reasons.Add("No current projection available for this player.");
        }

        if (confidence is int conf && conf < 45)
        {
            reasons.Add($"Low projection confidence ({conf}%).");
        }

        if (positionDepth > 2)
        {
            reasons.Add($"{positionDepth} {player.Position}s already on the roster — depth is secondary to role/trade value.");
        }
        else if (positionDepth <= 1)
        {
            reasons.Add($"Only {player.Position} on the roster — dropping leaves a positional hole.");
        }

        reasons.AddRange(roleReasons);
        reasons.AddRange(dynastyReasons);

        return new DropCandidate
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            PositionLabel = PlayerPresentation.PositionLabel(player.Position),
            IsStarter = isStarter,
            ProjectedPoints = ownPoints,
            Confidence = confidence,
            KeepValueScore = Math.Round(keepValue, 2),
            DynastyKeepAdjustment = Math.Round(dynastyKeep, 2),
            EstablishedRoleKeep = Math.Round(establishedRoleKeep, 2),
            ReplacementMargin = replacementMargin is null ? null : Math.Round(replacementMargin.Value, 1),
            PositionDepthOnRoster = positionDepth,
            Reasons = reasons
        };
    }

    /// <summary>
    /// Role/availability sanity check for pickup candidates using only existing player +
    /// projection provenance. Excludes practice-squad/suspended/unrostered players and
    /// veterans with no production-backed projection unless early-career/young upside data
    /// is present. Never invents roles or draft capital.
    /// </summary>
    private static bool IsCrediblePickupCandidate(Player player, PlayerProjection projection)
    {
        if (!HasNflTeam(player.Team))
        {
            return false;
        }

        if (player.Status is PlayerStatus.PracticeSquad
            or PlayerStatus.Suspended
            or PlayerStatus.InjuredReserve)
        {
            return false;
        }

        var hasProduction = HasProductionBackedProjection(projection);
        if (hasProduction)
        {
            return true;
        }

        // No meaningful NFL production in projection inputs: only allow when other available
        // data supports upside (early career / young age). Do not invent draft capital.
        return HasYouthUpsideSignal(player);
    }

    private static double RankPickupScore(Player player, PlayerProjection projection)
    {
        var points = (double)projection.ProjectedFantasyPoints;
        var confidenceAdjustment = (projection.Confidence - 50) * ConfidenceWeightPerPoint;
        var penalty = 0.0;

        if (!HasProductionBackedProjection(projection))
        {
            penalty += HasYouthUpsideSignal(player)
                ? PickupNoProductionYouthPenalty
                : PickupNoProductionVeteranPenalty;
        }

        if (projection.Confidence < CredibleProductionConfidenceFloor)
        {
            penalty += PickupLowConfidencePenalty;
        }
        else if (projection.Confidence < 50)
        {
            penalty += PickupSoftLowConfidencePenalty;
        }

        if (player.Status is PlayerStatus.Doubtful or PlayerStatus.Out)
        {
            penalty += 3.0;
        }

        return points + confidenceAdjustment - penalty;
    }

    private static double ComputeEstablishedRoleKeep(
        Player player,
        PlayerProjection? projection,
        bool isStarter,
        List<string> reasons)
    {
        var bonus = 0.0;
        if (isStarter)
        {
            bonus += FantasyStarterKeepBonus;
            reasons.Add("Fantasy starter — keep unless a clear upgrade.");
        }

        var confidence = projection?.Confidence ?? 0;
        var hasProduction = projection is not null && HasProductionBackedProjection(projection);
        // Production-backed NFL roster role still counts when temporarily unavailable — injury
        // depresses the weekly number, not the underlying role/trade value.
        if (HasNflTeam(player.Team) &&
            hasProduction &&
            confidence >= 55 &&
            player.Status is not (PlayerStatus.PracticeSquad or PlayerStatus.Suspended))
        {
            bonus += EstablishedNflRoleKeepBonus;
            reasons.Add(
                IsTemporarilyUnavailable(player.Status)
                    ? "Established NFL role (production-backed; current status treated as temporary)."
                    : "Established NFL role (active, production-backed projection).");
        }

        return bonus;
    }

    /// <summary>
    /// True when releasing this player would make them a top same-position waiver target versus
    /// currently available players, using injury-aware waiver points (ceiling when injured).
    /// </summary>
    private static bool IsHighValueWaiverTargetIfReleased(
        Player player,
        PlayerProjection? projection,
        List<(Player Player, PlayerProjection? Projection)>? freeAgentsAtPosition)
    {
        if (projection is null || !HasProductionBackedProjection(projection))
        {
            return false;
        }

        var ownPoints = WaiverValuePoints(player, projection);
        if (ownPoints < DynastyHighWaiverMinPoints)
        {
            return false;
        }

        var ownScore = RankWaiverTargetScore(player, projection);
        var faAhead = 0;
        if (freeAgentsAtPosition is not null)
        {
            foreach (var (fa, faProjection) in freeAgentsAtPosition)
            {
                if (faProjection is null)
                {
                    continue;
                }

                if (RankWaiverTargetScore(fa, faProjection) > ownScore)
                {
                    faAhead++;
                    if (faAhead > DynastyHighWaiverMaxFaAhead)
                    {
                        return false;
                    }
                }
            }
        }

        return faAhead <= DynastyHighWaiverMaxFaAhead;
    }

    /// <summary>
    /// Waiver-target ranking score. Injury status does not apply the pickup injury penalty —
    /// temporarily unavailable players are scored on healthy upside (ceiling) when elevated.
    /// </summary>
    private static double RankWaiverTargetScore(Player player, PlayerProjection projection)
    {
        var points = WaiverValuePoints(player, projection);
        var confidenceAdjustment = (projection.Confidence - 50) * ConfidenceWeightPerPoint;
        var penalty = 0.0;

        if (!HasProductionBackedProjection(projection))
        {
            penalty += HasYouthUpsideSignal(player)
                ? PickupNoProductionYouthPenalty
                : PickupNoProductionVeteranPenalty;
        }

        if (projection.Confidence < CredibleProductionConfidenceFloor)
        {
            penalty += PickupLowConfidencePenalty;
        }
        else if (projection.Confidence < 50)
        {
            penalty += PickupSoftLowConfidencePenalty;
        }

        return points + confidenceAdjustment - penalty;
    }

    private static double WaiverValuePoints(Player player, PlayerProjection projection)
    {
        var current = (double)projection.ProjectedFantasyPoints;
        if (!IsTemporarilyUnavailable(player.Status))
        {
            return current;
        }

        // Injury may depress the weekly median; ceiling retains the return/role path signal.
        var ceiling = (double)projection.Ceiling;
        return Math.Max(current, ceiling);
    }

    /// <summary>
    /// Dynasty trade-value Keep Value adjustment from data already present on
    /// <see cref="Player"/> / the projection (age, years-pro as career-capital proxy, role,
    /// production-backed inputs, confidence, ceiling-vs-current as healthy upside, roster
    /// depth as a secondary factor, and same-position waiver value if released). Draft
    /// round/pick are not on the player model and are never invented. Injury on a piece with
    /// a credible return/role path is temporary context — not a keep penalty.
    /// </summary>
    private static double ComputeDynastyKeepAdjustment(
        Player player,
        PlayerProjection? projection,
        bool isStarter,
        int positionDepth,
        double? replacementMargin,
        bool isHighWaiverValue,
        List<string> reasons)
    {
        var adjustment = 0.0;
        var youthSignal = false;
        var confidence = projection?.Confidence;
        var hasProduction = projection is not null && HasProductionBackedProjection(projection);
        var injured = IsTemporarilyUnavailable(player.Status);
        var ceilingUpside = projection is null
            ? 0.0
            : (double)(projection.Ceiling - projection.ProjectedFantasyPoints);

        if (player.YearsPro is int yearsPro)
        {
            var yearsBonus = yearsPro switch
            {
                <= 0 => DynastyEarlyCareerYears0,
                1 => DynastyEarlyCareerYears1,
                2 => DynastyEarlyCareerYears2,
                3 => DynastyEarlyCareerYears3,
                _ => 0.0
            };
            if (yearsBonus > 0)
            {
                adjustment += yearsBonus;
                youthSignal = true;
                reasons.Add($"Dynasty early-career trade value ({yearsPro} years pro).");
            }
        }

        if (player.Age is int age)
        {
            var (youngCutoff, agingStart) = player.Position switch
            {
                Position.QB => (26, 32),
                Position.TE => (25, 30),
                Position.RB => (24, 27),
                _ => (24, 29)
            };

            if (age <= youngCutoff - 2)
            {
                adjustment += DynastyYoungAgeBonus;
                youthSignal = true;
                reasons.Add($"Dynasty young-player trade value (age {age}).");
            }
            else if (age <= youngCutoff)
            {
                adjustment += DynastyNearYoungAgeBonus;
                youthSignal = true;
                reasons.Add($"Dynasty near-prime trade value (age {age}).");
            }
            else if (age >= agingStart + 3)
            {
                adjustment -= DynastyOlderPenalty;
                reasons.Add($"Dynasty aging risk (age {age}) — lower long-term trade value.");
            }
            else if (age >= agingStart)
            {
                adjustment -= DynastyAgingPenalty;
                reasons.Add($"Dynasty aging risk (age {age}) — modest trade-value discount.");
            }
        }

        // Role-backed trade value: a starting, production-backed piece still has dynasty value
        // even when age alone would discount them.
        if (isStarter && hasProduction)
        {
            adjustment += DynastyStarterTradeValueBonus;
            reasons.Add("Dynasty role trade value (fantasy starter with production-backed projection).");
        }
        else if (hasProduction &&
                 confidence is >= 55 &&
                 player.Status is not (PlayerStatus.PracticeSquad or PlayerStatus.Suspended))
        {
            adjustment += DynastyProductionTradeValueBonus;
            reasons.Add("Dynasty production trade value (production-backed projection).");
        }

        // Low confidence on a youth piece: weekly projection is a weak reason to abandon upside.
        if (youthSignal && confidence is int conf && conf < 45)
        {
            adjustment += DynastyLowConfidenceYouthBonus;
            reasons.Add($"Dynasty: low weekly confidence ({conf}%) does not erase early-career upside.");
        }

        // Injury is not a dynasty negative when there is a credible return/role path
        // (youth/early-career and/or elevated production-backed ceiling).
        if (injured && (youthSignal || (hasProduction && ceilingUpside >= DynastyCeilingOpportunityGap)))
        {
            adjustment += DynastyInjuredYouthBonus;
            reasons.Add($"Dynasty: {player.Status} status treated as temporary — not a trade-value penalty.");

            if (hasProduction &&
                (ceilingUpside >= DynastyCeilingOpportunityGap ||
                 projection!.InputsUsed.InjurySignal))
            {
                adjustment += DynastyInjuredOpportunityBonus;
                reasons.Add(
                    "Dynasty future-role opportunity: production-backed ceiling remains elevated " +
                    "while current projection looks injury-depressed.");
            }
        }
        else if (youthSignal &&
                 player.Status == PlayerStatus.Active &&
                 replacementMargin is < -1 &&
                 ceilingUpside < DynastyCeilingOpportunityGap &&
                 !isHighWaiverValue &&
                 (confidence is < 55 || positionDepth > 2))
        {
            // Healthy early-career piece already projecting poorly with limited ceiling upside —
            // youth alone should not freeze a low long-term role on the roster.
            adjustment -= DynastyHealthyLowRolePenalty;
            reasons.Add(
                "Dynasty: healthy but limited current role/ceiling — early-career shield reduced.");
        }

        if (isHighWaiverValue)
        {
            adjustment += DynastyHighWaiverValueBonus;
            reasons.Add(
                "Would rank as a high-value same-position waiver target if released — protect trade/waiver value.");
        }

        // Positional depth is secondary: skip when starter, high waiver value, or injured
        // opportunity path already justifies keeping the player.
        if (positionDepth > 2 &&
            !isStarter &&
            !isHighWaiverValue &&
            !(injured && (youthSignal || ceilingUpside >= DynastyCeilingOpportunityGap)))
        {
            adjustment -= DynastyEasilyReplaceablePenalty;
            reasons.Add(
                $"Dynasty: {positionDepth} {player.Position}s on roster — minor depth factor only.");
        }

        return adjustment;
    }

    private static bool HasYouthUpsideSignal(Player player) =>
        player.YearsPro is <= 2 || player.Age is <= 24;

    private static bool HasNflTeam(string? team) =>
        !string.IsNullOrWhiteSpace(team) &&
        !team.Equals("FA", StringComparison.OrdinalIgnoreCase) &&
        !team.Equals("None", StringComparison.OrdinalIgnoreCase);

    private static bool HasProductionBackedProjection(PlayerProjection projection)
    {
        if (projection.InputsUsed.HistoricalStatistics ||
            projection.InputsUsed.RecentUsage ||
            projection.InputsUsed.CareerBaseline)
        {
            return true;
        }

        var source = projection.InputsUsed.ProductionSource;
        return source.Equals(nameof(ProductionDataSource.StatsService), StringComparison.OrdinalIgnoreCase) ||
               source.Equals(nameof(ProductionDataSource.CuratedSeason), StringComparison.OrdinalIgnoreCase) ||
               source.Equals(nameof(ProductionDataSource.ProfileSeason), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemporarilyUnavailable(PlayerStatus status) =>
        status is PlayerStatus.Out
            or PlayerStatus.Doubtful
            or PlayerStatus.Questionable
            or PlayerStatus.InjuredReserve;

    private static PickupCandidate BuildPickupCandidate(
        Player player,
        PlayerProjection projection,
        DropCandidate drop,
        double valueGain)
    {
        var points = (double)projection.ProjectedFantasyPoints;
        var pickupValue = RankPickupScore(player, projection);

        var reasons = new List<string>
        {
            $"+{valueGain:0.0} projected points over {drop.PlayerName}.",
            $"Projection confidence {projection.Confidence}%.",
            $"Same position ({PlayerPresentation.PositionLabel(player.Position)}) — direct roster-spot swap, no lineup restructuring."
        };

        if (HasProductionBackedProjection(projection))
        {
            reasons.Add("Production-backed projection inputs support a credible NFL role.");
        }
        else if (HasYouthUpsideSignal(player))
        {
            reasons.Add("Limited NFL production sample, but early-career/young upside data supports consideration.");
        }

        return new PickupCandidate
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            PositionLabel = PlayerPresentation.PositionLabel(player.Position),
            ProjectedPoints = points,
            Confidence = projection.Confidence,
            PickupValueScore = Math.Round(pickupValue, 2),
            Reasons = reasons
        };
    }

    private static DropPickupReport Empty(
        PersonalizedAnalysisContext context,
        string message,
        DateTimeOffset now,
        League? league = null,
        FantasyTeam? team = null) =>
        new()
        {
            LeagueId = context.LeagueId,
            SelectedRosterId = context.SelectedRosterId,
            LeagueName = context.LeagueName,
            TeamName = context.TeamName ?? team?.DisplayName ?? "No team selected",
            IsSetupComplete = context.IsSetupComplete,
            HasRosterPlayers = false,
            RosterLimit = league?.RosterLimit,
            RosterCount = team?.CountedPlayerIds.Count ?? 0,
            AvailablePlayerCount = 0,
            Suggestions = [],
            UnavailableSignals = [],
            StatusMessage = message,
            GeneratedAt = now
        };
}
