using Playbook.Core.Players;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Replay;
using Playbook.Infrastructure.Replay.Calibration;
using Playbook.Infrastructure.Replay.Reconstruction;

namespace Playbook.Tests;

public class PositionSegmentedCalibrationExperimentTests
{
    private static readonly IReadOnlyList<int> DevSeasons =
        PositionSegmentedCalibrationExperiment.DevelopmentSeasons.ToList();

    // --- Position grouping -------------------------------------------------

    [Fact]
    public void Grouping_Gives_TE_Its_Own_Group_When_Every_Development_Season_Clears_The_Threshold()
    {
        var obs = new List<CalibrationObservation>();
        foreach (var season in DevSeasons)
        {
            AddMany(obs, Position.QB, season, 40);
            AddMany(obs, Position.RB, season, 40);
            AddMany(obs, Position.WR, season, 40);
            AddMany(obs, Position.TE, season, 40);
        }

        var (groups, rationale) = PositionSegmentedCalibrationExperimentRunner.DecideGrouping(obs, DevSeasons);

        Assert.Equal(4, groups.Count);
        Assert.Contains(groups, g => g.Label == "QB" && g.Positions.Single() == Position.QB);
        Assert.Contains(groups, g => g.Label == "RB" && g.Positions.Single() == Position.RB);
        Assert.Contains(groups, g => g.Label == "WR" && g.Positions.Single() == Position.WR);
        Assert.Contains(groups, g => g.Label == "TE" && g.Positions.Single() == Position.TE);
        Assert.DoesNotContain("folded into WR", rationale);
    }

    [Fact]
    public void Grouping_Folds_TE_Into_WR_When_Any_Single_Development_Season_Is_Thin()
    {
        var obs = new List<CalibrationObservation>();
        foreach (var season in DevSeasons)
        {
            AddMany(obs, Position.QB, season, 40);
            AddMany(obs, Position.RB, season, 40);
            AddMany(obs, Position.WR, season, 40);
        }

        // TE clears the threshold in two of three development seasons but not the third —
        // must fold into WR rather than getting its own group.
        AddMany(obs, Position.TE, DevSeasons[0], 40);
        AddMany(obs, Position.TE, DevSeasons[1], 40);
        AddMany(obs, Position.TE, DevSeasons[2], 5);

        var (groups, rationale) = PositionSegmentedCalibrationExperimentRunner.DecideGrouping(obs, DevSeasons);

        Assert.Equal(3, groups.Count);
        var wrte = Assert.Single(groups, g => g.Label == "WR/TE");
        Assert.Equal(new[] { Position.WR, Position.TE }, wrte.Positions);
        Assert.DoesNotContain(groups, g => g.Label is "WR" or "TE");
        Assert.Contains("folded into WR", rationale);
    }

    [Fact]
    public void Grouping_Throws_When_A_Core_Position_Has_No_Defensible_Fallback_In_This_Candidate_Set()
    {
        var obs = new List<CalibrationObservation>();
        foreach (var season in DevSeasons)
        {
            AddMany(obs, Position.RB, season, 40);
            AddMany(obs, Position.WR, season, 40);
            AddMany(obs, Position.TE, season, 40);
        }

        // QB has almost no data in one development season and there is no adjacent group
        // within {QB,RB,WR,TE} that it is football-defensible to fold QB into.
        AddMany(obs, Position.QB, DevSeasons[0], 40);
        AddMany(obs, Position.QB, DevSeasons[1], 40);
        AddMany(obs, Position.QB, DevSeasons[2], 2);

        Assert.Throws<InvalidOperationException>(
            () => PositionSegmentedCalibrationExperimentRunner.DecideGrouping(obs, DevSeasons));
    }

    [Fact]
    public void Grouping_Excludes_K_And_DST_From_Every_Candidate_Group()
    {
        var obs = new List<CalibrationObservation>();
        foreach (var season in DevSeasons)
        {
            AddMany(obs, Position.QB, season, 40);
            AddMany(obs, Position.RB, season, 40);
            AddMany(obs, Position.WR, season, 40);
            AddMany(obs, Position.TE, season, 40);
            AddMany(obs, Position.K, season, 40);
            AddMany(obs, Position.DST, season, 40);
        }

        var (groups, _) = PositionSegmentedCalibrationExperimentRunner.DecideGrouping(obs, DevSeasons);

        Assert.DoesNotContain(groups.SelectMany(g => g.Positions), p => p is Position.K or Position.DST);
    }

    [Fact]
    public void Grouping_Is_Deterministic_For_The_Same_Observations()
    {
        var obs = new List<CalibrationObservation>();
        foreach (var season in DevSeasons)
        {
            AddMany(obs, Position.QB, season, 30);
            AddMany(obs, Position.RB, season, 30);
            AddMany(obs, Position.WR, season, 30);
            AddMany(obs, Position.TE, season, 10); // consistently thin -> WR/TE fold every time
        }

        var (groupsA, rationaleA) = PositionSegmentedCalibrationExperimentRunner.DecideGrouping(obs, DevSeasons);
        var (groupsB, rationaleB) = PositionSegmentedCalibrationExperimentRunner.DecideGrouping(obs, DevSeasons);

        Assert.Equal(rationaleA, rationaleB);
        Assert.Equal(groupsA.Select(g => g.Label), groupsB.Select(g => g.Label));
        Assert.Equal(
            groupsA.Select(g => string.Join(',', g.Positions)),
            groupsB.Select(g => string.Join(',', g.Positions)));
    }

    // --- Calibration selection / LOOCV isolation (reused fitter) -----------

    [Fact]
    public void PerGroup_Fit_Reuses_ProjectionCalibrationFitter_And_Rejects_Holdout_Observations()
    {
        var obs = new List<CalibrationObservation>
        {
            Obs(Position.RB, DevSeasons[0], 12, 10),
            Obs(Position.RB, DevSeasons[1], 11, 9),
            Obs(Position.RB, FrozenProjectionCalibrationV2.HoldoutSeason, 20, 18) // forbidden
        };

        Assert.Throws<InvalidOperationException>(() =>
            ProjectionCalibrationFitter.SelectAndFreeze(
                obs, DevSeasons, FrozenProjectionCalibrationV2.HoldoutSeason));
    }

    [Fact]
    public void PerGroup_Fit_Never_Trains_On_The_Season_It_Validates_Against()
    {
        var obs = new List<CalibrationObservation>();
        foreach (var season in DevSeasons)
        {
            AddMany(obs, Position.WR, season, 30);
        }

        var selection = ProjectionCalibrationFitter.SelectAndFreeze(
            obs.Where(o => o.Position == Position.WR).ToList(), DevSeasons, FrozenProjectionCalibrationV2.HoldoutSeason);

        Assert.Equal(DevSeasons.Count, selection.Folds.Count);
        foreach (var fold in selection.Folds)
        {
            // A fold's fitted calibration must come strictly from the OTHER development seasons.
            var trainOnly = obs.Where(o => o.Position == Position.WR && o.Season != fold.ValidateSeason).ToList();
            var refit = ProjectionCalibrationFitter.Fit(selection.SelectedMethod, trainOnly, 20);
            Assert.Equal(refit.HighSlope, fold.Calibration.HighSlope, 6);
            Assert.Equal(refit.LowSlope, fold.Calibration.LowSlope, 6);
        }
    }

    // --- Engine behavior (fallback / configured group / determinism) -------

    [Fact]
    public void Engine_Falls_Back_To_Unchanged_Global_V2_When_No_Group_Is_Configured()
    {
        var v1 = new OpportunityAwareProjectionEngine();
        var state = new PositionSegmentedCalibrationState(); // Active == null: production default
        Assert.Null(state.Active);
        var engine = new PositionSegmentedCalibratedProjectionEngine(v1, state);

        var features = BuildFeatures(Position.WR);
        var v1Result = v1.Project(features, ScoringType.Ppr);
        var result = engine.Project(features, ScoringType.Ppr);

        Assert.Equal(FrozenProjectionCalibrationV2.Apply(v1Result.ProjectedPoints), result.ProjectedPoints);
        Assert.Contains("fell back to global Projection V2", result.Methodology);
    }

    [Fact]
    public void Engine_Uses_The_Configured_Group_Fit_And_Is_Deterministic()
    {
        var v1 = new OpportunityAwareProjectionEngine();
        var fit = new ProjectionCalibrationFitter.FittedCalibration(
            ProjectionCalibrationMethod.GlobalScale, 0, 0.5, 0, 0.5, 20);
        var state = new PositionSegmentedCalibrationState
        {
            Active = new Dictionary<Position, ProjectionCalibrationFitter.FittedCalibration> { [Position.RB] = fit }
        };
        var engine = new PositionSegmentedCalibratedProjectionEngine(v1, state);
        var features = BuildFeatures(Position.RB);

        var first = engine.Project(features, ScoringType.Ppr);
        var second = engine.Project(features, ScoringType.Ppr);

        Assert.Equal(first.ProjectedPoints, second.ProjectedPoints); // deterministic
        var v1Result = v1.Project(features, ScoringType.Ppr);
        Assert.Equal(ProjectionCalibrationFitter.Apply(fit, v1Result.ProjectedPoints), first.ProjectedPoints);
        Assert.NotEqual(FrozenProjectionCalibrationV2.Apply(v1Result.ProjectedPoints), first.ProjectedPoints);
    }

    [Fact]
    public void Engine_Only_Applies_A_Groups_Fit_To_Positions_In_That_Group()
    {
        var v1 = new OpportunityAwareProjectionEngine();
        var fit = new ProjectionCalibrationFitter.FittedCalibration(
            ProjectionCalibrationMethod.GlobalScale, 0, 0.4, 0, 0.4, 20);
        var state = new PositionSegmentedCalibrationState
        {
            Active = new Dictionary<Position, ProjectionCalibrationFitter.FittedCalibration> { [Position.RB] = fit }
        };
        var engine = new PositionSegmentedCalibratedProjectionEngine(v1, state);

        // WR is not in the configured map -> must fall back to global V2, not the RB fit.
        var wrFeatures = BuildFeatures(Position.WR);
        var wrV1 = v1.Project(wrFeatures, ScoringType.Ppr);
        var wrResult = engine.Project(wrFeatures, ScoringType.Ppr);

        Assert.Equal(FrozenProjectionCalibrationV2.Apply(wrV1.ProjectedPoints), wrResult.ProjectedPoints);
    }

    // --- Unchanged control ---------------------------------------------------

    [Fact]
    public void Control_Calibration_Constants_Are_Unchanged_By_This_Experiment()
    {
        Assert.Equal(ProjectionCalibrationMethod.PiecewiseScaleAt20, FrozenProjectionCalibrationV2.Method);
        Assert.Equal(0.9240, FrozenProjectionCalibrationV2.LowSlope);
        Assert.Equal(0.6005, FrozenProjectionCalibrationV2.HighSlope);
        Assert.Equal(20.0, FrozenProjectionCalibrationV2.Threshold);
        Assert.Equal(new[] { 2015, 2018, 2021 }, PositionSegmentedCalibrationExperiment.DevelopmentSeasons);
        Assert.Equal(2024, PositionSegmentedCalibrationExperiment.HoldoutSeason);
    }

    // --- Helpers ---------------------------------------------------------

    private static void AddMany(List<CalibrationObservation> obs, Position position, int season, int count)
    {
        for (var i = 0; i < count; i++)
        {
            obs.Add(Obs(position, season, 10 + (i * 0.1), 9 + (i * 0.1)));
        }
    }

    private static CalibrationObservation Obs(Position position, int season, double v1, double actual) =>
        new()
        {
            Season = season,
            Week = 7,
            PlayerId = Guid.NewGuid(),
            PlayerName = "P",
            Position = position,
            V1Predicted = v1,
            Actual = actual,
            BaselineAPredicted = actual
        };

    private static HistoricalPlayerFeatures BuildFeatures(Position position)
    {
        var reconstructor = new HistoricalFeatureReconstructor();
        var games = new List<HistoricalGameObservation>
        {
            Game(2018, 4, 14, targets: 8, carries: 3),
            Game(2018, 5, 11, targets: 6, carries: 4),
            Game(2018, 6, 16, targets: 9, carries: 2)
        };

        return reconstructor.Reconstruct(
            Guid.NewGuid(),
            "Test Player",
            position,
            "NO",
            season: 2018,
            targetWeek: 7,
            informationCutoff: new DateTimeOffset(2018, 10, 18, 20, 0, 0, TimeSpan.FromHours(-4)),
            games);
    }

    private static HistoricalGameObservation Game(
        int season, int week, double points, int? targets = null, int? carries = null) =>
        new()
        {
            Season = season,
            Week = week,
            FantasyPoints = points,
            Targets = targets,
            RushAttempts = carries,
            Receptions = targets is null ? null : Math.Max(0, targets.Value - 2),
            OffenseSnapPct = 0.7
        };
}
