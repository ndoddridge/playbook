using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Runs independent week replays across a season range and builds a measurement scorecard.
/// Each week keeps its own information cutoff; Week N never sees Week N outcomes before decisions.
/// </summary>
public sealed class MultiWeekHistoricalReplayRunner : IMultiWeekHistoricalReplayRunner
{
    private readonly IHistoricalReplayRunner _weekRunner;
    private readonly ILogger<MultiWeekHistoricalReplayRunner> _logger;

    public MultiWeekHistoricalReplayRunner(
        IHistoricalReplayRunner weekRunner,
        ILogger<MultiWeekHistoricalReplayRunner> logger)
    {
        _weekRunner = weekRunner;
        _logger = logger;
    }

    public async Task<SeasonScorecard> RunAsync(
        MultiWeekReplayRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.StartWeek < 1 || request.EndWeek < request.StartWeek)
        {
            throw new ArgumentException(
                $"Invalid week range {request.StartWeek}-{request.EndWeek}.",
                nameof(request));
        }

        var weekReports = new List<HistoricalReplayReport>();
        var skipped = new List<WeekReplaySkip>();

        for (var week = request.StartWeek; week <= request.EndWeek; week++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var weekRequest = new HistoricalReplayRequest
            {
                Season = request.Season,
                Week = week,
                ScoringType = request.ScoringType,
                FixtureId = request.FixtureId,
                DecisionKind = request.DecisionKind,
                CandidateUniverse = request.CandidateUniverse
            };

            try
            {
                _logger.LogInformation(
                    "Multi-week replay: starting {Season} week {Week}",
                    request.Season,
                    week);
                var report = await _weekRunner.RunAsync(weekRequest, cancellationToken)
                    .ConfigureAwait(false);

                // Hard temporal invariant: every projection source week must be < target week.
                foreach (var grade in report.Grades)
                {
                    if (grade.ProjectionSourceWeeks.Any(w => w >= week))
                    {
                        throw new InvalidOperationException(
                            $"Temporal leak in season run: {report.Season} W{week} grade for " +
                            $"{grade.PlayerName} includes source week >= target.");
                    }
                }

                foreach (var proj in report.ProjectionEvaluations)
                {
                    if (proj.SourceWeeks.Any(w => w >= week))
                    {
                        throw new InvalidOperationException(
                            $"Temporal leak in season run: {report.Season} W{week} projection for " +
                            $"{proj.PlayerName} includes source week >= target.");
                    }
                }

                if (report.Week != week || report.Season != request.Season)
                {
                    throw new InvalidOperationException("Week report identity mismatch.");
                }

                weekReports.Add(report);
                _logger.LogInformation(
                    "Multi-week replay: finished {Season} W{Week} decisions={Decisions} mae={Mae}",
                    request.Season,
                    week,
                    report.DecisionCount,
                    report.AverageProjectionAbsoluteError);
            }
            catch (Exception ex) when (request.ContinueOnWeekFailure)
            {
                _logger.LogWarning(
                    ex,
                    "Multi-week replay: skipped {Season} week {Week}",
                    request.Season,
                    week);
                skipped.Add(new WeekReplaySkip
                {
                    Season = request.Season,
                    Week = week,
                    Reason = ex.Message
                });
            }
        }

        return SeasonScorecardBuilder.Build(request, weekReports, skipped);
    }
}
