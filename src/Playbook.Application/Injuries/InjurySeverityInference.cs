using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries;

/// <summary>Infers severity only when status language clearly implies it; otherwise null.</summary>
public static class InjurySeverityInference
{
    public static InjurySeverity? FromStatus(string? status, int? gamesMissed = null)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return gamesMissed switch
            {
                >= 6 => InjurySeverity.Major,
                >= 2 => InjurySeverity.Significant,
                _ => null
            };
        }

        if (status.Contains("Injured Reserve", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("IR", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("PUP", StringComparison.OrdinalIgnoreCase))
        {
            return InjurySeverity.Major;
        }

        if (status.Equals("Out", StringComparison.OrdinalIgnoreCase))
        {
            return gamesMissed >= 4 ? InjurySeverity.Major : InjurySeverity.Significant;
        }

        if (status.Equals("Doubtful", StringComparison.OrdinalIgnoreCase))
        {
            return InjurySeverity.Significant;
        }

        if (status.Equals("Questionable", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("Limited", StringComparison.OrdinalIgnoreCase))
        {
            return InjurySeverity.Moderate;
        }

        return null;
    }
}
