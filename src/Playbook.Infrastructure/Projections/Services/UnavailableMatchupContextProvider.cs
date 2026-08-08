using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Projections.Services;

/// <summary>
/// Matchup architecture placeholder — reliable opponent defensive data is not wired yet.
/// </summary>
public sealed class UnavailableMatchupContextProvider : IMatchupContextProvider
{
    public string DisplayName => "Matchup (unavailable)";

    public bool IsConfigured => false;

    public MatchupContext GetMatchup(Player player, ProjectionLeagueContext leagueContext) =>
        MatchupContext.Unavailable(
            $"Matchup influence unavailable for {player.FullName} — opponent defensive data not yet integrated.");
}
