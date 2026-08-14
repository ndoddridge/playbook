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
                isDynasty,
                _injuries.GetCurrentInjury(player.Id)))
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
            RosterAssessment = dropCandidates,
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
        bool isDynasty,
        PlayerInjuryRecord? currentInjury)
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

            dynastyValue = ageComponent + roleComponent + injuryComponent +
                dynastyScarcityBonus + dynastyConfidenceAdjustment + waiverComponent;

            scoreBreakdown.Add(
                $"Dynasty value {dynastyValue:0.00}: age {ageComponent:0.0;-0.0;0.0}, role {roleComponent:0.0}, " +
                $"injury {injuryComponent:0.0;-0.0;0.0}, scarcity {dynastyScarcityBonus:0.0}, " +
                $"confidence {dynastyConfidenceAdjustment:0.00;-0.00;0.00}, waiver {waiverComponent:0.0}.");
        }

        var keepValue = isDynasty
            ? (immediateValue * DynastyImmediateDampening) + dynastyValue!.Value
            : immediateValue;

        var classification = keepValue switch
        {
            >= HoldThreshold => DropPickupClassification.Hold,
            <= DropThreshold => DropPickupClassification.Drop,
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
            Classification = classification,
            ScoreBreakdown = scoreBreakdown,
            ReplacementMargin = replacementMargin is null ? null : Math.Round(replacementMargin.Value, 1),
            PositionDepthOnRoster = positionDepth,
            Reasons = reasons
        };
    }

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
    /// Bounded so a temporary/minor injury never comes close to erasing the other DynastyValue
    /// components. No current injury on file contributes 0, not a penalty.
    /// </summary>
    private static double DynastyInjuryComponent(PlayerInjuryRecord? currentInjury)
    {
        if (currentInjury is null)
        {
            return 0.0;
        }

        return currentInjury.Severity switch
        {
            InjurySeverity.Minor => DynastyMinorInjuryPenalty,
            InjurySeverity.Moderate => DynastyModerateInjuryPenalty,
            InjurySeverity.Significant => DynastySignificantInjuryPenalty,
            InjurySeverity.Major => DynastyMajorInjuryPenalty,
            _ => DynastyUnknownSeverityInjuryPenalty
        };
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
            UnavailableSignals = [],
            StatusMessage = message,
            GeneratedAt = now
        };
}
