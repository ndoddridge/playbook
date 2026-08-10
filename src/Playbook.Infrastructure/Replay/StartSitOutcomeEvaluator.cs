using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Grades Start/Sit decisions comparatively when an alternative exists.
/// Does not claim confidence calibration — only records outcomes vs predictions.
/// </summary>
public sealed class StartSitOutcomeEvaluator : IDecisionOutcomeEvaluator
{
    public IReadOnlyList<ReplayDecisionGrade> EvaluateStartSit(
        IReadOnlyList<DecisionRecord> decisions,
        IReadOnlyList<DecisionResult> decisionResults,
        HistoricalWeekOutcomes outcomes,
        HistoricalSnapshot? snapshot = null,
        double meaningfulMarginPoints = 1.0)
    {
        var resultsById = decisionResults.ToDictionary(r => r.DecisionId);
        var playersById = snapshot?.Players.ToDictionary(p => p.PlayerId) ?? new Dictionary<Guid, HistoricalPlayerState>();
        var grades = new List<ReplayDecisionGrade>();

        foreach (var decision in decisions)
        {
            resultsById.TryGetValue(decision.DecisionId, out var result);
            playersById.TryGetValue(decision.PlayerId, out var playerState);

            outcomes.ByPlayerId.TryGetValue(decision.PlayerId, out var actual);
            double? actualPts = actual?.ActualFantasyPoints;
            double? absErr = actualPts is null ? null : Math.Abs(actualPts.Value - decision.ExpectedValue);
            double? signedErr = actualPts is null ? null : actualPts.Value - decision.ExpectedValue;
            double? squaredErr = actualPts is null ? null : Math.Pow(actualPts.Value - decision.ExpectedValue, 2);

            // Prefer the strongest alternative by expected value among those considered.
            var altResult = decision.AlternativesConsidered
                .Select(id => decisionResults.FirstOrDefault(r => r.PlayerId == id))
                .Where(r => r is not null)
                .Cast<DecisionResult>()
                .OrderByDescending(r => r.Values.ExpectedValue)
                .ThenByDescending(r => r.Values.DecisionValue)
                .FirstOrDefault();

            Guid? altId = altResult?.PlayerId;
            double? altExpected = altResult?.Values.ExpectedValue;
            double? altActual = null;
            string? altName = altResult?.PlayerName;
            if (altId is Guid alternativeId &&
                outcomes.ByPlayerId.TryGetValue(alternativeId, out var altOutcome))
            {
                altName = altOutcome.PlayerName;
                altActual = altOutcome.ActualFantasyPoints;
            }

            double? differential = null;
            bool? wasCorrect = null;
            var marginMattered = false;
            string summary;

            if (actualPts is null)
            {
                summary = "Ungraded — actual fantasy points unavailable.";
            }
            else if (decision.Recommendation == DecisionRecommendation.Start &&
                     altActual is not null)
            {
                differential = actualPts.Value - altActual.Value;
                marginMattered = Math.Abs(differential.Value) >= meaningfulMarginPoints;
                wasCorrect = differential >= 0;
                summary = wasCorrect == true
                    ? $"CORRECT — started player outperformed alternative by {differential:0.0} pts."
                    : $"INCORRECT — alternative outperformed by {Math.Abs(differential.Value):0.0} pts (decision cost {differential:0.0}).";
            }
            else if (decision.Recommendation == DecisionRecommendation.Sit &&
                     altActual is not null)
            {
                differential = altActual.Value - actualPts.Value;
                marginMattered = Math.Abs(differential.Value) >= meaningfulMarginPoints;
                wasCorrect = differential >= 0;
                summary = wasCorrect == true
                    ? $"CORRECT — sitting this player avoided a {differential:0.0}-pt deficit vs alternative."
                    : $"INCORRECT — sat player who beat alternative by {Math.Abs(differential.Value):0.0} pts.";
            }
            else
            {
                summary =
                    $"Recorded actual {actualPts:0.0} vs expected {decision.ExpectedValue:0.0} " +
                    $"(abs error {absErr:0.0}). No comparative alternative graded.";
            }

            double? baseA = playerState?.BaselineRecentAveragePoints;
            double? baseB = playerState?.BaselineOpportunityAwarePoints;
            double? baseAErr = actualPts is null || baseA is null ? null : Math.Abs(actualPts.Value - baseA.Value);
            double? baseBErr = actualPts is null || baseB is null ? null : Math.Abs(actualPts.Value - baseB.Value);

            grades.Add(new ReplayDecisionGrade
            {
                DecisionId = decision.DecisionId,
                PlayerId = decision.PlayerId,
                PlayerName = decision.PlayerName,
                Recommendation = decision.Recommendation,
                Confidence = decision.Confidence,
                ExpectedValue = decision.ExpectedValue,
                ActualFantasyPoints = actualPts,
                ProjectionAbsoluteError = absErr,
                ProjectionSignedError = signedErr,
                ProjectionSquaredError = squaredErr,
                DataSufficiency = playerState?.DataSufficiency,
                ProjectionSourceWeeks = playerState?.ProjectionSourceWeeks ?? [],
                BaselineRecentAveragePoints = baseA,
                BaselineOpportunityAwarePoints = baseB,
                BaselineRecentAbsoluteError = baseAErr,
                BaselineOpportunityAbsoluteError = baseBErr,
                AlternativePlayerId = altId,
                AlternativePlayerName = altName,
                AlternativeExpectedValue = altExpected,
                AlternativeActualFantasyPoints = altActual,
                ActualDecisionDifferential = differential,
                WasCorrect = wasCorrect,
                MarginMattered = marginMattered,
                EvaluationSummary = summary,
                SupportingEvidence = decision.SupportingEvidence,
                OpposingEvidence = decision.OpposingEvidence,
                Unknowns = decision.Unknowns,
                Rationale = result?.Rationale ?? decision.Rationale
            });
        }

        return grades;
    }
}
