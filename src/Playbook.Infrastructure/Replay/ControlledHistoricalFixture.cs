using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Small controlled 2018 Week 7 fixture for Historical Replay Engine v1.
/// Intentionally embeds future-dated events so cutoff filtering can be regression-tested.
/// </summary>
public static class ControlledHistoricalFixture
{
    public const string FixtureId = "controlled-2018-w7";
    public const int Season = 2018;
    public const int Week = 7;

    /// <summary>Tuesday before Week 7 games (no Week 7 results known yet).</summary>
    public static readonly DateTimeOffset InformationCutoff =
        new(2018, 10, 16, 16, 0, 0, TimeSpan.Zero);

    public static readonly Guid AlphaRbId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    public static readonly Guid BravoRbId = Guid.Parse("b2222222-2222-2222-2222-222222222222");
    public static readonly Guid CharlieWrId = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    public static readonly Guid DeltaWrId = Guid.Parse("d4444444-4444-4444-4444-444444444444");
    public static readonly Guid EchoTeId = Guid.Parse("e5555555-5555-5555-5555-555555555555");

    public static HistoricalRawWeekData Create(ScoringType scoringType = ScoringType.Ppr)
    {
        // Future-dated rows (AFTER cutoff) — must be stripped from the snapshot.
        var futureInjuryObservedAt = InformationCutoff.AddDays(3); // during/after Week 7
        var futureWeek8NewsAt = InformationCutoff.AddDays(10);
        var week7ActualsKnownAt = InformationCutoff.AddDays(1); // Sunday results — not for knowledge

        return new HistoricalRawWeekData
        {
            Season = Season,
            Week = Week,
            InformationCutoff = InformationCutoff,
            ScoringType = scoringType,
            LeagueName = "Replay Lab Controlled League",
            LeagueId = Guid.Parse("f1000000-0000-0000-0000-000000000001"),
            SelectedRosterId = 1,
            TeamName = "Replay Managers",
            SourceLabel = FixtureId,
            UnavailableSources =
            [
                "Historical depth charts (unavailable in v1 fixture)",
                "Historical betting/matchup lines (unavailable)",
                "Historical news archive (partial — fixture headlines only)"
            ],
            Roster =
            [
                new HistoricalRosterSlot { PlayerId = AlphaRbId, IsStarter = true },
                new HistoricalRosterSlot { PlayerId = BravoRbId, IsStarter = false },
                new HistoricalRosterSlot { PlayerId = CharlieWrId, IsStarter = true },
                new HistoricalRosterSlot { PlayerId = DeltaWrId, IsStarter = false },
                new HistoricalRosterSlot { PlayerId = EchoTeId, IsStarter = true }
            ],
            Players =
            [
                new HistoricalRawPlayerRecord
                {
                    PlayerId = AlphaRbId,
                    PlayerName = "Alpha Runner",
                    Position = Position.RB,
                    Team = "KC",
                    ProjectedPoints = 15.2m,
                    Floor = 9.0m,
                    Ceiling = 22.0m,
                    ProjectionConfidence = 72,
                    ProjectionObservedAt = InformationCutoff.AddHours(-6),
                    OpportunityScore = 68,
                    UsageScore = 70,
                    HealthLabel = "Healthy",
                    RoleNote = "Feature back role entering the week",
                    RecentProductionScore = 64,
                    UnavailableSignals = [],
                    ProjectedRushYards = 72.0,
                    ProjectedReceivingYards = 18.0,
                    ProjectedReceptions = 2.5
                },
                new HistoricalRawPlayerRecord
                {
                    PlayerId = BravoRbId,
                    PlayerName = "Bravo Backup",
                    Position = Position.RB,
                    Team = "KC",
                    ProjectedPoints = 12.4m,
                    Floor = 6.5m,
                    Ceiling = 20.0m,
                    ProjectionConfidence = 65,
                    ProjectionObservedAt = InformationCutoff.AddHours(-6),
                    OpportunityScore = 55,
                    UsageScore = 52,
                    HealthLabel = "Healthy",
                    RoleNote = "Change-of-pace / pass-down role",
                    RecentProductionScore = 50,
                    UnavailableSignals = [],
                    ProjectedRushYards = 45.0,
                    ProjectedReceivingYards = 28.0,
                    ProjectedReceptions = 3.5
                },
                new HistoricalRawPlayerRecord
                {
                    PlayerId = CharlieWrId,
                    PlayerName = "Charlie Target",
                    Position = Position.WR,
                    Team = "LAR",
                    ProjectedPoints = 18.0m,
                    Floor = 10.0m,
                    Ceiling = 28.0m,
                    ProjectionConfidence = 74,
                    ProjectionObservedAt = InformationCutoff.AddHours(-5),
                    OpportunityScore = 75,
                    UsageScore = 72,
                    HealthLabel = "Healthy",
                    RecentNewsHeadline = "Listed as WR1 with full practice participation",
                    RecentNewsObservedAt = InformationCutoff.AddHours(-20),
                    RecentNewsConfirmed = true,
                    RoleNote = "Clear WR1",
                    RecentProductionScore = 78,
                    UnavailableSignals = [],
                    ProjectedReceivingYards = 85.0,
                    ProjectedReceptions = 6.5
                },
                new HistoricalRawPlayerRecord
                {
                    PlayerId = DeltaWrId,
                    PlayerName = "Delta Deep Threat",
                    Position = Position.WR,
                    Team = "LAR",
                    ProjectedPoints = 11.0m,
                    Floor = 4.0m,
                    Ceiling = 21.0m,
                    ProjectionConfidence = 60,
                    ProjectionObservedAt = InformationCutoff.AddHours(-5),
                    OpportunityScore = 48,
                    UsageScore = 45,
                    HealthLabel = "Healthy",
                    // FUTURE injury — after cutoff. Builder must exclude this.
                    InjuryStatus = "Out",
                    InjuryBodyPart = "Hamstring",
                    InjuryObservedAt = futureInjuryObservedAt,
                    // FUTURE week-8 news — after cutoff.
                    RecentNewsHeadline = "Week 8: ruled out for the season",
                    RecentNewsObservedAt = futureWeek8NewsAt,
                    RecentNewsConfirmed = true,
                    RoleNote = "Secondary receiver",
                    RecentProductionScore = 42,
                    UnavailableSignals = ["Historical snap share detail"],
                    ProjectedReceivingYards = 48.0,
                    ProjectedReceptions = 3.0
                },
                new HistoricalRawPlayerRecord
                {
                    PlayerId = EchoTeId,
                    PlayerName = "Echo Tight End",
                    Position = Position.TE,
                    Team = "PHI",
                    ProjectedPoints = 9.5m,
                    Floor = 4.5m,
                    Ceiling = 16.0m,
                    ProjectionConfidence = 58,
                    ProjectionObservedAt = InformationCutoff.AddHours(-4),
                    OpportunityScore = 50,
                    UsageScore = 48,
                    HealthLabel = "Questionable",
                    InjuryStatus = "Questionable",
                    InjuryBodyPart = "Ankle",
                    InjuryObservedAt = InformationCutoff.AddHours(-12),
                    RecentNewsHeadline = "Limited Thursday; expected to play",
                    RecentNewsObservedAt = InformationCutoff.AddHours(-10),
                    RecentNewsConfirmed = true,
                    UnavailableSignals = ["Historical red-zone share"],
                    ProjectedReceivingYards = 42.0,
                    ProjectedReceptions = 4.0
                }
            ],
            Outcomes =
            [
                // Comparative trap: Alpha projected higher than Bravo but underperformed.
                new HistoricalPlayerOutcome
                {
                    PlayerId = AlphaRbId,
                    PlayerName = "Alpha Runner",
                    ActualFantasyPoints = 8.1,
                    Note = $"Actuals known {week7ActualsKnownAt:u} — not available at cutoff",
                    ActualRushYards = 38,
                    ActualReceivingYards = 12,
                    ActualReceptions = 2,
                    ActualRushTouchdowns = 0,
                    ActualReceivingTouchdowns = 0
                },
                new HistoricalPlayerOutcome
                {
                    PlayerId = BravoRbId,
                    PlayerName = "Bravo Backup",
                    ActualFantasyPoints = 17.3,
                    Note = "Outperformed Alpha despite lower projection",
                    ActualRushYards = 95,
                    ActualReceivingYards = 40,
                    ActualReceptions = 5,
                    ActualRushTouchdowns = 1,
                    ActualReceivingTouchdowns = 0
                },
                new HistoricalPlayerOutcome
                {
                    PlayerId = CharlieWrId,
                    PlayerName = "Charlie Target",
                    ActualFantasyPoints = 22.0,
                    ActualReceivingYards = 110,
                    ActualReceptions = 8,
                    ActualReceivingTouchdowns = 1
                },
                new HistoricalPlayerOutcome
                {
                    PlayerId = DeltaWrId,
                    PlayerName = "Delta Deep Threat",
                    ActualFantasyPoints = 3.2,
                    Note = "Future injury designation must not influence pre-game decision",
                    ActualReceivingYards = 18,
                    ActualReceptions = 1,
                    ActualReceivingTouchdowns = 0
                },
                new HistoricalPlayerOutcome
                {
                    PlayerId = EchoTeId,
                    PlayerName = "Echo Tight End",
                    ActualFantasyPoints = 11.4,
                    ActualReceivingYards = 55,
                    ActualReceptions = 5,
                    ActualReceivingTouchdowns = 0
                }
            ]
        };
    }
}
