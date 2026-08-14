using Playbook.Core.Predictions;

namespace Playbook.Core.Research;

/// <summary>
/// PRE_EVENT_SNAPSHOT — an immutable record of what Playbook believed about a Quick Pick before
/// the event happened. Captured only for predictions built on a real (non-mock) sportsbook line,
/// at the moment the prediction was produced. Never overwritten or mutated after creation — see
/// <see cref="Application.Research.IPredictionResearchStore"/>. Answers "what did Playbook believe
/// before this game?" independent of whatever the model later becomes.
/// </summary>
public sealed class PredictionSnapshot
{
    /// <summary>Stable identity for this snapshot — same as the originating <c>Prediction.Id</c>.</summary>
    public required Guid SnapshotId { get; init; }

    public required Guid? PlayerId { get; init; }

    public required string? PlayerName { get; init; }

    public required string EventId { get; init; }

    public required DateTimeOffset CommenceTime { get; init; }

    public required int Season { get; init; }

    public required int Week { get; init; }

    public required NflSeasonPhase SeasonPhase { get; init; }

    public required PredictionMarketType Market { get; init; }

    public required PredictionDirection Direction { get; init; }

    /// <summary>Sportsbook line at snapshot time (never a mock value — see capture-site gating).</summary>
    public required decimal? Line { get; init; }

    public required string Bookmaker { get; init; }

    public required string LineSource { get; init; }

    public required DateTimeOffset LineUpdatedAt { get; init; }

    public required decimal? PlaybookProjection { get; init; }

    public required int Probability { get; init; }

    public required int Confidence { get; init; }

    public required string Reasoning { get; init; }

    /// <summary>Snapshot of the supporting-intelligence bullet points at capture time.</summary>
    public required IReadOnlyList<string> SupportingIntelligence { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }
}
