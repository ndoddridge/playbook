using Playbook.Core.Leagues;

namespace Playbook.Application.Leagues;

public sealed class LeagueConnectResult
{
    public bool Succeeded { get; init; }

    public League? League { get; init; }

    public IReadOnlyList<FantasyTeam> Teams { get; init; } = [];

    public string? Error { get; init; }

    public static LeagueConnectResult Ok(League league, IReadOnlyList<FantasyTeam> teams) =>
        new()
        {
            Succeeded = true,
            League = league,
            Teams = teams
        };

    public static LeagueConnectResult Fail(string error) =>
        new()
        {
            Succeeded = false,
            Error = error
        };
}
