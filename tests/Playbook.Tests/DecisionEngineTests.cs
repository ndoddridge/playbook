using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Abstractions;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;

namespace Playbook.Tests;

public class DecisionEngineTests
{
    [Fact]
    public async Task Compose_Does_Not_Invent_Missing_Projection()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var players = provider.GetRequiredService<IPlayerService>();
        var composer = provider.GetRequiredService<IPlayerKnowledgeComposer>();
        var leagues = provider.GetRequiredService<ILeagueState>();

        var player = players.GetAllPlayers().First();
        var context = DecisionContext.FromLeague(leagues.CurrentLeague, leagues.CurrentUserTeam);

        var knowledge = await composer.ComposeAsync(player, context);

        Assert.Equal(player.Id, knowledge.PlayerId);
        Assert.NotEmpty(knowledge.Facts);
        Assert.NotEmpty(knowledge.Signals);
        Assert.All(knowledge.Facts, f => Assert.False(string.IsNullOrWhiteSpace(f.Statement)));
        // Missing evidence is explicit when present — never silently filled.
        Assert.All(knowledge.MissingEvidence, m => Assert.False(string.IsNullOrWhiteSpace(m)));
    }

    [Fact]
    public async Task Evaluate_Separates_Facts_Inferences_And_Decision()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var players = provider.GetRequiredService<IPlayerService>();
        var composer = provider.GetRequiredService<IPlayerKnowledgeComposer>();
        var engine = provider.GetRequiredService<IDecisionEngine>();
        var leagues = provider.GetRequiredService<ILeagueState>();

        var player = players.GetAllPlayers().First(p => p.Position is Core.Players.Position.RB or Core.Players.Position.WR);
        var context = DecisionContext.FromLeague(leagues.CurrentLeague, leagues.CurrentUserTeam);
        var knowledge = await composer.ComposeAsync(player, context);
        var result = await engine.EvaluatePlayerAsync(knowledge, context);

        Assert.NotEqual(Guid.Empty, result.DecisionId);
        Assert.NotEmpty(result.Facts);
        Assert.Contains(result.Recommendation, new[]
        {
            DecisionRecommendation.Start,
            DecisionRecommendation.Sit,
            DecisionRecommendation.Watch,
            DecisionRecommendation.NoAction
        });
        Assert.InRange(result.Confidence, 1, 100);
        Assert.True(result.Values.ExpectedValue >= 0);
        Assert.False(string.IsNullOrWhiteSpace(result.Values.MethodologyNote));
        Assert.NotEmpty(result.Rationale);
    }

    [Fact]
    public async Task DecisionValue_Can_Favor_Lower_Projection_With_Higher_Confidence()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var engine = provider.GetRequiredService<IDecisionEngine>();
        var leagues = provider.GetRequiredService<ILeagueState>();
        var context = DecisionContext.FromLeague(leagues.CurrentLeague, leagues.CurrentUserTeam);

        var stable = Knowledge(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Stable Starter",
            projected: 18.5m,
            knowledgeConfidence: 78,
            opportunity: 70,
            usage: 68,
            overall: EvidenceStatus.Known,
            healthPositive: true,
            coverageGap: false);

        var boom = Knowledge(
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            "Boom Uncertainty",
            projected: 21.0m,
            knowledgeConfidence: 28,
            opportunity: 55,
            usage: 50,
            overall: EvidenceStatus.LowConfidence,
            healthPositive: false,
            coverageGap: true,
            missing: ["Recent production trend", "Role", "Health certainty"]);

        var (preferred, other) = await engine.ComparePlayersAsync(stable, boom, context);

        Assert.Equal(stable.PlayerId, preferred.PlayerId);
        Assert.True(preferred.Values.DecisionValue > other.Values.DecisionValue);
        Assert.True(preferred.Values.ExpectedValue < other.Values.ExpectedValue);
        Assert.True(other.IsProvisional || other.Confidence <= preferred.Confidence);
    }

    [Fact]
    public async Task StartSit_Comes_From_Central_Engine_And_Records_Decisions()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var intel = provider.GetRequiredService<IFantasyTeamIntelligenceService>();
        var store = provider.GetRequiredService<IDecisionRecordStore>();
        var engine = provider.GetRequiredService<IDecisionEngine>();
        var composer = provider.GetRequiredService<IPlayerKnowledgeComposer>();
        var players = provider.GetRequiredService<IPlayerService>();
        var leagues = provider.GetRequiredService<ILeagueState>();

        var report = intel.GetReport();
        Assert.True(report.HasRosterPlayers);
        Assert.All(report.StartSit, rec =>
        {
            Assert.InRange(rec.Confidence, 1, 100);
            Assert.NotEmpty(rec.Reasons);
            if (rec.InsufficientData)
            {
                Assert.True(rec.Confidence <= 55);
            }
        });

        var team = leagues.CurrentUserTeam!;
        var context = DecisionContext.FromLeague(leagues.CurrentLeague, team);
        var rosterPlayers = team.PlayerIds
            .Select(id => players.GetPlayer(id))
            .Where(p => p is not null)
            .Cast<Core.Players.Player>()
            .Take(4)
            .ToList();

        var knowledge = await composer.ComposeManyAsync(rosterPlayers, context);
        var candidates = rosterPlayers
            .Select((p, i) => new StartSitCandidate
            {
                PlayerId = p.Id,
                PlayerName = p.FullName,
                Position = p.Position,
                IsStarter = i == 0
            })
            .ToList();

        var batch = await engine.EvaluateStartSitAsync(knowledge, candidates, context);
        Assert.NotEmpty(batch.Decisions);
        Assert.All(batch.Decisions, d =>
        {
            Assert.NotEmpty(d.Facts);
            Assert.NotNull(d.Inferences);
            Assert.NotNull(d.SupportingEvidence);
            Assert.NotNull(d.OpposingEvidence);
        });

        var recorded = await store.ListAsync(context.Season, context.Week);
        Assert.NotEmpty(recorded);
        Assert.All(recorded, r =>
        {
            Assert.Null(r.ActualOutcome);
            Assert.Null(r.EvaluationResult);
            Assert.Equal(context.Week, r.Week);
        });
    }

    [Fact]
    public async Task Low_Information_Player_Remains_Low_Confidence()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var engine = provider.GetRequiredService<IDecisionEngine>();
        var leagues = provider.GetRequiredService<ILeagueState>();
        var context = DecisionContext.FromLeague(leagues.CurrentLeague, leagues.CurrentUserTeam);

        var sparse = Knowledge(
            Guid.NewGuid(),
            "Sparse Signal",
            projected: 14.0m,
            knowledgeConfidence: 30,
            opportunity: null,
            usage: null,
            overall: EvidenceStatus.LowConfidence,
            healthPositive: false,
            coverageGap: true,
            missing: ["Opportunity", "Usage", "Recent production trend", "Role"]);

        var result = await engine.EvaluatePlayerAsync(sparse, context);

        Assert.True(result.IsProvisional);
        Assert.True(result.Confidence <= 55);
        Assert.NotEmpty(result.Unknowns);
        Assert.Contains(result.EvidenceStatus, new[] { EvidenceStatus.LowConfidence, EvidenceStatus.Unknown });
    }

    private static PlayerKnowledge Knowledge(
        Guid id,
        string name,
        decimal? projected,
        int knowledgeConfidence,
        int? opportunity,
        int? usage,
        EvidenceStatus overall,
        bool healthPositive,
        bool coverageGap,
        IReadOnlyList<string>? missing = null)
    {
        var signals = new List<KnowledgeSignal>();
        if (projected is not null)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Projection,
                Value = (double)projected.Value,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = knowledgeConfidence,
                Status = knowledgeConfidence >= 50 ? EvidenceStatus.Known : EvidenceStatus.LowConfidence,
                Source = "test",
                Explanation = $"Projection {projected:0.0}",
                ObservedAt = DateTimeOffset.UtcNow,
                Category = "Projection"
            });
        }

        if (opportunity is int o)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Opportunity,
                Value = o,
                Direction = o >= 60 ? SignalDirection.Positive : SignalDirection.Neutral,
                Strength = SignalStrength.Moderate,
                Confidence = 70,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = $"Opportunity {o}",
                ObservedAt = DateTimeOffset.UtcNow,
                Category = "Opportunity"
            });
        }

        if (usage is int u)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Usage,
                Value = u,
                Direction = u >= 60 ? SignalDirection.Positive : SignalDirection.Neutral,
                Strength = SignalStrength.Moderate,
                Confidence = 70,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = $"Usage {u}",
                ObservedAt = DateTimeOffset.UtcNow,
                Category = "Usage"
            });
        }

        if (healthPositive)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Value = 80,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 75,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = "Healthy",
                ObservedAt = DateTimeOffset.UtcNow,
                Category = "Health"
            });
        }

        if (coverageGap)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Coverage,
                Value = knowledgeConfidence,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Strong,
                Confidence = 80,
                Status = EvidenceStatus.LowConfidence,
                Source = "test",
                Explanation = "Limited intelligence coverage",
                ObservedAt = DateTimeOffset.UtcNow,
                Category = "Coverage"
            });
        }

        return new PlayerKnowledge
        {
            PlayerId = id,
            PlayerName = name,
            PositionLabel = "RB",
            Facts =
            [
                new KnowledgeFact
                {
                    Key = "projection.points",
                    Statement = projected is null ? "No projection" : $"Projected {projected:0.0}",
                    Source = "test",
                    ObservedAt = DateTimeOffset.UtcNow,
                    Status = projected is null ? EvidenceStatus.Unknown : EvidenceStatus.Known
                }
            ],
            Signals = signals,
            OverallStatus = overall,
            KnowledgeConfidence = knowledgeConfidence,
            MissingEvidence = missing?.ToList() ?? [],
            GeneratedAt = DateTimeOffset.UtcNow,
            InformationCutoff = null,
            ProjectedPoints = projected,
            Floor = projected is null ? null : projected - 4,
            Ceiling = projected is null ? null : projected + 6,
            ProjectionConfidence = knowledgeConfidence,
            OpportunityScore = opportunity,
            UsageScore = usage,
            HealthLabel = healthPositive ? "Healthy" : "Limited information"
        };
    }
}
