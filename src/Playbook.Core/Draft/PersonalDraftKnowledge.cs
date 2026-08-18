using Playbook.Core.Historical;
using Playbook.Core.Leagues;

namespace Playbook.Core.Draft;

/// <summary>
/// The currently selected Draft Assistant league + team. Personal draft knowledge is stored and
/// applied only under this pair — never globally.
/// </summary>
public sealed record PersonalDraftLearningRequest(
    string LeagueId,
    string TeamId,
    string LeagueName,
    string TeamName,
    int? RosterId = null,
    string? OwnerUserId = null,
    string? OwnerDisplayName = null)
{
    public bool HasLeagueAndTeam =>
        !string.IsNullOrWhiteSpace(LeagueId) && !string.IsNullOrWhiteSpace(TeamId);

    public static PersonalDraftLearningRequest? From(League? league, FantasyTeam? team)
    {
        if (league is null || team is null)
        {
            return null;
        }

        var leagueId = string.IsNullOrWhiteSpace(league.ExternalId)
            ? league.Id.ToString("N")
            : league.ExternalId;
        return new PersonalDraftLearningRequest(
            LeagueId: leagueId,
            TeamId: team.RosterId.ToString(),
            LeagueName: league.Name,
            TeamName: string.IsNullOrWhiteSpace(team.TeamName) ? team.DisplayName : team.TeamName!,
            RosterId: team.RosterId,
            OwnerUserId: team.OwnerUserId,
            OwnerDisplayName: team.DisplayName);
    }
}

/// <summary>
/// Roster/league situation attached to one observed player-vs-player decision. Contradictory
/// preferences with different construction stay separate rather than being averaged away.
/// </summary>
public sealed record PersonalPreferenceContext(
    LeagueType LeagueType,
    string ScoringFormat,
    int LeagueSize,
    int Round,
    int PickNumber,
    IReadOnlyDictionary<string, int> RosterBefore,
    IReadOnlyList<string> AlternativePlayerKeys);

/// <summary>
/// One specific-player preference: <see cref="PreferredPlayerKey"/> was taken while
/// <see cref="PassedPlayerKey"/> was still available. Repeated identical decisions increase
/// <see cref="ObservationCount"/>; a different <see cref="Context"/> is a different record.
/// </summary>
public sealed record PersonalPlayerPreference(
    string PreferredPlayerKey,
    string PreferredPlayerName,
    string PassedPlayerKey,
    string PassedPlayerName,
    PersonalPreferenceContext Context,
    int ObservationCount,
    IReadOnlyList<string> SourceDraftIds);

/// <summary>
/// Persisted personal draft knowledge for exactly one LeagueId + TeamId. Never mixed across
/// leagues or roster owners.
/// </summary>
public sealed class PersonalDraftKnowledge
{
    public required string LeagueId { get; init; }
    public required string TeamId { get; init; }
    public string? OwnerKey { get; init; }
    public required string LeagueName { get; init; }
    public required string TeamName { get; init; }
    public required int DraftCount { get; init; }
    public required int DecisionCount { get; init; }
    public IReadOnlyList<string> LearnedDraftIds { get; init; } = [];
    public IReadOnlyList<PersonalPlayerPreference> Preferences { get; init; } = [];
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public HistoricalEvidenceStrength EvidenceStrength =>
        PersonalDraftLearningPolicy.Strength(DecisionCount);
}

/// <summary>Compact status for the Draft Assistant UI — always scoped to the active league/team.</summary>
public sealed record PersonalDraftKnowledgeStatus(
    bool LeagueSelected,
    bool TeamSelected,
    string? LeagueName,
    string? TeamName,
    int DraftCount,
    int DecisionCount)
{
    public bool CanLearn => LeagueSelected && TeamSelected;

    public string ScopeLabel =>
        CanLearn ? $"Learning for: {LeagueName} — {TeamName}" : "Select a league and team to enable personal draft learning.";

    public string CountsLabel =>
        CanLearn
            ? $"Personal draft knowledge: {DraftCount} draft{(DraftCount == 1 ? "" : "s")} · {DecisionCount} decision{(DecisionCount == 1 ? "" : "s")}"
            : "";
}

/// <summary>Bounded score nudge plus an explainable factor, never a replacement for objective value.</summary>
public sealed record PersonalPreferenceAdjustment(decimal ScoreDelta, DraftRecommendationFactor? Factor);
