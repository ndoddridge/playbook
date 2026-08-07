namespace Playbook.Core.Players;

/// <summary>
/// Core football player identity. Engines and UI consume this; UI never constructs it.
/// </summary>
public sealed class Player
{
    public required Guid Id { get; init; }

    public required string FullName { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required Position Position { get; init; }

    public required string Team { get; init; }

    public int? JerseyNumber { get; init; }

    public int? Age { get; init; }

    public int? YearsPro { get; init; }

    public string? College { get; init; }

    public string? Height { get; init; }

    public int? Weight { get; init; }

    public string? HeadshotUrl { get; init; }

    public required PlayerStatus Status { get; init; }

    public int? ByeWeek { get; init; }
}
