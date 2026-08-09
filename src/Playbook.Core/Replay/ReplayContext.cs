using Playbook.Core.Decisions;
using Playbook.Core.Leagues;

namespace Playbook.Core.Replay;

/// <summary>
/// Context passed into the existing Knowledge + Decision engines for a historical week.
/// The engines must not care whether this originated from live or historical data —
/// they only need a valid <see cref="DecisionContext"/> and knowledge snapshots.
/// </summary>
public sealed class ReplayContext
{
    public required HistoricalSnapshot Snapshot { get; init; }

    public required DecisionContext DecisionContext { get; init; }

    public static ReplayContext FromSnapshot(HistoricalSnapshot snapshot, DecisionKind kind = DecisionKind.StartSit)
    {
        var decisionContext = new DecisionContext
        {
            LeagueId = snapshot.LeagueId,
            SelectedRosterId = snapshot.SelectedRosterId,
            LeagueName = snapshot.LeagueName,
            ScoringType = snapshot.ScoringType,
            Season = snapshot.Season,
            Week = snapshot.Week,
            InformationCutoff = snapshot.InformationCutoff,
            TeamName = snapshot.TeamName,
            DecisionKind = kind
        };

        return new ReplayContext
        {
            Snapshot = snapshot,
            DecisionContext = decisionContext
        };
    }
}

/// <summary>Developer-facing replay request (e.g. season=2018 week=7).</summary>
public sealed class HistoricalReplayRequest
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public ScoringType ScoringType { get; init; } = ScoringType.Ppr;

    public string? FixtureId { get; init; }

    public DecisionKind DecisionKind { get; init; } = DecisionKind.StartSit;
}

/// <summary>Availability classification for historical data domains.</summary>
public enum HistoricalDataAvailability
{
    Available = 0,
    Partial = 1,
    Unavailable = 2
}

public sealed class HistoricalDataAvailabilityItem
{
    public required string Domain { get; init; }

    public required HistoricalDataAvailability Status { get; init; }

    public required string Notes { get; init; }
}
