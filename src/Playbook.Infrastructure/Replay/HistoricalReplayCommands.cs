using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Replay;
using Playbook.Core.Leagues;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Developer-facing helpers for executing historical replays without mutating live league UI state.
/// <code>
/// await HistoricalReplayCommands.RunAsync(services, season: 2018, week: 7);
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
}
