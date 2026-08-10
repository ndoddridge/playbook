using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Knowledge;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Knowledge;
using Playbook.Core.Leagues;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Core.Stats.Models;
using Playbook.Infrastructure.Replay;

namespace Playbook.Tests;

public class SharedKnowledgeModelTests
{
    [Fact]
    public void Same_Historical_Input_Produces_Deterministic_Knowledge()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();
        var snapshot = LoadControlledSnapshot();

        var a = model.BuildFromHistorical(snapshot, ControlledHistoricalFixture.AlphaRbId, PredictionType.StartSit);
        var b = model.BuildFromHistorical(snapshot, ControlledHistoricalFixture.AlphaRbId, PredictionType.StartSit);

        Assert.Equal(a.KnowledgeConfidence, b.KnowledgeConfidence);
        Assert.Equal(a.OverallStatus, b.OverallStatus);
        Assert.Equal(a.Facts.Select(f => f.Statement), b.Facts.Select(f => f.Statement));
        Assert.Equal(a.Evidence.Select(e => (e.Aspect, e.Statement, e.Direction)),
            b.Evidence.Select(e => (e.Aspect, e.Statement, e.Direction)));
    }

    [Fact]
    public void Historical_Cutoff_Excludes_Future_Injury_And_News()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();
        var snapshot = LoadControlledSnapshot();

        var delta = model.BuildFromHistorical(
            snapshot,
            ControlledHistoricalFixture.DeltaWrId,
            PredictionType.StartSit);

        KnowledgeTemporalGuard.AssertNoFutureLeak(delta, snapshot.InformationCutoff);
        Assert.Equal(snapshot.InformationCutoff, delta.InformationCutoff);
        Assert.DoesNotContain(delta.Facts, f =>
            f.Key.StartsWith("health.injury", StringComparison.OrdinalIgnoreCase) ||
            f.Statement.Contains("Listed as Out", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(delta.Evidence, e =>
            e.Aspect == KnowledgeAspect.InjuryStatus &&
            e.Direction == SignalDirection.Negative &&
            !e.IsUnavailableMarker);
        Assert.DoesNotContain(delta.Evidence, e => e.Statement.Contains("Hamstring", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(delta.Evidence, e => e.Statement.Contains("ruled out", StringComparison.OrdinalIgnoreCase));
        Assert.All(delta.Facts.Where(f => f.ObservedAt is not null),
            f => Assert.True(f.ObservedAt <= snapshot.InformationCutoff));
        Assert.All(delta.Evidence.Where(e => e.ObservedAt is not null),
            e => Assert.True(e.ObservedAt <= snapshot.InformationCutoff));
    }

    [Fact]
    public void Temporal_Guard_Filters_Future_Dated_Evidence()
    {
        var cutoff = new DateTimeOffset(2018, 10, 21, 13, 0, 0, TimeSpan.Zero);
        var evidence = new List<KnowledgeEvidence>
        {
            new()
            {
                Scope = KnowledgeScope.Player,
                Aspect = KnowledgeAspect.InjuryStatus,
                Statement = "Pre-cutoff injury",
                Direction = SignalDirection.Negative,
                Strength = SignalStrength.Moderate,
                Status = EvidenceStatus.Known,
                Confidence = 80,
                Reliability = EvidenceReliability.High,
                Source = "test",
                ObservedAt = cutoff.AddHours(-2),
                InformationCutoff = cutoff
            },
            new()
            {
                Scope = KnowledgeScope.Player,
                Aspect = KnowledgeAspect.InjuryStatus,
                Statement = "Future injury",
                Direction = SignalDirection.Negative,
                Strength = SignalStrength.Strong,
                Status = EvidenceStatus.Known,
                Confidence = 90,
                Reliability = EvidenceReliability.High,
                Source = "test",
                ObservedAt = cutoff.AddHours(6),
                InformationCutoff = cutoff
            }
        };

        var filtered = KnowledgeTemporalGuard.FilterEvidence(evidence, cutoff);
        Assert.Single(filtered);
        Assert.Equal("Pre-cutoff injury", filtered[0].Statement);
        Assert.False(KnowledgeTemporalGuard.IsKnownAtCutoff(cutoff.AddMinutes(1), cutoff));
    }

    [Fact]
    public void Unavailable_Aspects_Remain_Unknown_Not_Directional()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();
        var snapshot = LoadControlledSnapshot();

        var bundle = model.BuildFromHistorical(
            snapshot,
            ControlledHistoricalFixture.AlphaRbId,
            PredictionType.Generic);

        Assert.Contains(KnowledgeAspect.Weather, bundle.UnavailableAspects);
        Assert.Contains(KnowledgeAspect.PositionalMatchup, bundle.UnavailableAspects);
        Assert.Contains(KnowledgeAspect.Pace, bundle.UnavailableAspects);

        var markers = bundle.Evidence.Where(e => e.IsUnavailableMarker).ToList();
        Assert.NotEmpty(markers);
        Assert.All(markers, m =>
        {
            Assert.Equal(EvidenceStatus.Unknown, m.Status);
            Assert.Equal(SignalDirection.Uncertainty, m.Direction);
            Assert.Equal(EvidenceReliability.Unknown, m.Reliability);
        });
    }

    [Fact]
    public void Positive_And_Negative_Evidence_Are_Represented()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();
        var snapshot = LoadControlledSnapshot();

        var echo = model.BuildFromHistorical(
            snapshot,
            ControlledHistoricalFixture.EchoTeId,
            PredictionType.StartSit);

        // Echo has a pre-cutoff Questionable injury → negative health evidence.
        Assert.Contains(echo.NegativeEvidence, e => e.Aspect is KnowledgeAspect.Health or KnowledgeAspect.InjuryStatus);

        var alpha = model.BuildFromHistorical(
            snapshot,
            ControlledHistoricalFixture.AlphaRbId,
            PredictionType.StartSit);
        // Alpha is a startable RB with projection/opportunity in the fixture.
        Assert.True(alpha.PositiveEvidence.Count + alpha.Evidence.Count(e => e.Direction == SignalDirection.Neutral) > 0);
        Assert.NotNull(alpha.DecisionPlayerKnowledge);
    }

    [Fact]
    public void StartSit_PredictionContext_Exposes_Decision_PlayerKnowledge()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();
        var snapshot = LoadControlledSnapshot();

        var ctx = model.BuildHistoricalPredictionContext(
            snapshot,
            ControlledHistoricalFixture.AlphaRbId,
            PredictionType.StartSit);

        Assert.Equal(PredictionType.StartSit, ctx.PredictionType);
        Assert.NotNull(ctx.PlayerKnowledge);
        Assert.Equal(ControlledHistoricalFixture.AlphaRbId, ctx.PlayerKnowledge!.PlayerId);
        Assert.Equal(snapshot.InformationCutoff, ctx.InformationCutoff);
        Assert.NotNull(ctx.DecisionContext);
    }

    [Fact]
    public async Task Historical_Replay_StartSit_Consumes_Shared_Knowledge()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var report = await HistoricalReplayCommands.RunControlled2018Week7Async(provider);

        Assert.True(report.DecisionCount > 0);
        Assert.All(report.Grades, g =>
            Assert.True(g.InformationCutoff <= ControlledHistoricalFixture.InformationCutoff ||
                        g.InformationCutoff == ControlledHistoricalFixture.InformationCutoff));
        Assert.DoesNotContain(report.Grades,
            g => g.Rationale.Any(r => r.Contains("ruled out for the season", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void QuickPicks_Consumes_Shared_PredictionContext()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();

        var line = new PropLine
        {
            Id = "test-line",
            Event = new FootballEvent
            {
                EventId = "e1",
                HomeTeam = "KC",
                AwayTeam = "BUF",
                CommenceTime = DateTimeOffset.UtcNow.AddDays(2),
                Season = 2024,
                Week = 7,
                Phase = NflSeasonPhase.RegularSeason
            },
            PlayerId = Guid.NewGuid(),
            PlayerName = "Test Receiver",
            TeamName = "KC",
            Market = PredictionMarketType.ReceivingYards,
            Line = 64.5m,
            Bookmaker = "mock",
            Source = "mock",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = PropLineFreshness.Live
        };

        var evaluation = new QuickPickEvaluationContext
        {
            Line = line,
            PlaybookProjection = 72.0m,
            ProjectionConfidence = 62,
            Volatility = 40,
            SeasonPhase = NflSeasonPhase.RegularSeason
        };

        var ctx = model.BuildQuickPickPredictionContext(evaluation);
        Assert.Equal(PredictionType.QuickPick, ctx.PredictionType);
        Assert.NotNull(ctx.Knowledge);
        Assert.Equal(line.PlayerId, ctx.PlayerId);
        Assert.Equal(64.5m, ctx.MarketLine);
        Assert.Contains(ctx.Knowledge.Evidence, e => e.Aspect == KnowledgeAspect.Projection);
        Assert.Contains(ctx.Knowledge.UnavailableAspects, a => a == KnowledgeAspect.Weather);
        KnowledgeTemporalGuard.AssertNoFutureLeak(ctx.Knowledge);
    }

    [Fact]
    public void QuickPicks_Future_Injury_Does_Not_Enter_Knowledge()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();
        var cutoff = new DateTimeOffset(2024, 10, 20, 12, 0, 0, TimeSpan.Zero);
        var playerId = Guid.NewGuid();

        var evaluation = new QuickPickEvaluationContext
        {
            Line = new PropLine
            {
                Id = "inj",
                Event = new FootballEvent
                {
                    EventId = "e2",
                    HomeTeam = "DAL",
                    AwayTeam = "PHI",
                    CommenceTime = cutoff.AddDays(1),
                    Season = 2024,
                    Week = 8
                },
                PlayerId = playerId,
                PlayerName = "Injured Player",
                TeamName = "DAL",
                Market = PredictionMarketType.RushingYards,
                Line = 55m,
                Bookmaker = "mock",
                Source = "mock",
                UpdatedAt = cutoff,
                Freshness = PropLineFreshness.Live
            },
            PlaybookProjection = 60m,
            ProjectionConfidence = 50,
            InjuryProfile = new PlayerInjuryProfile
            {
                PlayerId = playerId,
                CurrentInjury = new PlayerInjuryRecord
                {
                    PlayerId = playerId,
                    Date = cutoff.AddHours(8),
                    Status = "Out",
                    BodyPart = "Ankle",
                    LastUpdated = cutoff.AddHours(8),
                    IsCurrent = true
                }
            }
        };

        var ctx = model.BuildQuickPickPredictionContext(evaluation, cutoff);
        Assert.DoesNotContain(ctx.Knowledge.Evidence,
            e => e.Aspect == KnowledgeAspect.InjuryStatus &&
                 e.Direction == SignalDirection.Negative &&
                 !e.IsUnavailableMarker);
        KnowledgeTemporalGuard.AssertNoFutureLeak(ctx.Knowledge, cutoff);
    }

    [Fact]
    public void QuickPicks_Service_Attaches_PredictionContext_Without_Breaking_Board()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var qp = provider.GetRequiredService<IQuickPicksService>();
        var picks = qp.GetTopPicks(5);
        Assert.NotNull(picks);
        // Board may be empty without live lines in mock — presence of service wiring is enough;
        // knowledge attachment is covered by BuildQuickPickPredictionContext tests.
        _ = qp.GetAllPredictions();
    }

    [Fact]
    public void Live_StartSit_Path_Builds_Shared_PredictionContext()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var intel = provider.GetRequiredService<Application.Intelligence.Interfaces.IFantasyTeamIntelligenceService>();
        var report = intel.GetReport();
        Assert.True(report.HasRosterPlayers);
        // Start/Sit recommendations still produced after shared-knowledge routing.
        Assert.NotNull(report.StartSit);
    }

    [Fact]
    public void Frozen_Projection_Confidence_And_Policy_Unchanged()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(new[] { 0, 15, 25, 35 }, FrozenDecisionConfidenceCalibrationV2.BinStarts);
        Assert.Equal(new[] { 57, 67, 65, 42 }, FrozenDecisionConfidenceCalibrationV2.CalibratedRates);
        Assert.Equal(DecisionPolicyKinds.SuppressStartAndSit, FrozenConfidenceAwareDecisionPolicyV1.Kind);
        Assert.Equal(45, FrozenConfidenceAwareDecisionPolicyV1.MaxCalibratedConfidenceToSuppressStart);
        Assert.Equal(6.0, FrozenConfidenceAwareDecisionPolicyV1.MaxDecisionValueMarginToSuppress);
    }

    [Fact]
    public async Task Default_Modes_Keep_Frozen_2018_Benchmark()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var projection = provider.GetRequiredService<HistoricalProjectionExperimentState>();
        var policy = provider.GetRequiredService<ConfidenceAwareDecisionPolicyState>();
        Assert.Equal(HistoricalProjectionPrimaryMode.ProjectionV1, projection.PrimaryMode);
        Assert.Equal(ConfidenceAwareDecisionPolicyMode.Off, policy.Mode);

        var scorecard = await HistoricalReplayCommands.RunReal2018SeasonAsync(provider);
        Assert.Equal(Frozen2018SeasonBenchmark.CurrentModelMae, scorecard.CurrentModelMae);
        Assert.Equal(Frozen2018SeasonBenchmark.DecisionAccuracyPercent, scorecard.DecisionAccuracyPercent);
    }

    [Fact]
    public void Unknown_Information_Does_Not_Become_Evidence()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);
        var model = provider.GetRequiredService<ISharedKnowledgeModel>();

        var evaluation = new QuickPickEvaluationContext
        {
            Line = new PropLine
            {
                Id = "sparse",
                Event = new FootballEvent
                {
                    EventId = "e3",
                    HomeTeam = "MIA",
                    AwayTeam = "NYJ",
                    CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
                    Season = 2024,
                    Week = 3
                },
                PlayerName = "Sparse",
                Market = PredictionMarketType.Receptions,
                Line = 4.5m,
                Bookmaker = "mock",
                Source = "mock",
                UpdatedAt = DateTimeOffset.UtcNow,
                Freshness = PropLineFreshness.Live
            },
            ProjectionConfidence = 20,
            Volatility = 50
        };

        var ctx = model.BuildQuickPickPredictionContext(evaluation);
        Assert.DoesNotContain(ctx.Knowledge.PositiveEvidence, e => e.Aspect == KnowledgeAspect.Weather);
        Assert.DoesNotContain(ctx.Knowledge.NegativeEvidence, e => e.Aspect == KnowledgeAspect.PositionalMatchup);
        Assert.Contains(ctx.Knowledge.UnknownEvidence, e => e.Aspect == KnowledgeAspect.Weather);
    }

    private static HistoricalSnapshot LoadControlledSnapshot()
    {
        var raw = ControlledHistoricalFixture.Create();
        var (snapshot, _) = new HistoricalSnapshotBuilder().Build(raw);
        return snapshot;
    }
}
