using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Application.Projections.Interfaces;

/// <summary>
/// Optional matchup data. Implementations may return unavailable — never fabricate.
/// </summary>
public interface IMatchupContextProvider
{
    string DisplayName { get; }

    bool IsConfigured { get; }

    MatchupContext GetMatchup(Player player, ProjectionLeagueContext leagueContext);
}
