namespace Playbook.Core.Leagues;

/// <summary>
/// Distinguishes demo/mock leagues from live platform-connected leagues.
/// </summary>
public enum LeagueDataSource
{
    Mock = 0,
    Sleeper = 1
}
