using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

public class QuickPicksEngineTests
{
    private readonly QuickPicksEngine _engine = CreateEngine();

    [Fact]
    public void Engine_Version_Is_0_3()
    {
        Assert.Equal("0.3", _engine.Version);
        Assert.Equal(QuickPicksEngine.CurrentVersion, _engine.Version);
    }

    [Fact]
    public void Projection_Above_Line_Favors_Over()
    {
        var prediction = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(usage: 70, health: 75)));

        Assert.Equal(PredictionDirection.Over, prediction.Direction);
        Assert.True(prediction.Edge > 0);
        Assert.True(prediction.Probability > 50);
        Assert.Contains("above", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(prediction.SignalContributions);
        Assert.Equal("0.3", prediction.EngineVersion);
        Assert.Contains("Week", prediction.Event.SlateLabel, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(prediction.LineUpdatedAt);
    }

    [Fact]
    public void Projection_Below_Line_Favors_Under()
    {
        var prediction = Require(Eval(
            Line(PredictionMarketType.RushingYards, 90.5m),
            62.0m, 78, 30, Intel(usage: 45, health: 70)));

        Assert.Equal(PredictionDirection.Under, prediction.Direction);
        Assert.True(prediction.Edge > 0);
        Assert.Contains("below", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Projection_Equal_To_Line_Has_Minimal_Edge()
    {
        var prediction = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            94.5m, 70, 40, Intel()));

        Assert.True(prediction.Edge <= 1.5m);
        Assert.InRange(prediction.Probability, 45, 55);
    }

    [Fact]
    public void High_Confidence_Produces_Stronger_Edge_Than_Low_Confidence()
    {
        var high = Require(Eval(Line(PredictionMarketType.ReceivingYards, 94.5m), 108.2m, 90, 30, Intel(usage: 70, health: 80)));
        var low = Require(Eval(Line(PredictionMarketType.ReceivingYards, 94.5m), 108.2m, 25, 30, Intel(usage: 70, health: 80)));

        Assert.True(high.Edge > low.Edge);
        Assert.True(high.Confidence > low.Confidence);
    }

    [Fact]
    public void Low_Confidence_Edge_Is_Dampened()
    {
        var prediction = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 20, 40, Intel()));

        Assert.Contains(prediction.CalculationNotes, n => n.Contains("dampened", StringComparison.OrdinalIgnoreCase));
        Assert.True(prediction.Edge < 5m);
    }

    [Fact]
    public void High_Volatility_Reduces_Edge_Vs_Low_Volatility()
    {
        var calm = Require(Eval(Line(PredictionMarketType.RushingYards, 70.5m), 90m, 80, 20, Intel(health: 75)));
        var volatilePick = Require(Eval(Line(PredictionMarketType.RushingYards, 70.5m), 90m, 80, 90, Intel(health: 75)));
        Assert.True(calm.Edge > volatilePick.Edge);
    }

    [Fact]
    public void Missing_Line_Returns_Null()
    {
        Assert.Null(Eval(Line(PredictionMarketType.ReceivingYards, null), 100m, 70, 40, Intel()));
    }

    [Fact]
    public void Unavailable_Line_Returns_Null()
    {
        Assert.Null(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m, PropLineFreshness.Unavailable),
            108m, 70, 40, Intel()));
    }

    [Fact]
    public void Stale_Line_Is_Excluded_Not_Merely_Derated()
    {
        // A stale line isn't "current" — it must never surface as an actionable pick at all.
        var live = Require(Eval(Line(PredictionMarketType.ReceivingYards, 94.5m, PropLineFreshness.Mock), 108.2m, 80, 35, Intel(health: 75)));
        var stale = Eval(Line(PredictionMarketType.ReceivingYards, 94.5m, PropLineFreshness.Stale), 108.2m, 80, 35, Intel(health: 75));

        Assert.NotNull(live);
        Assert.Null(stale);
    }

    [Fact]
    public void Missing_Player_Intelligence_Reduces_Confidence_Without_Fabricating()
    {
        var withIntel = Require(Eval(Line(PredictionMarketType.ReceivingYards, 94.5m), 108.2m, 75, 40, Intel()));
        var missing = Require(Eval(Line(PredictionMarketType.ReceivingYards, 94.5m), 108.2m, 75, 40, intelligence: null));

        Assert.True(missing.Confidence < withIntel.Confidence);
        Assert.Contains(missing.SignalContributions, c =>
            c.SignalId == "intelligence-confidence" && !c.Available);
        Assert.Contains("Limited player intelligence", missing.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(missing.SupportingIntelligence, s =>
            s.Contains("Health score", StringComparison.OrdinalIgnoreCase) &&
            s.Contains("50", StringComparison.Ordinal));
    }

    [Fact]
    public void Current_Injury_Tempers_Over_More_Than_Healthy()
    {
        var healthy = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 80)));

        var injured = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 80),
            injuryProfile: InjuryProfile(
                current: CurrentInjury("Questionable", "ankle"))));

        Assert.True(injured.Edge < healthy.Edge);
        Assert.Contains(injured.SupportingIntelligence, s =>
            s.Contains("Current injury", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Current designation", injured.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unconfirmed_Buzz_Is_Labeled_And_Does_Not_Crash()
    {
        var prediction = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 70),
            injuryProfile: InjuryProfile(unconfirmed: true)));

        Assert.Contains(prediction.SupportingIntelligence, s =>
            s.StartsWith("Unconfirmed:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Unconfirmed", prediction.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(prediction.SignalContributions, c => c.IsUnconfirmed && c.Available);
        Assert.DoesNotContain(prediction.Reasoning, "ruled out", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Historical_Injury_Is_Age_Weighted_Weaker_Than_Current()
    {
        var withHistory = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 70),
            injuryProfile: InjuryProfile(historicalHigh: true)));

        // "Doubtful" rather than "Out"/"IR" — those now hard-exclude the pick entirely (see
        // QuickPicksParticipationGateTests), so they can no longer be compared here by edge.
        var withCurrent = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(health: 70),
            injuryProfile: InjuryProfile(current: CurrentInjury("Doubtful", "knee"))));

        Assert.True(withCurrent.Edge < withHistory.Edge);
        Assert.Contains(withHistory.SupportingIntelligence, s =>
            s.Contains("Relevant history", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Multiple_Markets_For_Same_Player_Produce_Distinct_Predictions()
    {
        var yards = Require(Eval(Line(PredictionMarketType.ReceivingYards, 94.5m, id: "chase-yds"), 108.2m, 80, 35, Intel(usage: 70)));
        var receptions = Require(Eval(Line(PredictionMarketType.Receptions, 6.5m, id: "chase-rec"), 7.8m, 78, 35, Intel(usage: 70)));
        Assert.NotEqual(yards.Id, receptions.Id);
    }

    [Fact]
    public void Prediction_Output_Is_Deterministic()
    {
        var line = Line(PredictionMarketType.ReceivingYards, 94.5m);
        var intel = Intel(usage: 68, health: 72, opportunity: 70);
        var a = Require(Eval(line, 108.2m, 82, 33, intel));
        var b = Require(Eval(line, 108.2m, 82, 33, intel));
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Direction, b.Direction);
        Assert.Equal(a.Edge, b.Edge);
        Assert.Equal(a.Probability, b.Probability);
        Assert.Equal(a.Confidence, b.Confidence);
        Assert.Equal(a.Reasoning, b.Reasoning);
        Assert.Equal(a.OpportunityScore, b.OpportunityScore);
    }

    [Fact]
    public void Signal_Contributions_Are_Structured_For_Weight_Tuning()
    {
        var prediction = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            108.2m, 80, 35, Intel(usage: 70, health: 75)));

        Assert.Contains(prediction.SignalContributions, c => c.SignalId == "projection-vs-line");
        Assert.Contains(prediction.SignalContributions, c => c.SignalId == "usage-opportunity" && c.Available);
        Assert.All(prediction.SignalContributions, c => Assert.False(string.IsNullOrWhiteSpace(c.Label)));
        Assert.True(prediction.OpportunityScore > 0);
    }

    [Fact]
    public void QuickPicksService_Works_Without_Fantasy_League_Dependency()
    {
        using var provider = TestServiceFactory.CreateProvider(PlayerDataProviderKind.Mock);

        var ctorParams = typeof(QuickPicksService).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(ctorParams, p =>
            p.ParameterType.Name.Contains("League", StringComparison.OrdinalIgnoreCase) ||
            p.ParameterType.Name.Contains("Roster", StringComparison.OrdinalIgnoreCase));

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();

        var all = quickPicks.GetAllPredictions();
        Assert.NotEmpty(all);
        Assert.Contains(all, p => p.EngineVersion == "0.3");
        Assert.Contains(all, p => p.SignalContributions.Count > 0);
        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p.Reasoning)));
        Assert.NotNull(quickPicks.SelectedWeek);
        Assert.NotEmpty(quickPicks.AvailableWeeks);
        Assert.All(all, p => Assert.True(quickPicks.SelectedWeek!.Matches(p.Event)));
        Assert.All(all, p => Assert.Contains("Week", p.Event.SlateLabel, StringComparison.OrdinalIgnoreCase));
        Assert.All(all, p => Assert.True(p.Event.Week <= 3 || p.Event.Phase != NflSeasonPhase.Preseason));
    }

    [Fact]
    public void Preseason_Prior_Production_Is_Labeled_And_Tempers_Confidence()
    {
        var regular = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m, phase: NflSeasonPhase.RegularSeason),
            108.2m, 70, 40, Intel(usage: 65, health: 75),
            seasonPhase: NflSeasonPhase.RegularSeason));

        var preseason = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m, phase: NflSeasonPhase.Preseason),
            108.2m, 70, 40, Intel(usage: 65, health: 75),
            seasonPhase: NflSeasonPhase.Preseason,
            usingPrior: true));

        Assert.True(preseason.Confidence < regular.Confidence);
        Assert.Contains("preseason", preseason.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(preseason.SupportingIntelligence, s =>
            s.Contains("Prior regular-season", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(preseason.SignalContributions, c => c.SignalId == "season-phase");
    }

    [Fact]
    public void Usage_Signal_Moves_Edge_Vs_Neutral_Usage()
    {
        var strong = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            100m, 75, 35, Intel(usage: 85, opportunity: 80, health: 75)));
        var soft = Require(Eval(
            Line(PredictionMarketType.ReceivingYards, 94.5m),
            100m, 75, 35, Intel(usage: 25, opportunity: 25, health: 75)));

        Assert.True(strong.Edge != soft.Edge || strong.Probability != soft.Probability);
        Assert.Contains(strong.SignalContributions, c => c.SignalId == "usage-opportunity" && c.Available);
    }

    private static QuickPicksEngine CreateEngine(QuickPicksScoringOptions? options = null) =>
        new(Options.Create(options ?? new QuickPicksScoringOptions()));

    private Prediction? Eval(
        PropLine line,
        decimal? projection,
        int confidence,
        int volatility,
        PlayerIntelligenceProfile? intelligence,
        PlayerInjuryProfile? injuryProfile = null,
        IReadOnlyList<IntelligenceFact>? facts = null,
        NflSeasonPhase seasonPhase = NflSeasonPhase.RegularSeason,
        bool usingPrior = false) =>
        _engine.Evaluate(new QuickPickEvaluationContext
        {
            Line = line,
            PlaybookProjection = projection,
            ProjectionConfidence = confidence,
            Volatility = volatility,
            Intelligence = intelligence,
            StatisticalContext = null,
            InjuryProfile = injuryProfile,
            RecentFacts = facts ?? [],
            SeasonPhase = seasonPhase,
            UsingPriorRegularSeasonProduction = usingPrior
        });

    private static Prediction Require(Prediction? prediction)
    {
        Assert.NotNull(prediction);
        return prediction!;
    }

    private static PropLine Line(
        PredictionMarketType market,
        decimal? line,
        PropLineFreshness freshness = PropLineFreshness.Mock,
        string id = "test-line",
        NflSeasonPhase phase = NflSeasonPhase.RegularSeason,
        int week = 1) =>
        new()
        {
            Id = id,
            Event = new FootballEvent
            {
                EventId = "test-cin-cle",
                HomeTeam = "CLE",
                AwayTeam = "CIN",
                CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
                Season = 2026,
                Phase = phase,
                Week = week
            },
            PlayerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            PlayerName = "Ja'Marr Chase",
            TeamName = "CIN",
            Market = market,
            Line = line,
            Bookmaker = "MockBook",
            Source = "Mock",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = freshness
        };

    private static PlayerIntelligenceProfile Intel(
        int health = 50,
        int opportunity = 50,
        int usage = 50,
        int risk = 10,
        int confidence = 70) =>
        new()
        {
            PlayerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            OverallConfidence = confidence,
            OverallRisk = risk,
            OpportunityScore = opportunity,
            TrendDirection = TrendDirection.Flat,
            HealthScore = health,
            UsageScore = usage,
            NewsMomentum = 50,
            LastUpdated = DateTimeOffset.UtcNow,
            SupportingFacts = [],
            Headline = "Neutral",
            ChangeSignal = IntelligenceChangeSignal.Neutral
        };

    private static PlayerInjuryRecord CurrentInjury(string status, string bodyPart) =>
        new()
        {
            PlayerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Date = DateTimeOffset.UtcNow.AddDays(-1),
            Status = status,
            BodyPart = bodyPart,
            Source = "Test",
            Verified = true,
            SourceConfidence = InjurySourceConfidence.Verified,
            LastUpdated = DateTimeOffset.UtcNow,
            IsCurrent = true
        };

    private static PlayerInjuryProfile InjuryProfile(
        PlayerInjuryRecord? current = null,
        bool unconfirmed = false,
        bool historicalHigh = false)
    {
        var playerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        IReadOnlyList<UnconfirmedInjurySignal> buzz = unconfirmed
            ?
            [
                new UnconfirmedInjurySignal
                {
                    Id = Guid.NewGuid(),
                    PlayerId = playerId,
                    Headline = "Reportedly limited in practice with ankle soreness",
                    Source = "TestWire",
                    Published = DateTimeOffset.UtcNow.AddHours(-6),
                    LastUpdated = DateTimeOffset.UtcNow,
                    Confidence = 62
                }
            ]
            : [];

        IReadOnlyList<InjuryHistoryEntry> history = historicalHigh
            ?
            [
                new InjuryHistoryEntry
                {
                    Record = new PlayerInjuryRecord
                    {
                        PlayerId = playerId,
                        Date = DateTimeOffset.UtcNow.AddMonths(-10),
                        Status = "Out",
                        BodyPart = "hamstring",
                        Source = "nflverse",
                        Verified = true,
                        SourceConfidence = InjurySourceConfidence.Verified,
                        LastUpdated = DateTimeOffset.UtcNow.AddMonths(-10),
                        IsCurrent = false
                    },
                    RelevanceScore = 78,
                    Band = InjuryRelevanceBand.High,
                    RelevanceReason = "Same body region cluster"
                }
            ]
            : [];

        return new PlayerInjuryProfile
        {
            PlayerId = playerId,
            CurrentDataStatus = current is null ? CurrentInjuryDataStatus.NoCurrentInjury : CurrentInjuryDataStatus.Available,
            CurrentStatus = current?.Status,
            CurrentInjury = current,
            RecentHistory = history,
            HistoricalEntries = history,
            HistoricalDataStatus = HistoricalDataStatus.Available,
            NflHistoricalDataStatus = HistoricalDataStatus.Available,
            CollegeHistoricalDataStatus = HistoricalDataStatus.NotSupportedByProvider,
            UnconfirmedSignals = buzz,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }
}
