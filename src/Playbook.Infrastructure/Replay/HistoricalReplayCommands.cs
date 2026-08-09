using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Developer-facing helpers for executing historical replays without mutating live league UI state.
/// <code>
/// await HistoricalReplayCommands.RunAsync(services, season: 2018, week: 7);
/// await HistoricalReplayCommands.RunSeasonAsync(services, season: 2018, startWeek: 1, endWeek: 17);
/// // or
/// await HistoricalReplayCommands.RunControlled2018Week7Async(services);
/// </code>
/// </summary>
public static class HistoricalReplayCommands
{
    public static Task<HistoricalReplayReport> RunControlled2018Week7Async(
        IServiceProvider services,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            new HistoricalReplayRequest
            {
                Season = ControlledHistoricalFixture.Season,
                Week = ControlledHistoricalFixture.Week,
                ScoringType = scoringType,
                FixtureId = ControlledHistoricalFixture.FixtureId
            },
            cancellationToken);

    /// <summary>Real nflverse-backed 2018 Week 7 replay (not the synthetic leakage fixture).</summary>
    public static Task<HistoricalReplayReport> RunReal2018Week7Async(
        IServiceProvider services,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            new HistoricalReplayRequest
            {
                Season = 2018,
                Week = 7,
                ScoringType = scoringType,
                FixtureId = "nflverse"
            },
            cancellationToken);

    public static Task<HistoricalReplayReport> RunAsync(
        IServiceProvider services,
        int season,
        int week,
        ScoringType scoringType = ScoringType.Ppr,
        string? fixtureId = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            services,
            new HistoricalReplayRequest
            {
                Season = season,
                Week = week,
                ScoringType = scoringType,
                FixtureId = fixtureId
            },
            cancellationToken);

    public static Task<HistoricalReplayReport> RunAsync(
        IServiceProvider services,
        HistoricalReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IHistoricalReplayRunner>();
        return runner.RunAsync(request, cancellationToken);
    }

    /// <summary>Real nflverse-backed full regular-season measurement (default 2018 W1–17).</summary>
    public static Task<SeasonScorecard> RunReal2018SeasonAsync(
        IServiceProvider services,
        int startWeek = 1,
        int endWeek = 17,
        ScoringType scoringType = ScoringType.Ppr,
        CancellationToken cancellationToken = default) =>
        RunSeasonAsync(
            services,
            season: 2018,
            startWeek,
            endWeek,
            scoringType,
            fixtureId: "nflverse",
            cancellationToken);

    public static Task<SeasonScorecard> RunSeasonAsync(
        IServiceProvider services,
        int season,
        int startWeek,
        int endWeek,
        ScoringType scoringType = ScoringType.Ppr,
        string? fixtureId = "nflverse",
        CancellationToken cancellationToken = default) =>
        RunSeasonAsync(
            services,
            new MultiWeekReplayRequest
            {
                Season = season,
                StartWeek = startWeek,
                EndWeek = endWeek,
                ScoringType = scoringType,
                FixtureId = fixtureId,
                ContinueOnWeekFailure = true
            },
            cancellationToken);

    public static Task<SeasonScorecard> RunSeasonAsync(
        IServiceProvider services,
        MultiWeekReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        var runner = services.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        return runner.RunAsync(request, cancellationToken);
    }
}
