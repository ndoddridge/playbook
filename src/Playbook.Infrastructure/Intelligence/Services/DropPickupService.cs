using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Drop/Pickup intelligence composed entirely from existing projection + roster data. Never
/// reads or writes <c>IDecisionEngine</c>/knowledge state, so it cannot disturb Start/Sit or
/// Quick Picks. Ranking separates two concepts: ImmediateValue (this week's replacement margin,
/// confidence, positional scarcity, starter status — same for every league type) and DynastyValue
/// (age, role, injury trajectory, scarcity, waiver-replaceability — Dynasty leagues only, and
/// deliberately excludes raw projected points so a single week can't dominate it). Never
/// fabricates ownership beyond known league rosters, waiver priority, betting lines, news,
/// matchup information, draft capital, or trade/market value — those signals are simply absent
/// from every candidate rather than guessed at.
/// </summary>
public sealed class DropPickupService : IDropPickupService
{
    // ImmediateValue: modest, explainable nudges around the core replacement-value signal.
    private const double ConfidenceWeightPerPoint = 0.08;
    private const double ThinPositionBonus = 6.0;
    private const double ShallowPositionBonus = 2.0;
    private const double StarterBonus = 2.0;

    // DynastyValue: long-horizon signal, applied only when the league is Dynasty. Deliberately
    // does not weight raw projected points (see class doc) — only confidence, at a smaller
    // weight than ImmediateValue's, stands in for short-term role certainty.
    private const double DynastyImmediateDampening = 0.5;
    private const double DynastyRoleBonus = 3.5;
    private const double DynastyThinPositionBonus = 3.0;
    private const double DynastyShallowPositionBonus = 1.0;
    private const double DynastyConfidenceWeightPerPoint = 0.04;
    private const double DynastyWaiverStrongMargin = 5.0;
    private const double DynastyWaiverStrongBonus = 4.0;
    private const double DynastyWaiverModerateMargin = 2.0;
    private const double DynastyWaiverModerateBonus = 2.0;
    private const double DynastyMinorInjuryPenalty = -1.0;
    private const double DynastyModerateInjuryPenalty = -2.5;
    private const double DynastySignificantInjuryPenalty = -5.0;
    private const double DynastyMajorInjuryPenalty = -8.0;
    private const double DynastyUnknownSeverityInjuryPenalty = -2.0;

    /// <summary>
    /// Bounds the raw per-severity injury penalty above. A current injury is exactly what makes a
    /// player's real-world platform status (Out/IR/PUP) and this week's projection collapse —
    /// ImmediateValue already reflects that. Stacking an uncapped severity penalty on top in
    /// DynastyValue as well double-counts the same temporary absence and can by itself erase a
    /// valuable young asset's long-horizon value, contradicting this class's own design intent
    /// (a single week/injury should never dominate DynastyValue).
    /// </summary>
    private const double DynastyInjuryPenaltyCap = -2.0;

    // Roster context: positional surplus/deficit and this-roster age distribution, Dynasty only.
    // "Normal" depth = this team's own current starters at the position (real roster data) plus a
    // generic bench-depth allowance by position — RB/WR commonly roster deeper than their raw
    // starter count suggests because FLEX pulls from either, so a fixed "1 bench per starter"
    // would falsely flag ordinary RB/WR depth as surplus. QB/TE need much less. This is general
    // fantasy roster-construction knowledge (the same kind already behind ThinPositionBonus /
    // ShallowPositionBonus above), not a per-league setting or player-specific exception.
    private const int MinimumNormalAllowance = 2;
    private const double SurplusPenaltyPerExcessPlayer = -2.5;

    /// <summary>
    /// Bounds the raw surplus penalty's magnitude (mirrors <see cref="RelativeAgePressureCap"/> for
    /// age) so positional depth alone can never spiral without limit as a position gets deeper.
    /// </summary>
    private const double SurplusPenaltyCap = -6.0;

    /// <summary>
    /// A player who currently holds a starting lineup spot is, by definition, not excess bench
    /// depth this week — surplus measures bench burden, not starter quality. Current starters keep
    /// only a fraction of the (already capped) surplus penalty so positional depth alone can never
    /// push a legitimate starter into Drop-Competitive; genuine age/injury/confidence decline still
    /// can, since only the surplus component is dampened here.
    /// </summary>
    private const double StarterSurplusProtectionFactor = 0.25;

    private const double RelativeAgePressurePerYear = -0.5;
    private const double RelativeAgePressureCap = 6.0;

    private const double HoldThreshold = 3.0;
    private const double DropThreshold = -3.0;

    private const int MaxSuggestions = 3;
    private const int MaxDropCandidatesConsidered = 8;

    private readonly ILeagueState _leagueState;
    private readonly IPlayerService _players;
    private readonly IProjectionService _projections;
    private readonly IPlayerInjuryService _injuries;
    private readonly object _gate = new();

    private DropPickupReport? _cached;
    private PersonalizedAnalysisContext _cachedContext;

    public DropPickupService(
        ILeagueState leagueState,
        IPlayerService players,
        IProjectionService projections,
        IPlayerInjuryService injuries)
    {
        _leagueState = leagueState;
        _players = players;
        _projections = projections;
        _injuries = injuries;
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
        var reserveSet = team.ReservePlayerIds.ToHashSet();
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

        // Players already placed in an IR/reserve slot (real platform data — team.ReservePlayerIds)
        // occupy a separate roster mechanism, not a normal bench spot, so they don't compete for
        // positional depth the way an active bench player does. Excluding them here means the
        // depth/surplus/age-average signals reflect the ACTIVE roster only — matching how taxi
        // squad is already excluded from the roster-limit count.
        var activeRosterRows = rosterRows.Where(p => !reserveSet.Contains(p.Id)).ToList();

        var depthByPosition = activeRosterRows
            .GroupBy(p => p.Position)
            .ToDictionary(g => g.Key, g => g.Count());

        // Roster context: how many of THIS position are currently started (from the connected
        // league's actual lineup) and the position's average known age on THIS roster — both
        // derived only from real roster data, never an invented league setting or player-specific
        // exception.
        var startersByPosition = activeRosterRows
            .Where(p => starterSet.Contains(p.Id))
            .GroupBy(p => p.Position)
            .ToDictionary(g => g.Key, g => g.Count());

        var averageAgeByPosition = activeRosterRows
            .Where(p => p.Age is not null)
            .GroupBy(p => p.Position)
            .ToDictionary(g => g.Key, g => g.Average(p => p.Age!.Value));

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
            .Select(player =>
            {
                var positionDepth = depthByPosition.GetValueOrDefault(player.Position);
                var starters = startersByPosition.GetValueOrDefault(player.Position);
                var normalAllowance = Math.Max(starters + NormalBenchAllowance(player.Position), MinimumNormalAllowance);
                var positionSurplus = Math.Max(0, positionDepth - normalAllowance);
                double? ageDelta = player.Age is int age && averageAgeByPosition.TryGetValue(player.Position, out var avgAge)
                    ? age - avgAge
                    : null;

                return BuildDropCandidate(
                    player,
                    starterSet.Contains(player.Id),
                    reserveSet.Contains(player.Id),
                    projectionByPlayer.GetValueOrDefault(player.Id),
                    positionDepth,
                    freeAgentsByPosition.GetValueOrDefault(player.Position),
                    isDynasty,
                    _injuries.GetCurrentInjury(player.Id),
                    positionSurplus,
                    ageDelta);
            })
            .OrderBy(c => c.KeepValueScore)
            .ToList();

        // Dynasty leagues: a swap-for-immediate-upgrade suggestion is only appropriate for players
        // whose KeepValueScore (which already blends DynastyValue, including roster-context surplus
        // and relative-age pressure) actually classifies as DropCompetitive. Protected/Trade dynasty
        // assets must never be offered as a same-week cut merely because a hotter waiver option
        // exists this week — that's exactly how a talented young player with a soft week gets
        // mislabeled as expendable. Redraft/Keeper leagues have no long-horizon value to protect,
        // so their existing (ungated) behavior is unchanged.
        // A player already on IR/reserve is never offered as a drop-for-swap in any league type —
        // that roster spot isn't a normal bench slot competing for depth, so "drop him for a waiver
        // upgrade" doesn't reflect what actually happens on the platform.
        var swapCandidatePool = (isDynasty
            ? dropCandidates.Where(c => c.Classification == DropPickupClassification.DropCompetitive)
            : dropCandidates)
            .Where(c => !reserveSet.Contains(c.PlayerId))
            .ToList();

        var suggestions = new List<DropPickupSuggestion>();
        var usedPickupIds = new HashSet<Guid>();

        foreach (var drop in swapCandidatePool.Take(MaxDropCandidatesConsidered))
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

        // Dynasty leagues: players with meaningful-but-replaceable dynasty value are surfaced as
        // trade candidates instead of silently disappearing — they are not droppable, but they are
        // not "just hold forever" either. Never padded to a fixed count.
        var tradeCandidates = isDynasty
            ? dropCandidates.Where(c => c.Classification == DropPickupClassification.Trade)
                .Take(MaxSuggestions)
                .ToList()
            : [];

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
            RosterAssessment = dropCandidates,
            TradeCandidates = tradeCandidates,
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StatusMessage = suggestions.Count == 0
                ? (tradeCandidates.Count > 0
                    ? $"No genuine drop candidates for {context.DisplayLabel} — {tradeCandidates.Count} " +
                      "player(s) carry meaningful dynasty value and are better traded than cut."
                    : $"No improving same-position swap found for {rosterRows.Count} roster players against " +
                      $"{allPlayers.Count - rosteredElsewhere.Count} available players.")
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
        bool isReserve,
        PlayerProjection? projection,
        int positionDepth,
        List<(Player Player, PlayerProjection? Projection)>? freeAgentsAtPosition,
        bool isDynasty,
        PlayerInjuryRecord? currentInjury,
        int positionSurplus,
        double? ageDeltaFromPositionAverage)
    {
        var ownPoints = projection is null ? (double?)null : (double)projection.ProjectedFantasyPoints;
        var confidence = projection?.Confidence;
        var bestFreeAgent = freeAgentsAtPosition?.FirstOrDefault();
        var replacementLevel = bestFreeAgent?.Projection is { } fa ? (double)fa.ProjectedFantasyPoints : 0.0;
        double? replacementMargin = ownPoints is null ? null : ownPoints.Value - replacementLevel;

        var scarcityBonus = positionDepth <= 1 ? ThinPositionBonus : positionDepth == 2 ? ShallowPositionBonus : 0.0;
        var confidenceAdjustment = confidence is int c ? (c - 50) * ConfidenceWeightPerPoint : 0.0;
        var immediateValue =
            (replacementMargin ?? -100) + // unknown projection is treated as maximally expendable
            confidenceAdjustment +
            scarcityBonus +
            (isStarter ? StarterBonus : 0.0);

        double? dynastyValue = null;
        double? rosterPressure = null;
        var scoreBreakdown = new List<string>
        {
            $"Immediate value {immediateValue:0.00}: replacement {(replacementMargin ?? -100):0.0;-0.0;0.0}, " +
            $"confidence {confidenceAdjustment:0.00;-0.00;0.00}, scarcity {scarcityBonus:0.0}, " +
            $"starter {(isStarter ? StarterBonus : 0.0):0.0}."
        };

        if (isDynasty)
        {
            var ageComponent = DynastyAgeComponent(player.Age);
            var roleComponent = isStarter ? DynastyRoleBonus : 0.0;
            var injuryComponent = DynastyInjuryComponent(currentInjury);
            var dynastyScarcityBonus =
                positionDepth <= 1 ? DynastyThinPositionBonus : positionDepth == 2 ? DynastyShallowPositionBonus : 0.0;
            var dynastyConfidenceAdjustment =
                confidence is int dc ? (dc - 50) * DynastyConfidenceWeightPerPoint : 0.0;
            var waiverComponent = DynastyWaiverComponent(replacementMargin);

            // Roster context: surplus depth at this position (beyond this team's own starters +
            // normal bench) makes every player there more expendable; being older than the
            // position's average age on THIS roster compounds that, being younger offsets it.
            // Purely roster-derived — no player-specific exception, no invented league setting.
            // The surplus term is bounded (SurplusPenaltyCap) and further dampened for a current
            // starter (StarterSurplusProtectionFactor) so positional depth alone — however deep —
            // can never by itself overwhelm a legitimate starter's role/confidence baseline. A
            // player already on IR/reserve (real platform data, see reserveSet in BuildReport)
            // isn't competing for bench room at all, so surplus pressure doesn't apply to them.
            var rawSurplusPressure = Math.Max(SurplusPenaltyCap, positionSurplus * SurplusPenaltyPerExcessPlayer);
            var surplusPressure = isReserve ? 0.0
                : isStarter ? rawSurplusPressure * StarterSurplusProtectionFactor
                : rawSurplusPressure;
            var relativeAgePressure = ageDeltaFromPositionAverage is { } delta
                ? Math.Clamp(delta * RelativeAgePressurePerYear, -RelativeAgePressureCap, RelativeAgePressureCap)
                : 0.0;
            rosterPressure = surplusPressure + relativeAgePressure;

            dynastyValue = ageComponent + roleComponent + injuryComponent +
                dynastyScarcityBonus + dynastyConfidenceAdjustment + waiverComponent + rosterPressure.Value;

            scoreBreakdown.Add(
                $"Dynasty value {dynastyValue:0.00}: age {ageComponent:0.0;-0.0;0.0}, role {roleComponent:0.0}, " +
                $"injury {injuryComponent:0.0;-0.0;0.0}, scarcity {dynastyScarcityBonus:0.0}, " +
                $"confidence {dynastyConfidenceAdjustment:0.00;-0.00;0.00}, waiver {waiverComponent:0.0}, " +
                $"roster pressure {rosterPressure:0.00;-0.00;0.00} ({positionSurplus} surplus at position, " +
                $"age {(ageDeltaFromPositionAverage is { } d ? $"{d:+0.0;-0.0;0.0} yrs vs position avg" : "unknown")}).");
        }

        var keepValue = isDynasty
            ? (immediateValue * DynastyImmediateDampening) + dynastyValue!.Value
            : immediateValue;

        var classification = keepValue switch
        {
            >= HoldThreshold => DropPickupClassification.Protected,
            <= DropThreshold => DropPickupClassification.DropCompetitive,
            _ => DropPickupClassification.Trade
        };

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

        if (isDynasty && positionSurplus > 0)
        {
            reasons.Add(
                $"{positionDepth} {player.Position}s rostered — {positionSurplus} beyond this team's own " +
                "starters + normal bench depth at the position.");
        }
        else if (positionDepth > 2)
        {
            reasons.Add($"{positionDepth} {player.Position}s already on the roster — depth is not a concern here.");
        }
        else if (positionDepth <= 1)
        {
            reasons.Add($"Only {player.Position} on the roster — dropping leaves a positional hole.");
        }

        if (isDynasty && ageDeltaFromPositionAverage is { } ageDelta && Math.Abs(ageDelta) >= 2.0)
        {
            reasons.Add(ageDelta > 0
                ? $"{ageDelta:0.0} years older than this roster's average {player.Position} — aging within a crowded group."
                : $"{Math.Abs(ageDelta):0.0} years younger than this roster's average {player.Position}.");
        }

        if (isStarter)
        {
            reasons.Add("Currently a starter.");
        }

        return new DropCandidate
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            PositionLabel = PlayerPresentation.PositionLabel(player.Position),
            IsStarter = isStarter,
            ProjectedPoints = ownPoints,
            Confidence = confidence,
            KeepValueScore = Math.Round(keepValue, 2),
            ImmediateValue = Math.Round(immediateValue, 2),
            DynastyValue = dynastyValue is null ? null : Math.Round(dynastyValue.Value, 2),
            RosterPressure = rosterPressure is null ? null : Math.Round(rosterPressure.Value, 2),
            Classification = classification,
            ScoreBreakdown = scoreBreakdown,
            ReplacementMargin = replacementMargin is null ? null : Math.Round(replacementMargin.Value, 1),
            PositionDepthOnRoster = positionDepth,
            Reasons = reasons
        };
    }

    /// <summary>
    /// Generic bench-depth allowance per starting slot at a position, used only to detect surplus
    /// on THIS roster (combined with this team's own current starter count) — not a league setting.
    /// RB/WR get more because FLEX makes deeper rosters normal there; QB/TE need little bench.
    /// </summary>
    private static int NormalBenchAllowance(Position position) => position switch
    {
        Position.RB => 3,
        Position.WR => 3,
        Position.QB => 1,
        Position.TE => 1,
        _ => 2
    };

    /// <summary>
    /// Coarse dynasty age curve. Bucketed rather than continuous so a single year of age never
    /// swings value sharply; missing age contributes 0 (neutral), never a penalty.
    /// </summary>
    private static double DynastyAgeComponent(int? age) => age switch
    {
        null => 0.0,
        <= 23 => 6.0,
        <= 26 => 3.0,
        <= 29 => 0.0,
        <= 32 => -3.0,
        _ => -6.0
    };

    /// <summary>
    /// Bounded (DynastyInjuryPenaltyCap) so a temporary injury — even a severe one — never comes
    /// close to erasing the other DynastyValue components by itself; ImmediateValue already carries
    /// the short-term production hit. No current injury on file contributes 0, not a penalty.
    /// </summary>
    private static double DynastyInjuryComponent(PlayerInjuryRecord? currentInjury)
    {
        if (currentInjury is null)
        {
            return 0.0;
        }

        var raw = currentInjury.Severity switch
        {
            InjurySeverity.Minor => DynastyMinorInjuryPenalty,
            InjurySeverity.Moderate => DynastyModerateInjuryPenalty,
            InjurySeverity.Significant => DynastySignificantInjuryPenalty,
            InjurySeverity.Major => DynastyMajorInjuryPenalty,
            _ => DynastyUnknownSeverityInjuryPenalty
        };

        return Math.Max(DynastyInjuryPenaltyCap, raw);
    }

    /// <summary>
    /// Packet's decision principle: a player who'd be a strong waiver target if released is
    /// evidence against dropping them. Bounded (unlike ImmediateValue's raw replacement margin)
    /// so it nudges rather than dominates DynastyValue.
    /// </summary>
    private static double DynastyWaiverComponent(double? replacementMargin) => replacementMargin switch
    {
        null => 0.0,
        > DynastyWaiverStrongMargin => DynastyWaiverStrongBonus,
        > DynastyWaiverModerateMargin => DynastyWaiverModerateBonus,
        _ => 0.0
    };

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
            RosterAssessment = [],
            TradeCandidates = [],
            UnavailableSignals = [],
            StatusMessage = message,
            GeneratedAt = now
        };
}
