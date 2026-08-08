using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Recommendations;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Core.Recommendations;

namespace Playbook.Infrastructure.Recommendations;

/// <summary>
/// League- and roster-aware mock recommendations. Rebuilds whenever the active
/// league or owned team changes so personalized UI never keeps the previous context.
/// </summary>
public sealed class MockRecommendationService : IRecommendationService
{
    private readonly ILeagueState _leagueState;
    private readonly IProjectionService _projectionService;
    private readonly IPlayerService _playerService;
    private readonly object _gate = new();

    private IReadOnlyList<Recommendation> _recommendations = [];
    private PersonalizedAnalysisContext _cachedContext;

    public MockRecommendationService(
        ILeagueState leagueState,
        IProjectionService projectionService,
        IPlayerService playerService)
    {
        _leagueState = leagueState;
        _projectionService = projectionService;
        _playerService = playerService;
        _leagueState.Changed += OnLeagueContextChanged;
    }

    public IReadOnlyList<Recommendation> GetRecommendations()
    {
        EnsureCurrent();
        return _recommendations;
    }

    public IReadOnlyList<Recommendation> GetTopRecommendations(int count = 5)
    {
        EnsureCurrent();
        return _recommendations
            .Where(r => r.MatchesContext(_cachedContext.LeagueId, _cachedContext.SelectedRosterId))
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.Confidence)
            .Take(Math.Max(0, count))
            .ToList();
    }

    private void OnLeagueContextChanged()
    {
        lock (_gate)
        {
            _recommendations = [];
            _cachedContext = default;
        }
    }

    private void EnsureCurrent()
    {
        var context = PersonalizedAnalysisContext.FromState(_leagueState);
        if (_recommendations.Count > 0 &&
            context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
            context.ScoringType == _cachedContext.ScoringType &&
            context.Week == _cachedContext.Week)
        {
            return;
        }

        lock (_gate)
        {
            context = PersonalizedAnalysisContext.FromState(_leagueState);
            if (_recommendations.Count > 0 &&
                context.Matches(_cachedContext.LeagueId, _cachedContext.SelectedRosterId) &&
                context.ScoringType == _cachedContext.ScoringType &&
                context.Week == _cachedContext.Week)
            {
                return;
            }

            _recommendations = BuildRecommendations(context);
            _cachedContext = context;
        }
    }

    private IReadOnlyList<Recommendation> BuildRecommendations(PersonalizedAnalysisContext context)
    {
        var now = DateTimeOffset.Now;
        if (context.LeagueId is null)
        {
            return
            [
                Stamp(new Recommendation
                {
                    Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                    Title = "Select a league to personalize",
                    Summary = "Connect a Sleeper league or choose a demo league to unlock roster-aware decisions.",
                    ActionType = RecommendationType.Hold,
                    Priority = RecommendationPriority.Medium,
                    Confidence = 100,
                    Impact = "Setup required",
                    Category = RecommendationCategory.Roster,
                    Status = RecommendationStatus.Watching,
                    Reasoning = "Playbook needs an active league context before it can generate personalized recommendations.",
                    SupportingSignals = ["No league selected"],
                    Evidence = ["Open the league switcher to continue."],
                    LastUpdated = now,
                    SourceEngine = EngineType.Decision
                }, context)
            ];
        }

        if (!context.IsSetupComplete)
        {
            return
            [
                Stamp(new Recommendation
                {
                    Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                    Title = "Pick your team to finish setup",
                    Summary = $"Choose your roster in {context.LeagueName} so Playbook can personalize lineup and roster advice.",
                    ActionType = RecommendationType.Hold,
                    Priority = RecommendationPriority.Critical,
                    Confidence = 100,
                    Impact = "Setup incomplete",
                    Category = RecommendationCategory.Roster,
                    Status = RecommendationStatus.Active,
                    Reasoning = "Live Sleeper leagues require an owned-team selection before roster-scoped recommendations are generated.",
                    SupportingSignals = ["League loaded", "Owned team not selected"],
                    Evidence = ["Use the league switcher team list to finish setup."],
                    LastUpdated = now,
                    SourceEngine = EngineType.Decision
                }, context)
            ];
        }

        var team = _leagueState.CurrentUserTeam;
        var rosterIds = team?.PlayerIds ?? [];
        if (rosterIds.Count > 0)
        {
            return BuildRosterRecommendations(context, team!, rosterIds, now);
        }

        return BuildDemoRecommendations(context, now);
    }

    private IReadOnlyList<Recommendation> BuildRosterRecommendations(
        PersonalizedAnalysisContext context,
        FantasyTeam team,
        IReadOnlyList<Guid> rosterIds,
        DateTimeOffset now)
    {
        var rosterProjections = _projectionService.ProjectRoster(rosterIds);
        if (rosterProjections.Count == 0)
        {
            return BuildDemoRecommendations(context, now);
        }

        var ranked = rosterProjections
            .Select(p => (Proj: p, Player: _playerService.GetPlayer(p.PlayerId)))
            .Where(x => x.Player is not null)
            .OrderByDescending(x => x.Proj.ProjectedFantasyPoints)
            .ThenBy(x => x.Player!.FullName)
            .ToList();

        var top = ranked[0];
        var floor = ranked[^1];
        var mid = ranked[Math.Min(ranked.Count / 2, ranked.Count - 1)];
        var teamLabel = context.TeamName ?? team.DisplayName;
        var scoring = FormatScoring(context.ScoringType);

        var list = new List<Recommendation>
        {
            Stamp(new Recommendation
            {
                Id = StableId(context, "start"),
                Title = $"Start {top.Player!.FullName}",
                Summary = $"Top projected player on {teamLabel} this week in {scoring}.",
                ActionType = RecommendationType.Start,
                Priority = RecommendationPriority.Critical,
                Confidence = Math.Clamp(top.Proj.Confidence, 55, 95),
                Impact = $"+{top.Proj.ProjectedFantasyPoints:0.0} expected pts",
                Category = RecommendationCategory.Lineup,
                Status = RecommendationStatus.Active,
                Reasoning =
                    $"{top.Player.FullName} leads your {teamLabel} roster at {top.Proj.ProjectedFantasyPoints:0.0} projected points " +
                    $"under {context.LeagueName} scoring.",
                SupportingSignals =
                [
                    $"Roster rank #1 of {ranked.Count} on {teamLabel}",
                    $"League scoring: {scoring}",
                    $"Confidence {top.Proj.Confidence}%"
                ],
                Evidence =
                [
                    $"Projection: {top.Proj.ProjectedFantasyPoints:0.0} (floor {top.Proj.Floor:0.0} / ceiling {top.Proj.Ceiling:0.0})",
                    $"Generated for {context.DisplayLabel}"
                ],
                FutureNotes = "Revisit if injury or inactive status changes before lock.",
                LastUpdated = now,
                SourceEngine = EngineType.Decision,
                RelatedPlayerId = top.Player.Id
            }, context),
            Stamp(new Recommendation
            {
                Id = StableId(context, "hold"),
                Title = $"Hold {mid.Player!.FullName}",
                Summary = $"Keep {mid.Player.FullName} on {teamLabel} — solid mid-roster projection this week.",
                ActionType = RecommendationType.Hold,
                Priority = RecommendationPriority.High,
                Confidence = Math.Clamp(mid.Proj.Confidence - 4, 50, 90),
                Impact = "Roster continuity",
                Category = RecommendationCategory.Roster,
                Status = RecommendationStatus.Watching,
                Reasoning =
                    $"{mid.Player.FullName} remains a useful piece for {teamLabel} in {context.LeagueName} " +
                    $"with {mid.Proj.ProjectedFantasyPoints:0.0} projected points.",
                SupportingSignals =
                [
                    $"Mid-roster projection on {teamLabel}",
                    $"Week {context.Week} · {scoring}"
                ],
                Evidence =
                [
                    $"Projection: {mid.Proj.ProjectedFantasyPoints:0.0}",
                    $"Generated for {context.DisplayLabel}"
                ],
                LastUpdated = now,
                SourceEngine = EngineType.Projection,
                RelatedPlayerId = mid.Player.Id
            }, context)
        };

        if (!Equals(floor.Player!.Id, top.Player!.Id))
        {
            list.Add(Stamp(new Recommendation
            {
                Id = StableId(context, "bench"),
                Title = $"Bench {floor.Player.FullName}",
                Summary = $"Lowest projected player on {teamLabel} under current {scoring} settings.",
                ActionType = RecommendationType.Bench,
                Priority = RecommendationPriority.Medium,
                Confidence = Math.Clamp(floor.Proj.Confidence - 8, 45, 85),
                Impact = "Reduce weekly volatility",
                Category = RecommendationCategory.Lineup,
                Status = RecommendationStatus.Active,
                Reasoning =
                    $"{floor.Player.FullName} currently projects {floor.Proj.ProjectedFantasyPoints:0.0} points — " +
                    $"the softest outlook on {teamLabel} this week.",
                SupportingSignals =
                [
                    $"Lowest of {ranked.Count} roster projections",
                    $"League: {context.LeagueName}"
                ],
                Evidence =
                [
                    $"Projection: {floor.Proj.ProjectedFantasyPoints:0.0}",
                    $"Generated for {context.DisplayLabel}"
                ],
                LastUpdated = now,
                SourceEngine = EngineType.Decision,
                RelatedPlayerId = floor.Player.Id
            }, context));
        }

        list.Add(Stamp(new Recommendation
        {
            Id = StableId(context, "review"),
            Title = $"Review {teamLabel} lineup",
            Summary = $"All personalized decisions below are scoped to {context.DisplayLabel}.",
            ActionType = RecommendationType.Hold,
            Priority = RecommendationPriority.Low,
            Confidence = 80,
            Impact = "Context check",
            Category = RecommendationCategory.Lineup,
            Status = RecommendationStatus.Watching,
            Reasoning =
                $"Playbook regenerated roster analysis for {teamLabel} after the latest league/team selection. " +
                "Previous league recommendations were discarded.",
            SupportingSignals =
            [
                $"{ranked.Count} roster players projected",
                $"Scoring {scoring} · Week {context.Week}"
            ],
            Evidence = [$"Context stamp: league {context.LeagueId}, roster {context.SelectedRosterId}"],
            LastUpdated = now,
            SourceEngine = EngineType.Decision
        }, context));

        return list;
    }

    private IReadOnlyList<Recommendation> BuildDemoRecommendations(
        PersonalizedAnalysisContext context,
        DateTimeOffset now)
    {
        // Demo leagues have empty ownership — still stamp + vary by league/team so switches never look identical.
        var seed = HashCode.Combine(context.LeagueId, context.SelectedRosterId, context.ScoringType);
        var teamLabel = context.TeamName ?? "your demo team";
        var scoring = FormatScoring(context.ScoringType);
        var catalog = _playerService.GetAllPlayers()
            .OrderBy(p => p.Id)
            .Take(40)
            .ToList();
        Player? Pick(int offset) =>
            catalog.Count == 0 ? null : catalog[Math.Abs(seed + offset) % catalog.Count];

        var startPlayer = Pick(1);
        var holdPlayer = Pick(7);
        var benchPlayer = Pick(13);

        return
        [
            Stamp(new Recommendation
            {
                Id = StableId(context, "demo-start"),
                Title = startPlayer is null ? "Start your top option" : $"Start {startPlayer.FullName}",
                Summary = $"Lineup lean for {teamLabel} in {context.LeagueName} ({scoring}).",
                ActionType = RecommendationType.Start,
                Priority = RecommendationPriority.Critical,
                Confidence = 70 + Math.Abs(seed % 20),
                Impact = "Weekly edge",
                Category = RecommendationCategory.Lineup,
                Status = RecommendationStatus.Active,
                Reasoning =
                    $"Demo recommendation regenerated for {context.DisplayLabel}. " +
                    "Connect a live Sleeper roster for true owned-team lineup analysis.",
                SupportingSignals =
                [
                    $"Scoped to {teamLabel}",
                    $"League scoring: {scoring}"
                ],
                Evidence = [$"Generated for {context.DisplayLabel}"],
                LastUpdated = now,
                SourceEngine = EngineType.Decision,
                RelatedPlayerId = startPlayer?.Id
            }, context),
            Stamp(new Recommendation
            {
                Id = StableId(context, "demo-hold"),
                Title = holdPlayer is null ? "Hold a mid-roster piece" : $"Hold {holdPlayer.FullName}",
                Summary = $"Roster guidance for {teamLabel} — not carried over from another league.",
                ActionType = RecommendationType.Hold,
                Priority = RecommendationPriority.High,
                Confidence = 65 + Math.Abs(seed % 18),
                Impact = "Roster continuity",
                Category = RecommendationCategory.Roster,
                Status = RecommendationStatus.Watching,
                Reasoning = $"Hold guidance is tied to {context.DisplayLabel} and refreshes when you change league or team.",
                SupportingSignals = [$"Active team: {teamLabel}", $"Week {context.Week}"],
                Evidence = [$"Generated for {context.DisplayLabel}"],
                LastUpdated = now,
                SourceEngine = EngineType.Projection,
                RelatedPlayerId = holdPlayer?.Id
            }, context),
            Stamp(new Recommendation
            {
                Id = StableId(context, "demo-bench"),
                Title = benchPlayer is null ? "Bench a volatile flex" : $"Bench {benchPlayer.FullName}",
                Summary = $"Volatility lean for {teamLabel} under {scoring}.",
                ActionType = RecommendationType.Bench,
                Priority = RecommendationPriority.Medium,
                Confidence = 60 + Math.Abs(seed % 15),
                Impact = "Lower risk",
                Category = RecommendationCategory.Lineup,
                Status = RecommendationStatus.Active,
                Reasoning = $"Bench lean regenerated for {context.DisplayLabel}; previous-team advice was cleared.",
                SupportingSignals = [$"Scoped to {context.LeagueName}", scoring],
                Evidence = [$"Generated for {context.DisplayLabel}"],
                LastUpdated = now,
                SourceEngine = EngineType.Decision,
                RelatedPlayerId = benchPlayer?.Id
            }, context)
        ];
    }

    private static Recommendation Stamp(Recommendation recommendation, PersonalizedAnalysisContext context) =>
        new()
        {
            Id = recommendation.Id,
            Title = recommendation.Title,
            Summary = recommendation.Summary,
            ActionType = recommendation.ActionType,
            Priority = recommendation.Priority,
            Confidence = recommendation.Confidence,
            Impact = recommendation.Impact,
            Category = recommendation.Category,
            Status = recommendation.Status,
            Reasoning = recommendation.Reasoning,
            SupportingSignals = recommendation.SupportingSignals,
            Evidence = recommendation.Evidence,
            FutureNotes = recommendation.FutureNotes,
            LastUpdated = recommendation.LastUpdated,
            SourceEngine = recommendation.SourceEngine,
            RelatedPlayerId = recommendation.RelatedPlayerId,
            LeagueId = context.LeagueId,
            SelectedRosterId = context.SelectedRosterId,
            LeagueName = context.LeagueName,
            TeamName = context.TeamName,
            IsExpanded = recommendation.IsExpanded,
            Metadata = recommendation.Metadata
        };

    private static Guid StableId(PersonalizedAnalysisContext context, string slot)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"playbook:rec:{context.LeagueId:N}:{context.SelectedRosterId}:{slot}"));
        return new Guid(bytes);
    }

    private static string FormatScoring(ScoringType scoring) => scoring switch
    {
        ScoringType.Ppr => "PPR",
        ScoringType.HalfPpr => "Half PPR",
        ScoringType.Standard => "Standard",
        _ => scoring.ToString()
    };
}
