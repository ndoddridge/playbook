using Playbook.Core.Predictions;

namespace Playbook.Core.Predictions.Models;

/// <summary>
/// One quarterback's passing work in one completed game. The atom the QB-quality feature is
/// built from.
/// </summary>
public sealed record QuarterbackGameLine
{
    public required int Season { get; init; }

    public required int Week { get; init; }

    public required string Team { get; init; }

    public required string PlayerId { get; init; }

    public required int Attempts { get; init; }

    /// <summary>Total passing EPA for the game (not per attempt).</summary>
    public required decimal PassingEpa { get; init; }
}

/// <summary>
/// A team's quarterback quality heading into a game, measured from completed games only.
/// </summary>
public sealed record QuarterbackForm
{
    /// <summary>Attempt-weighted passing EPA per attempt across prior games this season.</summary>
    public required decimal EpaPerAttempt { get; init; }

    public required int AttemptsObserved { get; init; }

    public required int GamesObserved { get; init; }

    /// <summary>Player id of the QB who took the most attempts in the most recent prior game.</summary>
    public string? ExpectedStarterId { get; init; }
}

/// <summary>
/// Builds <see cref="QuarterbackForm"/> from completed quarterback game lines.
///
/// LEAKAGE DISCIPLINE — mirrors <see cref="TeamPointsFeatureBuilder"/> exactly. Only games in the
/// same season with week &lt; the predicted week are considered. The predicted game and every
/// later game are excluded, so runtime behaviour matches how the coefficient was fitted.
/// </summary>
public static class QuarterbackFormBuilder
{
    /// <summary>
    /// Attempt-weighted EPA per attempt over prior games. Attempt weighting rather than a simple
    /// per-game mean so a one-attempt cameo cannot swing a team's quarterback rating.
    ///
    /// Returns null when no prior passing work exists — the caller must fall back to the baseline
    /// model rather than substituting a league-average quarterback.
    /// </summary>
    public static QuarterbackForm? Build(
        string team,
        int season,
        int week,
        IReadOnlyList<QuarterbackGameLine> completedQbLines)
    {
        ArgumentNullException.ThrowIfNull(completedQbLines);

        if (string.IsNullOrWhiteSpace(team))
        {
            return null;
        }

        var prior = completedQbLines
            .Where(l => l.Season == season
                        && l.Week < week
                        && string.Equals(l.Team, team, StringComparison.OrdinalIgnoreCase)
                        && l.Attempts > 0)
            .ToList();

        if (prior.Count == 0)
        {
            return null;
        }

        var totalAttempts = prior.Sum(l => l.Attempts);
        if (totalAttempts <= 0)
        {
            return null;
        }

        var epaPerAttempt = prior.Sum(l => l.PassingEpa) / totalAttempts;

        var mostRecentWeek = prior.Max(l => l.Week);
        var starter = prior
            .Where(l => l.Week == mostRecentWeek)
            .OrderByDescending(l => l.Attempts)
            .First();

        return new QuarterbackForm
        {
            EpaPerAttempt = Math.Round(epaPerAttempt, 4, MidpointRounding.AwayFromZero),
            AttemptsObserved = totalAttempts,
            GamesObserved = prior.Select(l => l.Week).Distinct().Count(),
            ExpectedStarterId = starter.PlayerId
        };
    }
}
