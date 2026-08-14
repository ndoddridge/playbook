using Playbook.Core.Injuries.Models;
using Playbook.Core.Research;

namespace Playbook.Application.Research;

/// <summary>Grades one snapshot against its actual outcome. Pure — no side effects, no persistence.</summary>
public interface IPredictionOutcomeClassifier
{
    PredictionOutcomeAssessment Classify(
        PredictionSnapshot snapshot,
        decimal? actualValue,
        PlayerInjuryRecord? injuryAtGradingTime);
}
