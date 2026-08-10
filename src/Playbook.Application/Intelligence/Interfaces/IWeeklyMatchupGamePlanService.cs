using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Weekly matchup / game-plan composition for the currently selected league + owned team.
/// </summary>
public interface IWeeklyMatchupGamePlanService
{
    WeeklyMatchupGamePlan GetPlan();
}
