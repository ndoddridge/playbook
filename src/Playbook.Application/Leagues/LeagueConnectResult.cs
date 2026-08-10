using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

public sealed class LeagueConnectResult
{
    public bool Succeeded { get; init; }

    /// <summary>
    /// League loaded successfully but the user still needs to pick their team
    /// before setup is complete (unless a saved selection was restored).
    /// </summary>
    public bool NeedsTeamSelection { get; init; }

    public League? League { get; init; }

    public IReadOnlyList<FantasyTeam> Teams { get; init; } = [];

    public FantasyTeam? SelectedTeam { get; init; }

    public string? Error { get; init; }

    public bool IsSetupComplete =>
        Succeeded &&
        !NeedsTeamSelection &&
        League is { IsSetupComplete: true };

    public static LeagueConnectResult Ok(
        League league,
        IReadOnlyList<FantasyTeam> teams,
        FantasyTeam? selectedTeam = null,
        bool needsTeamSelection = false) =>
        new()
        {
            Succeeded = true,
            League = league,
            Teams = teams,
            SelectedTeam = selectedTeam,
            NeedsTeamSelection = needsTeamSelection
        };

    public static LeagueConnectResult Fail(string error) =>
        new()
        {
            Succeeded = false,
            Error = error
        };
}
