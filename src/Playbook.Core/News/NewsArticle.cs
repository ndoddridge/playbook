namespace Playbook.Core.News;

/// <summary>
/// Normalized football news item. Providers map source-specific payloads into this shape.
/// The Intelligence Engine will consume these later — UI only renders them.
/// </summary>
public sealed class NewsArticle
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Summary { get; init; }

    public required DateTimeOffset Published { get; init; }

    public required string Source { get; init; }

    public string? Url { get; init; }

    public IReadOnlyList<Guid> RelatedPlayerIds { get; init; } = [];

    public IReadOnlyList<string> RelatedTeamIds { get; init; } = [];

    public required NewsCategory Category { get; init; }

    public required NewsPriority Priority { get; init; }

    /// <summary>
    /// Optional athlete names from the source used to map RelatedPlayerIds when IDs are absent.
    /// </summary>
    public IReadOnlyList<string> RelatedPlayerNames { get; init; } = [];
}
