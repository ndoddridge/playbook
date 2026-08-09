using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Runs identical frozen-model season evaluations across a diverse season sample.
/// Does not modify projection, decision, or confidence formulas.
/// </summary>
public sealed class MultiSeasonHistoricalBenchmarkRunner : IMultiSeasonHistoricalBenchmarkRunner
{
    private readonly IMultiWeekHistoricalReplayRunner _seasonRunner;
    private readonly IHistoricalSeasonCalendar _calendar;
    private readonly ILogger<MultiSeasonHistoricalBenchmarkRunner> _logger;

    public MultiSeasonHistoricalBenchmarkRunner(
        IMultiWeekHistoricalReplayRunner seasonRunner,
        IHistoricalSeasonCalendar calendar,
        ILogger<MultiSeasonHistoricalBenchmarkRunner> logger)
    {
        _seasonRunner = seasonRunner;
        _calendar = calendar;
        _logger = logger;
    }

    public async Task<MultiSeasonBenchmarkReport> RunAsync(
        MultiSeasonBenchmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Seasons is null || request.Seasons.Count == 0)
        {
            throw new ArgumentException("At least one season is required.", nameof(request));
        }

        var distinctSeasons = request.Seasons.Distinct().OrderBy(s => s).ToList();
        var scorecards = new List<SeasonScorecard>();

        foreach (var season in distinctSeasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endWeek = request.EndWeek
                          ?? await _calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                              .ConfigureAwait(false);
            var startWeek = request.StartWeek ?? 1;
            if (startWeek < 1 || endWeek < startWeek)
            {
                throw new InvalidOperationException(
                    $"Invalid week bounds for {season}: {startWeek}-{endWeek}.");
            }

            _logger.LogInformation(
                "Multi-season benchmark: starting season {Season} weeks {Start}-{End} (model frozen)",
                season,
                startWeek,
                endWeek);

            var scorecard = await _seasonRunner.RunAsync(
                    new MultiWeekReplayRequest
                    {
                        Season = season,
                        StartWeek = startWeek,
                        EndWeek = endWeek,
                        ScoringType = request.ScoringType,
                        FixtureId = request.FixtureId,
                        DecisionKind = request.DecisionKind,
                        ContinueOnWeekFailure = request.ContinueOnWeekFailure
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            // Temporal invariant across the season card.
            foreach (var proj in scorecard.ProjectionEvaluations)
            {
                if (proj.SourceWeeks.Any(w => w >= proj.Week))
                {
                    throw new InvalidOperationException(
                        $"Temporal leak in multi-season benchmark: {proj.Season} W{proj.Week} " +
                        $"{proj.PlayerName} source weeks include target/future.");
                }
            }

            scorecards.Add(scorecard);
            _logger.LogInformation(
                "Multi-season benchmark: finished {Season} weeks={Weeks} mae={Mae} baseA={BaseA}",
                season,
                scorecard.DataQuality.WeeksCompleted,
                scorecard.CurrentModelMae,
                scorecard.BaselineAMae);
        }

        var reportRequest = new MultiSeasonBenchmarkRequest
        {
            Seasons = distinctSeasons,
            ScoringType = request.ScoringType,
            FixtureId = request.FixtureId,
            DecisionKind = request.DecisionKind,
            ContinueOnWeekFailure = request.ContinueOnWeekFailure,
            StartWeek = request.StartWeek,
            EndWeek = request.EndWeek,
            SeasonRoles = request.SeasonRoles
        };

        return CrossSeasonBenchmarkBuilder.Build(reportRequest, scorecards);
    }
}
