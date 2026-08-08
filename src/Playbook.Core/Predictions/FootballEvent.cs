namespace Playbook.Core.Predictions;

/// <summary>
/// Upcoming/live football game context for a prediction. Not fantasy-league scoped.
/// </summary>
public sealed class FootballEvent
{
    public required string EventId { get; init; }

    public required string HomeTeam { get; init; }

    public required string AwayTeam { get; init; }

    public required DateTimeOffset CommenceTime { get; init; }

    public string DisplayName => $"{AwayTeam} @ {HomeTeam}";
}
