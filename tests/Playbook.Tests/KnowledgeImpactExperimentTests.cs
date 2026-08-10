using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Knowledge;
using Playbook.Application.Players.Data;
using Playbook.Core.Decisions;
using Playbook.Core.Knowledge;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class KnowledgeImpactExperimentTests
{
    [Fact]
    public void Baseline_Strips_Contextual_Knowledge_Groups()
    {
        var source = SampleKnowledge(opportunity: 80, usage: 75, recentProd: 70, healthy: true, role: "Featured RB1");
        var baseline = KnowledgeImpactApplicator.ToBaseline(source);

        Assert.Null(baseline.OpportunityScore);
        Assert.Null(baseline.UsageScore);
        Assert.Null(baseline.HealthLabel);
        Assert.DoesNotContain(baseline.Signals, s => s.Type == SignalType.Opportunity);
        Assert.DoesNotContain(baseline.Signals, s => s.Type == SignalType.Usage);
        Assert.DoesNotContain(baseline.Signals, s => s.Type == SignalType.RecentProduction);
        Assert.DoesNotContain(baseline.Signals, s => s.Type == SignalType.Role);
        Assert.DoesNotContain(baseline.Signals, s => s.Type == SignalType.Health);
        Assert.Contains(baseline.Signals, s => s.Type == SignalType.Projection);
        Assert.Contains(baseline.MissingEvidence, m => m.Contains("Usage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Enhanced_Usage_Restores_Scores()
    {
        var source = SampleKnowledge(opportunity: 80, usage: 75, recentProd: 50, healthy: true, role: null);
        var enhanced = KnowledgeImpactApplicator.ToEnhanced(source, KnowledgeImpactGroup.Usage);

        Assert.Equal(80, enhanced.OpportunityScore);
        Assert.Equal(75, enhanced.UsageScore);
        Assert.Contains(enhanced.Signals, s => s.Type == SignalType.Opportunity);
        Assert.Contains(enhanced.Facts, f => f.Key == "knowledge_impact.transform");
    }

    [Fact]
    public void Enhanced_RecentForm_Applies_Bounded_Opportunity_Delta()
    {
        var high = SampleKnowledge(opportunity: 50, usage: 50, recentProd: 80, healthy: true, role: null);
        var low = SampleKnowledge(opportunity: 50, usage: 50, recentProd: 20, healthy: true, role: null);

        var highEnh = KnowledgeImpactApplicator.ToEnhanced(high, KnowledgeImpactGroup.RecentForm);
        var lowEnh = KnowledgeImpactApplicator.ToEnhanced(low, KnowledgeImpactGroup.RecentForm);

        Assert.Equal(50 + FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta, highEnh.OpportunityScore);
        Assert.Equal(50 - FrozenKnowledgeImpactExperimentV1.RecentFormOpportunityDelta, lowEnh.OpportunityScore);
    }

    [Fact]
    public void Enhanced_RoleHealth_Applies_Role_Delta_And_Keeps_Health()
    {
        var source = SampleKnowledge(opportunity: 50, usage: 50, recentProd: 50, healthy: false, role: "Featured RB1");
        var enhanced = KnowledgeImpactApplicator.ToEnhanced(source, KnowledgeImpactGroup.RoleHealth);

        Assert.Equal(50 + FrozenKnowledgeImpactExperimentV1.RoleOpportunityDelta, enhanced.OpportunityScore);
        Assert.Contains(enhanced.Signals, s => s.Type == SignalType.Health);
        Assert.Contains(enhanced.Signals, s => s.Type == SignalType.Role);
    }

    [Fact]
    public void Transforms_Are_Deterministic()
    {
        var source = SampleKnowledge(opportunity: 70, usage: 60, recentProd: 70, healthy: true, role: "starter");
        var a = KnowledgeImpactApplicator.ToEnhanced(source, KnowledgeImpactGroup.AllSupported);
        var b = KnowledgeImpactApplicator.ToEnhanced(source, KnowledgeImpactGroup.AllSupported);
        Assert.Equal(a.OpportunityScore, b.OpportunityScore);
        Assert.Equal(a.UsageScore, b.UsageScore);
        Assert.Equal(a.Signals.Select(s => (s.Type, s.Explanation)), b.Signals.Select(s => (s.Type, s.Explanation)));
    }

    [Fact]
    public void Passthrough_Mode_Is_Identity()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        var applicator = provider.GetRequiredService<IKnowledgeImpactApplicator>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);

        var source = SampleKnowledge(opportunity: 80, usage: 70, recentProd: 60, healthy: true, role: "WR1");
        var applied = applicator.ApplyToPlayerKnowledge(source);
        Assert.Same(source, applied);
    }

    [Fact]
    public void Baseline_And_Enhanced_Modes_Differ()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        var applicator = provider.GetRequiredService<IKnowledgeImpactApplicator>();
        var source = SampleKnowledge(opportunity: 80, usage: 70, recentProd: 70, healthy: true, role: "RB1");

        state.ConfigureBaseline();
        var baseline = applicator.ApplyToPlayerKnowledge(source);
        state.ConfigureEnhanced(KnowledgeImpactGroup.Usage);
        var enhanced = applicator.ApplyToPlayerKnowledge(source);

        Assert.Null(baseline.OpportunityScore);
        Assert.Equal(80, enhanced.OpportunityScore);
        state.ConfigurePassthrough();
    }

    [Fact]
    public void Missing_Knowledge_Does_Not_Invent_Matchup()
    {
        var source = SampleKnowledge(opportunity: null, usage: null, recentProd: null, healthy: true, role: null);
        var enhanced = KnowledgeImpactApplicator.ToEnhanced(source, KnowledgeImpactGroup.Usage);
        Assert.Null(enhanced.OpportunityScore);
        Assert.DoesNotContain(enhanced.Signals, s => s.Type == SignalType.MatchupContext);
    }

    [Fact]
    public void QuickPick_Enhanced_Adjusts_Opportunity_Score()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        var applicator = provider.GetRequiredService<IKnowledgeImpactApplicator>();

        var prediction = new Prediction
        {
            Id = Guid.NewGuid(),
            Event = new FootballEvent
            {
                EventId = "e",
                HomeTeam = "KC",
                AwayTeam = "BUF",
                CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
                Season = 2024,
                Week = 1
            },
            PlayerId = Guid.NewGuid(),
            PlayerName = "P",
            Market = PredictionMarketType.ReceivingYards,
            Line = 60m,
            PlaybookProjection = 70m,
            Probability = 55,
            Edge = 3m,
            Confidence = 60,
            Direction = PredictionDirection.Over,
            Reasoning = "test",
            SupportingIntelligence = [],
            CalculationNotes = [],
            Source = "test",
            LineFreshness = PropLineFreshness.Live,
            LastUpdated = DateTimeOffset.UtcNow,
            OpportunityScore = 50m
        };

        var ctx = new PredictionContext
        {
            PredictionType = PredictionType.QuickPick,
            Season = 2024,
            Week = 1,
            Knowledge = new SharedKnowledgeBundle
            {
                PlayerId = prediction.PlayerId,
                PlayerName = "P",
                Position = null,
                Season = 2024,
                Week = 1,
                GeneratedAt = DateTimeOffset.UtcNow,
                Facts = [],
                Evidence =
                [
                    new KnowledgeEvidence
                    {
                        Scope = KnowledgeScope.Player,
                        Aspect = KnowledgeAspect.Usage,
                        Statement = "High usage",
                        Direction = SignalDirection.Positive,
                        Strength = SignalStrength.Moderate,
                        Status = EvidenceStatus.Known,
                        Confidence = 70,
                        Reliability = EvidenceReliability.Moderate,
                        Source = "test"
                    }
                ],
                UnavailableAspects = [],
                UnavailableSources = [],
                OverallStatus = EvidenceStatus.Known,
                KnowledgeConfidence = 60
            },
            GeneratedAt = DateTimeOffset.UtcNow
        };

        state.ConfigureBaseline();
        var basePred = applicator.ApplyToQuickPickPrediction(prediction, ctx);
        Assert.Equal(50m, basePred.OpportunityScore);

        state.ConfigureEnhanced(KnowledgeImpactGroup.Usage);
        var enhPred = applicator.ApplyToQuickPickPrediction(prediction, ctx);
        Assert.True(enhPred.OpportunityScore > 50m);
        state.ConfigurePassthrough();
    }

    [Fact]
    public void Temporal_Cutoff_Preserved_On_Transform_Facts()
    {
        var cutoff = new DateTimeOffset(2018, 10, 16, 16, 0, 0, TimeSpan.Zero);
        var raw = SampleKnowledge(opportunity: 70, usage: 60, recentProd: 70, healthy: true, role: "starter");
        var source = new PlayerKnowledge
        {
            PlayerId = raw.PlayerId,
            PlayerName = raw.PlayerName,
            PositionLabel = raw.PositionLabel,
            Facts = raw.Facts.Select(f => new KnowledgeFact
            {
                Key = f.Key,
                Statement = f.Statement,
                Source = f.Source,
                ObservedAt = cutoff.AddHours(-1),
                Status = f.Status
            }).ToList(),
            Signals = raw.Signals.Select(s => new KnowledgeSignal
            {
                Type = s.Type,
                Value = s.Value,
                Direction = s.Direction,
                Strength = s.Strength,
                Confidence = s.Confidence,
                Status = s.Status,
                Source = s.Source,
                Explanation = s.Explanation,
                ObservedAt = cutoff.AddHours(-1),
                Category = s.Category
            }).ToList(),
            OverallStatus = raw.OverallStatus,
            KnowledgeConfidence = raw.KnowledgeConfidence,
            MissingEvidence = raw.MissingEvidence,
            GeneratedAt = cutoff,
            InformationCutoff = cutoff,
            ProjectedPoints = raw.ProjectedPoints,
            Floor = raw.Floor,
            Ceiling = raw.Ceiling,
            ProjectionConfidence = raw.ProjectionConfidence,
            OpportunityScore = raw.OpportunityScore,
            UsageScore = raw.UsageScore,
            HealthLabel = raw.HealthLabel
        };

        var enhanced = KnowledgeImpactApplicator.ToEnhanced(source, KnowledgeImpactGroup.Usage);
        Assert.All(enhanced.Facts.Where(f => f.Key == "knowledge_impact.transform"),
            f => Assert.True(f.ObservedAt <= cutoff));
        KnowledgeTemporalGuard.AssertNoFutureLeak(
            new SharedKnowledgeBundle
            {
                PlayerId = enhanced.PlayerId,
                PlayerName = enhanced.PlayerName,
                Position = null,
                Season = 2018,
                Week = 7,
                InformationCutoff = cutoff,
                GeneratedAt = cutoff,
                Facts = enhanced.Facts,
                Evidence = [],
                UnavailableAspects = [],
                UnavailableSources = [],
                OverallStatus = EvidenceStatus.Known,
                KnowledgeConfidence = enhanced.KnowledgeConfidence,
                DecisionPlayerKnowledge = enhanced
            },
            cutoff);
    }

    [Fact]
    public void Frozen_Layers_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(new[] { 0, 15, 25, 35 }, FrozenDecisionConfidenceCalibrationV2.BinStarts);
        Assert.Equal(new[] { 57, 67, 65, 42 }, FrozenDecisionConfidenceCalibrationV2.CalibratedRates);
        Assert.Equal(DecisionPolicyKinds.SuppressStartAndSit, FrozenConfidenceAwareDecisionPolicyV1.Kind);
        Assert.Equal(45, FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart);
    }

    [Fact]
    public async Task Default_Passthrough_Keeps_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var knowledge = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, knowledge.Mode);

        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
    }

    [Fact]
    public async Task Official_Knowledge_Impact_Experiment_Runs_Once()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunKnowledgeImpactExperimentAsync(provider);

        Assert.False(report.UsedHoldoutDuringFitting);
        Assert.True(report.ProjectionV2Unchanged);
        Assert.True(report.ConfidenceV2Unchanged);
        Assert.True(report.DecisionPolicyV1Unchanged);
        Assert.Equal(2024, report.HoldoutSeason);
        Assert.Equal(FrozenKnowledgeImpactExperimentV1.FrozenEnhancedGroups, report.FrozenGroups);
        Assert.False(report.FrozenGroups.HasFlag(KnowledgeImpactGroup.Matchup));

        var state = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        Assert.Equal(KnowledgeMode.Passthrough, state.Mode);

        var text = report.ToReportText();
        Assert.Contains("KNOWLEDGE IMPACT EXPERIMENT", text);
        Assert.Contains("OFFICIAL HOLDOUT 2024", text);
        Assert.Contains("VERDICT", text);
        Assert.Contains("Quick Picks", text);

        var outPath = Path.Combine(AppContext.BaseDirectory, "KNOWLEDGE_IMPACT_EXPERIMENT_V1_REPORT.txt");
        await File.WriteAllTextAsync(outPath, text);
        Assert.True(File.Exists(outPath));
    }

    private static PlayerKnowledge SampleKnowledge(
        int? opportunity,
        int? usage,
        int? recentProd,
        bool healthy,
        string? role)
    {
        var signals = new List<KnowledgeSignal>
        {
            new()
            {
                Type = SignalType.Projection,
                Value = 14,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 60,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = "Projection 14.0"
            }
        };
        var facts = new List<KnowledgeFact>
        {
            new()
            {
                Key = "projection.points",
                Statement = "Projected 14.0",
                Source = "test",
                ObservedAt = DateTimeOffset.UtcNow,
                Status = EvidenceStatus.Known
            }
        };

        if (opportunity is int opp)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Opportunity,
                Value = opp,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 60,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = $"Opportunity {opp}"
            });
            facts.Add(new KnowledgeFact
            {
                Key = "opportunity.score",
                Statement = $"Opportunity {opp}",
                Source = "test",
                Status = EvidenceStatus.Known
            });
        }

        if (usage is int use)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Usage,
                Value = use,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 60,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = $"Usage {use}"
            });
            facts.Add(new KnowledgeFact
            {
                Key = "usage.score",
                Statement = $"Usage {use}",
                Source = "test",
                Status = EvidenceStatus.Known
            });
        }

        if (recentProd is int prod)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.RecentProduction,
                Value = prod,
                Direction = prod >= 60 ? SignalDirection.Positive : SignalDirection.Negative,
                Strength = SignalStrength.Moderate,
                Confidence = 60,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = $"Recent production {prod}"
            });
            facts.Add(new KnowledgeFact
            {
                Key = "production.recent",
                Statement = $"Recent production {prod}",
                Source = "test",
                Status = EvidenceStatus.Known
            });
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Role,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 65,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = role
            });
            facts.Add(new KnowledgeFact
            {
                Key = "role.signal",
                Statement = role,
                Source = "test",
                Status = EvidenceStatus.Known
            });
        }

        if (healthy)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 70,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = "Healthy"
            });
            facts.Add(new KnowledgeFact
            {
                Key = "health.label",
                Statement = "Healthy",
                Source = "test",
                Status = EvidenceStatus.Known
            });
        }
        else
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Direction = SignalDirection.Negative,
                Strength = SignalStrength.Moderate,
                Confidence = 80,
                Status = EvidenceStatus.Known,
                Source = "test",
                Explanation = "Questionable"
            });
            facts.Add(new KnowledgeFact
            {
                Key = "health.injury.current",
                Statement = "Listed as Questionable",
                Source = "test",
                Status = EvidenceStatus.Known
            });
        }

        return new PlayerKnowledge
        {
            PlayerId = Guid.NewGuid(),
            PlayerName = "Sample",
            PositionLabel = "RB",
            Facts = facts,
            Signals = signals,
            OverallStatus = EvidenceStatus.Known,
            KnowledgeConfidence = 60,
            MissingEvidence = [],
            GeneratedAt = DateTimeOffset.UtcNow,
            ProjectedPoints = 14m,
            Floor = 8m,
            Ceiling = 22m,
            ProjectionConfidence = 60,
            OpportunityScore = opportunity,
            UsageScore = usage,
            HealthLabel = healthy ? "Healthy" : "Questionable"
        };
    }
}
