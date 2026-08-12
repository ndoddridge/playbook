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
/// vs. the best available same-position free agent), projection confidence, and positional depth
/// on the roster. In dynasty leagues, Keep Value also applies an explainable age / early-career
/// adjustment from data already on <see cref="Player"/> so small weekly projection edges cannot
/// alone justify dropping young or high-upside roster pieces. Never fabricates ownership beyond
/// known league rosters, waiver priority, betting lines, news, draft capital, or matchup
/// information — those signals are simply absent from every candidate rather than guessed at.
/// </summary>
public sealed class DropPickupService : IDropPickupService
{
    // Modest, explainable nudges around the core replacement-value signal (see BuildReasons).
    private const double ConfidenceWeightPerPoint = 0.08;
    private const double ThinPositionBonus = 6.0;
    private const double ShallowPositionBonus = 2.0;
    private const double StarterBonus = 2.0;
    private const int MaxSuggestions = 3;
    private const int MaxDropCandidatesConsidered = 8;

    // Dynasty keep-value: large enough that a small weekly projection edge cannot alone surface
    // a young/early-career player as a drop, but not so large that aging low-upside pieces are
    // frozen on the roster forever.
    private const double DynastyEarlyCareerYears0 = 14.0;
    private const double DynastyEarlyCareerYears1 = 11.0;
    private const double DynastyEarlyCareerYears2 = 7.0;
    private const double DynastyEarlyCareerYears3 = 3.0;
    private const double DynastyYoungAgeBonus = 8.0;
    private const double DynastyNearYoungAgeBonus = 4.0;
    private const double DynastyAgingPenalty = 3.0;
    private const double DynastyOlderPenalty = 6.0;
    private const double DynastyLowConfidenceYouthBonus = 2.0;
    private const double DynastyInjuredYouthBonus = 3.0;
    /// <summary>Dynasty keep bonus at/above this requires a larger value-gain to recommend a drop.</summary>
    private const double DynastyProtectedKeepThreshold = 8.0;
    /// <summary>Minimum projected-points gain to drop a dynasty-protected player.</summary>
    private const double DynastyProtectedMinValueGain = 10.0;

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
                .Where(x => x.Projection is not null)
                .OrderByDescending(x => (double)x.Projection!.ProjectedFantasyPoints)
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

            // Dynasty: a small weekly projection edge must not auto-justify dropping a protected
            // young/early-career piece. Large upgrades can still clear the bar.
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
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StatusMessage = suggestions.Count == 0
                ? $"No improving same-position swap found for {rosterRows.Count} roster players against " +
                  $"{allPlayers.Count - rosteredElsewhere.Count} available players."
                : $"{suggestions.Count} suggested swap(s) for {context.DisplayLabel}." +
                  (rosterLimitStatus is { IsKnown: true, IsOverLimit: true }
                      ? $" Note: {rosterLimitStatus.Message}"
                      : string.Empty),
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
        var dynastyReasons = new List<string>();
        var dynastyKeep = isDynasty
            ? ComputeDynastyKeepAdjustment(player, confidence, dynastyReasons)
            : 0.0;
        var keepValue =
            (replacementMargin ?? -100) + // unknown projection is treated as maximally expendable
            confidenceAdjustment +
            scarcityBonus +
            (isStarter ? StarterBonus : 0.0) +
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
            reasons.Add($"{positionDepth} {player.Position}s already on the roster — depth is not a concern here.");
        }
        else if (positionDepth <= 1)
        {
            reasons.Add($"Only {player.Position} on the roster — dropping leaves a positional hole.");
        }

        if (isStarter)
        {
            reasons.Add("Currently a starter.");
        }

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
            ReplacementMargin = replacementMargin is null ? null : Math.Round(replacementMargin.Value, 1),
            PositionDepthOnRoster = positionDepth,
            Reasons = reasons
        };
    }

    /// <summary>
    /// Dynasty-only Keep Value adjustment from data already present on <see cref="Player"/> /
    /// the projection. Draft round/pick are not on the player model and are never invented —
    /// early-career <see cref="Player.YearsPro"/> is the available career-capital proxy.
    /// </summary>
    private static double ComputeDynastyKeepAdjustment(
        Player player,
        int? confidence,
        List<string> reasons)
    {
        var adjustment = 0.0;
        var youthSignal = false;

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
                reasons.Add($"Dynasty early-career keep ({yearsPro} years pro).");
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
                reasons.Add($"Dynasty young-player keep (age {age}).");
            }
            else if (age <= youngCutoff)
            {
                adjustment += DynastyNearYoungAgeBonus;
                youthSignal = true;
                reasons.Add($"Dynasty near-prime keep (age {age}).");
            }
            else if (age >= agingStart + 3)
            {
                adjustment -= DynastyOlderPenalty;
                reasons.Add($"Dynasty aging risk (age {age}) — lower long-term keep.");
            }
            else if (age >= agingStart)
            {
                adjustment -= DynastyAgingPenalty;
                reasons.Add($"Dynasty aging risk (age {age}) — modest keep discount.");
            }
        }

        // Low confidence on a youth piece: weekly projection is a weak reason to abandon upside.
        if (youthSignal && confidence is int conf && conf < 45)
        {
            adjustment += DynastyLowConfidenceYouthBonus;
            reasons.Add($"Dynasty: low weekly confidence ({conf}%) does not erase early-career upside.");
        }

        // Temporary injury status on a youth piece should not alone make them the drop.
        if (youthSignal && IsTemporarilyUnavailable(player.Status))
        {
            adjustment += DynastyInjuredYouthBonus;
            reasons.Add($"Dynasty: {player.Status} status treated as temporary for a young/early-career piece.");
        }

        return adjustment;
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
        var confidenceAdjustment = (projection.Confidence - 50) * ConfidenceWeightPerPoint;
        var pickupValue = points + confidenceAdjustment;

        var reasons = new List<string>
        {
            $"+{valueGain:0.0} projected points over {drop.PlayerName}.",
            $"Projection confidence {projection.Confidence}%.",
            $"Same position ({PlayerPresentation.PositionLabel(player.Position)}) — direct roster-spot swap, no lineup restructuring."
        };

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
