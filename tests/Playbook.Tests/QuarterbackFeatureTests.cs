using Playbook.Core.Predictions.Models;
using Playbook.Infrastructure.Predictions;
using Xunit;

namespace Playbook.Tests;

/// <summary>
/// The first player-level feature in the team-points model: quarterback quality.
///
/// The tests that matter most here are the negative ones — that absent QB data falls back to the
/// untouched baseline rather than inventing a quarterback, and that no information from the
/// predicted week or later can reach the feature.
/// </summary>
public class QuarterbackFeatureTests
{
    // ---------------------------------------------------------------- leakage

    [Fact]
    public void QbForm_UsesOnlyPriorWeeks_NeverThePredictedGame()
    {
        // Week 3 is a monster game. If it leaked, EPA/att would jump toward +1.0 instead of
        // staying at the +0.1 established in weeks 1-2.
        var lines = new List<QuarterbackGameLine>
        {
            Qb(2025, 1, "KC", "qb1", attempts: 30, epa: 3.0m),   // 0.10/att
            Qb(2025, 2, "KC", "qb1", attempts: 30, epa: 3.0m),   // 0.10/att
            Qb(2025, 3, "KC", "qb1", attempts: 30, epa: 30.0m)   // predicted game
        };

        var form = QuarterbackFormBuilder.Build("KC", 2025, week: 3, lines)!;

        Assert.Equal(0.1m, form.EpaPerAttempt);
        Assert.Equal(2, form.GamesObserved);
        Assert.Equal(60, form.AttemptsObserved);
    }

    [Fact]
    public void QbForm_ExcludesLaterWeeks_Entirely()
    {
        var lines = new List<QuarterbackGameLine>
        {
            Qb(2025, 1, "KC", "qb1", 20, 2.0m),
            Qb(2025, 9, "KC", "qb1", 40, 40.0m)   // far future
        };

        var form = QuarterbackFormBuilder.Build("KC", 2025, week: 2, lines)!;

        Assert.Equal(1, form.GamesObserved);
        Assert.Equal(20, form.AttemptsObserved);
    }

    [Fact]
    public void QbForm_DoesNotBorrowFromAnotherSeason()
    {
        var lines = new List<QuarterbackGameLine>
        {
            Qb(2024, 17, "KC", "qb1", 35, 35.0m)   // prior season, not this one
        };

        Assert.Null(QuarterbackFormBuilder.Build("KC", 2025, week: 5, lines));
    }

    [Fact]
    public void QbForm_DoesNotBorrowFromAnotherTeam()
    {
        var lines = new List<QuarterbackGameLine> { Qb(2025, 1, "BUF", "qb2", 30, 9.0m) };

        Assert.Null(QuarterbackFormBuilder.Build("KC", 2025, week: 2, lines));
    }

    // ---------------------------------------------------------------- no fabrication

    [Fact]
    public void QbForm_ReturnsNull_WhenNoPriorPassingExists()
    {
        Assert.Null(QuarterbackFormBuilder.Build("KC", 2025, week: 1, []));
    }

    [Fact]
    public void MissingQbData_FallsBackToUntouchedBaseline_NotAnInventedQuarterback()
    {
        // Identical features, one with QB evidence absent. The absent case must reproduce the
        // v1 baseline arithmetic exactly.
        var baseline = TeamPointsModel.Predict(Features(qbEpa: null))!;

        var expected =
            TeamPointsModel.Intercept
            + TeamPointsModel.RollingPointsForWeight * 24m
            + TeamPointsModel.OpponentPointsAllowedWeight * 22m
            + TeamPointsModel.HomeFieldWeight
            + TeamPointsModel.PriorSeasonWeight * 23m;

        Assert.Equal(Math.Round(expected, 1, MidpointRounding.AwayFromZero), baseline.ExpectedPoints);
        Assert.Contains(TeamPointsModel.Version, baseline.Explanation);
        Assert.Contains("QB quality unavailable", baseline.Explanation);
    }

    // ---------------------------------------------------------------- feature is used

    [Fact]
    public void PresentQbData_SelectsTheEnhancedModel()
    {
        var enhanced = TeamPointsModel.Predict(Features(qbEpa: 0.12m))!;

        Assert.Contains(TeamPointsModel.EnhancedVersion, enhanced.Explanation);
        Assert.Contains("EPA/att", enhanced.Explanation);
    }

    [Fact]
    public void BetterQuarterback_ProjectsMorePoints()
    {
        var poor = TeamPointsModel.Predict(Features(qbEpa: -0.25m))!;
        var elite = TeamPointsModel.Predict(Features(qbEpa: 0.30m))!;

        Assert.True(
            elite.ExpectedPoints > poor.ExpectedPoints,
            $"elite QB {elite.ExpectedPoints} must exceed poor QB {poor.ExpectedPoints}");

        // Bounded: across the observed EPA range (-0.54 to +0.67) the swing is meaningful but
        // does not dominate the scoring history.
        Assert.InRange(elite.ExpectedPoints - poor.ExpectedPoints, 2m, 6m);
    }

    [Fact]
    public void QbFeature_DoesNotBypassTheEvidenceGate()
    {
        // Good QB data cannot substitute for missing team scoring history.
        var thin = Features(qbEpa: 0.30m) with { GamesObservedTeam = 1 };

        Assert.Null(TeamPointsModel.Predict(thin));
    }

    // ---------------------------------------------------------------- attempt weighting

    [Fact]
    public void QbForm_WeightsByAttempts_SoACameoCannotSwingTheRating()
    {
        // A 1-attempt backup with a freak EPA must not move the team's rating much.
        var lines = new List<QuarterbackGameLine>
        {
            Qb(2025, 1, "KC", "starter", attempts: 40, epa: 4.0m),  // 0.10/att
            Qb(2025, 1, "KC", "backup", attempts: 1, epa: 5.0m)     // 5.00/att on one snap
        };

        var form = QuarterbackFormBuilder.Build("KC", 2025, week: 2, lines)!;

        // Attempt-weighted: (4.0 + 5.0) / 41 = 0.2195, not the 2.55 a naive per-player mean gives.
        Assert.InRange(form.EpaPerAttempt, 0.20m, 0.23m);
    }

    [Fact]
    public void QbForm_ExpectedStarter_IsTheMostRecentHighestVolumePasser()
    {
        var lines = new List<QuarterbackGameLine>
        {
            Qb(2025, 1, "KC", "old-starter", 40, 4.0m),
            Qb(2025, 2, "KC", "new-starter", 35, 3.5m),
            Qb(2025, 2, "KC", "mop-up", 3, 0.1m)
        };

        var form = QuarterbackFormBuilder.Build("KC", 2025, week: 3, lines)!;

        Assert.Equal("new-starter", form.ExpectedStarterId);
    }

    // ---------------------------------------------------------------- parser

    [Fact]
    public void Parser_KeepsOnlyRegularSeasonQuarterbacksWithAttempts()
    {
        var csv = new[]
        {
            "season,week,season_type,team,player_id,position,attempts,passing_epa",
            "2025,1,REG,KC,qb1,QB,32,6.5",
            "2025,1,POST,KC,qb1,QB,30,5.0",     // postseason
            "2025,1,REG,KC,wr1,WR,0,",          // not a QB
            "2025,2,REG,KC,qb2,QB,0,0.0"        // no attempts
        };

        var lines = NflverseQuarterbackFormProvider.Parse(csv, 2025);

        var line = Assert.Single(lines);
        Assert.Equal("qb1", line.PlayerId);
        Assert.Equal(32, line.Attempts);
        Assert.Equal(6.5m, line.PassingEpa);
    }

    [Fact]
    public void Parser_SkipsRowsWithMissingEpa_RatherThanTreatingThemAsZero()
    {
        var csv = new[]
        {
            "season,week,season_type,team,player_id,position,attempts,passing_epa",
            "2025,1,REG,KC,qb1,QB,32,"
        };

        Assert.Empty(NflverseQuarterbackFormProvider.Parse(csv, 2025));
    }

    // ---------------------------------------------------------------- baseline preserved

    [Fact]
    public void BaselineCoefficients_AreUnchanged_SoComparisonRemainsValid()
    {
        // Pinned so the v1 baseline cannot drift silently while v2 is developed against it.
        Assert.Equal(-4.9929m, TeamPointsModel.Intercept);
        Assert.Equal(0.3803m, TeamPointsModel.RollingPointsForWeight);
        Assert.Equal(0.5580m, TeamPointsModel.OpponentPointsAllowedWeight);
        Assert.Equal(1.8038m, TeamPointsModel.HomeFieldWeight);
        Assert.Equal(0.2349m, TeamPointsModel.PriorSeasonWeight);
        Assert.Equal("team-points-ols-v1", TeamPointsModel.Version);
    }

    [Fact]
    public void ChronologicalSplit_IsPreserved_NoOverlapNoShuffling()
    {
        // Both model versions are fitted and compared on this exact split. Encoded so the
        // methodology cannot silently drift into a shuffled or overlapping evaluation.
        Assert.Equal(2020, TeamPointsModel.TrainSeasonStart);
        Assert.Equal(2023, TeamPointsModel.TrainSeasonEnd);
        Assert.Equal(2024, TeamPointsModel.ValidationSeason);
        Assert.Equal(2025, TeamPointsModel.TestSeason);

        Assert.True(TeamPointsModel.TrainSeasonStart < TeamPointsModel.TrainSeasonEnd);
        Assert.True(TeamPointsModel.TrainSeasonEnd < TeamPointsModel.ValidationSeason);
        Assert.True(TeamPointsModel.ValidationSeason < TeamPointsModel.TestSeason);
    }

    [Fact]
    public void BettingRemainsDisabled()
    {
        Assert.False(TeamPointsModel.GameMarketBettingEnabled);
    }

    // ---------------------------------------------------------------- helpers

    private static QuarterbackGameLine Qb(
        int season, int week, string team, string playerId, int attempts, decimal epa) => new()
        {
            Season = season,
            Week = week,
            Team = team,
            PlayerId = playerId,
            Attempts = attempts,
            PassingEpa = epa
        };

    private static TeamPointsFeatures Features(decimal? qbEpa) => new()
    {
        RollingPointsFor = 24m,
        OpponentRollingPointsAllowed = 22m,
        IsHome = true,
        PriorSeasonPointsFor = 23m,
        GamesObservedTeam = 8,
        GamesObservedOpponent = 8,
        QuarterbackEpaPerAttempt = qbEpa
    };
}
