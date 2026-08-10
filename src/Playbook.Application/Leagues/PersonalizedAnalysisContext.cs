using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

/// <summary>
/// Snapshot of the active league + owned team used to stamp and invalidate personalized outputs.
/// </summary>
public readonly record struct PersonalizedAnalysisContext(
    Guid? LeagueId,
    int? SelectedRosterId,
    string LeagueName,
    string? TeamName,
    ScoringType ScoringType,
    int Week,
    bool IsSetupComplete)
{
    public static PersonalizedAnalysisContext FromState(ILeagueState state)
    {
        var league = state.CurrentLeague;
        var team = state.CurrentUserTeam;
        return new PersonalizedAnalysisContext(
            LeagueId: league?.Id,
            SelectedRosterId: league?.SelectedRosterId ?? team?.RosterId,
            LeagueName: league?.Name ?? "No League Selected",
            TeamName: team is null
                ? null
                : string.IsNullOrWhiteSpace(team.TeamName) ? team.DisplayName : team.TeamName,
            ScoringType: league?.ScoringType ?? ScoringType.Ppr,
            Week: league?.CurrentWeek ?? 1,
            IsSetupComplete: league?.IsSetupComplete ?? false);
    }

    public bool Matches(Guid? leagueId, int? selectedRosterId) =>
        LeagueId == leagueId && SelectedRosterId == selectedRosterId;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(TeamName)
            ? LeagueName
            : $"{LeagueName} · {TeamName}";
}
