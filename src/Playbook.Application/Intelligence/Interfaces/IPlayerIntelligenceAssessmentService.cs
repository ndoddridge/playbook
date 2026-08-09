using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Composes existing intelligence / injury / projection / stats into a scannable assessment.
/// Does not create parallel data pipelines.
/// </summary>
public interface IPlayerIntelligenceAssessmentService
{
    PlayerIntelligenceAssessment GetAssessment(Guid playerId);
}
