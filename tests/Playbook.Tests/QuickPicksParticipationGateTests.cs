using Playbook.Application.Predictions;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Infrastructure.Predictions;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

/// <summary>
/// Quick Picks must only show players realistically expected to participate. These tests cover
/// the real-signal-only participation gate: confirmed non-participants (roster status, Out/IR
/// injury designation) are excluded; everything else (including uncertain-but-not-ruled-out
/// designations) still produces a pick, since Playbook has no fabricated depth-chart signal to
/// exclude on.
/// </summary>
public class QuickPicksParticipationGateTests
{
    private static readonly Guid PlayerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private readonly QuickPicksEngine _engine = new(Options.Create(new QuickPicksScoringOptions()));

    [Theory]
    [InlineData(PlayerStatus.Suspended)]
    [InlineData(PlayerStatus.InjuredReserve)]
    [InlineData(PlayerStatus.PracticeSquad)]
    public void Disqualifying_Roster_Status_Excludes_The_Pick(PlayerStatus status)
    {
        var prediction = Evaluate(rosterStatus: status);

        Assert.Null(prediction);
    }

    [Fact]
    public void Active_Roster_Status_Does_Not_Exclude_The_Pick()
    {
        var prediction = Evaluate(rosterStatus: PlayerStatus.Active);

        Assert.NotNull(prediction);
    }

    [Theory]
    [InlineData("Out")]
    [InlineData("IR")]
    public void Out_Or_IR_Injury_Designation_Excludes_The_Pick(string status)
    {
        var prediction = Evaluate(injury: Injury(status));

        Assert.Null(prediction);
    }

    [Theory]
    [InlineData("Doubtful")]
    [InlineData("Questionable")]
    public void Doubtful_Or_Questionable_Does_Not_Exclude_The_Pick(string status)
    {
        // Real uncertainty, not confirmed non-participation — derated elsewhere, not excluded.
        var prediction = Evaluate(injury: Injury(status));

        Assert.NotNull(prediction);
    }

    [Fact]
    public void Stale_Line_Excludes_The_Pick()
    {
        var prediction = Evaluate(freshness: PropLineFreshness.Stale);

        Assert.Null(prediction);
    }

    [Fact]
    public void Unmatched_Player_Name_Is_Not_Excluded_By_The_Participation_Gate()
    {
        // No PlayerId means no roster/injury record exists to gate on — leave it to existing
        // quality/confidence handling rather than fabricating an exclusion.
        var prediction = Evaluate(matchedPlayer: false);

        Assert.NotNull(prediction);
    }

    private Prediction? Evaluate(
        PlayerStatus? rosterStatus = null,
        PlayerInjuryRecord? injury = null,
        PropLineFreshness freshness = PropLineFreshness.Mock,
        bool matchedPlayer = true)
    {
        var line = new PropLine
        {
            Id = "test-line",
            Event = new FootballEvent
            {
                EventId = "test-cin-cle",
                HomeTeam = "CLE",
                AwayTeam = "CIN",
                CommenceTime = DateTimeOffset.UtcNow.AddDays(1),
                Season = 2026,
                Phase = NflSeasonPhase.RegularSeason,
                Week = 1
            },
            PlayerId = matchedPlayer ? PlayerId : null,
            PlayerName = "Ja'Marr Chase",
            TeamName = "CIN",
            Market = PredictionMarketType.ReceivingYards,
            Line = 94.5m,
            Bookmaker = "Caesars",
            Source = "TheOddsAPI",
            UpdatedAt = DateTimeOffset.UtcNow,
            Freshness = freshness
        };

        return _engine.Evaluate(new QuickPickEvaluationContext
        {
            Line = line,
            PlaybookProjection = 108.2m,
            ProjectionConfidence = 75,
            Volatility = 35,
            RosterStatus = rosterStatus,
            InjuryProfile = injury is null ? null : new PlayerInjuryProfile { PlayerId = PlayerId, CurrentInjury = injury }
        });
    }

    private static PlayerInjuryRecord Injury(string status) => new()
    {
        PlayerId = PlayerId,
        Date = DateTimeOffset.UtcNow.AddDays(-1),
        Status = status,
        Source = "Test",
        LastUpdated = DateTimeOffset.UtcNow,
        IsCurrent = true
    };
}
