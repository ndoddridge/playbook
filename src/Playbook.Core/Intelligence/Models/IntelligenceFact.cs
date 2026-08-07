namespace Playbook.Core.Intelligence.Models;

/// <summary>
/// One inferred football insight. Contains no fantasy points, rankings, or league settings.
/// </summary>
public sealed class IntelligenceFact
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required IntelligenceCategory Category { get; init; }
    public required int Confidence { get; init; }
    public required IntelligenceImportance Importance { get; init; }
    public required IntelligenceSource Source { get; init; }
    public required DateTimeOffset Created { get; init; }
    public DateTimeOffset? Expires { get; init; }
    public Guid? RelatedPlayerId { get; init; }
    public string? RelatedTeamId { get; init; }
    public Guid? RelatedGameId { get; init; }

    /// <summary>
    /// News articles that produced this fact. Required for explainability.
    /// </summary>
    public IReadOnlyList<Guid> RelatedNewsArticleIds { get; init; } = [];

    /// <summary>
    /// Human-readable reasons / evidence (rule ids, matched phrases, headlines).
    /// </summary>
    public IReadOnlyList<string> SupportingEvidence { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];
}
