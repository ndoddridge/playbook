using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Composes roster-level intelligence for the currently selected league + owned team.
/// </summary>
public interface IFantasyTeamIntelligenceService
{
    FantasyTeamIntelligenceReport GetReport();
}
