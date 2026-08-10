using Playbook.Core.Leagues;

namespace Playbook.Core.Projections.Models;

/// <summary>
/// League-aware inputs for projection (scoring, week, roster size).
/// Projection estimates outcomes; it does not make fantasy decisions.
/// </summary>
public sealed class ProjectionLeagueContext
{
    public Guid? LeagueId { get; init; }

    public required string LeagueName { get; init; }

    public required ScoringType ScoringType { get; init; }

    public required int CurrentWeek { get; init; }

    public required int Season { get; init; }

    public required int NumberOfTeams { get; init; }

    public static ProjectionLeagueContext FromLeague(League? league) =>
        league is null
            ? new ProjectionLeagueContext
            {
                LeagueId = null,
                LeagueName = "No League Selected",
                ScoringType = ScoringType.Ppr,
                CurrentWeek = 1,
                Season = DateTime.UtcNow.Year,
                NumberOfTeams = 12
            }
            : new ProjectionLeagueContext
            {
                LeagueId = league.Id,
                LeagueName = league.Name,
                ScoringType = league.ScoringType,
                CurrentWeek = league.CurrentWeek,
                Season = league.Season,
                NumberOfTeams = league.NumberOfTeams
            };
}
