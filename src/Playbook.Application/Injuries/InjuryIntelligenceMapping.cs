using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;

namespace Playbook.Application.Injuries;

/// <summary>
/// Deterministic mapping from injury designations to Intelligence rule ids / signals.
/// </summary>
public static class InjuryIntelligenceMapping
{
    public static string? ResolveRuleId(PlayerInjuryRecord record)
    {
        var blob = string.Join(
            ' ',
            record.Status,
            record.GameStatus,
            record.PracticeStatus,
            record.Description);

        if (ContainsAny(blob, "injured reserve", " ir ", "to ir", "placed on ir") ||
            EqualsStatus(record.Status, "IR", "Injured Reserve"))
        {
            return "injury-ir";
        }

        if (EqualsStatus(record.Status, "Out") ||
            ContainsAny(blob, "ruled out", "will not play", "inactive"))
        {
            return "injury-out";
        }

        if (EqualsStatus(record.Status, "Doubtful") || ContainsAny(blob, "doubtful"))
        {
            return "injury-doubtful";
        }

        if (EqualsStatus(record.Status, "Questionable") || ContainsAny(blob, "questionable"))
        {
            return "injury-questionable";
        }

        if (ContainsAny(blob, "limited practice", "limited participant", "limited in practice", "dnp", "did not practice"))
        {
            return "injury-limited";
        }

        if (ContainsAny(blob, "full participant", "full practice", "returned to practice", "cleared", "no longer on") ||
            EqualsStatus(record.Status, "Active", "Healthy", "Probable"))
        {
            // Only treat Active/returned as positive when it is a recovery signal with injury context.
            if (record.BodyPart is not null ||
                ContainsAny(blob, "returned", "cleared", "full participant", "full practice"))
            {
                return "injury-positive";
            }
        }

        return null;
    }

    public static IntelligenceImportance ImportanceForRule(string ruleId) => ruleId switch
    {
        "injury-out" or "injury-ir" => IntelligenceImportance.Critical,
        "injury-doubtful" or "injury-questionable" or "injury-limited" => IntelligenceImportance.High,
        "injury-positive" => IntelligenceImportance.Medium,
        _ => IntelligenceImportance.Low
    };

    public static int ConfidenceForRule(string ruleId) => ruleId switch
    {
        "injury-out" => 94,
        "injury-ir" => 92,
        "injury-doubtful" => 88,
        "injury-questionable" => 84,
        "injury-limited" => 80,
        "injury-positive" => 78,
        _ => 70
    };

    public static string HeadlineForRule(string ruleId, PlayerInjuryRecord record)
    {
        var part = string.IsNullOrWhiteSpace(record.BodyPart) ? "injury" : record.BodyPart!;
        return ruleId switch
        {
            "injury-out" => $"Out — {part} designation removes near-term availability.",
            "injury-ir" => $"Injured Reserve — {part} signals multi-week absence risk.",
            "injury-doubtful" => $"Doubtful — {part} implies low play probability.",
            "injury-questionable" => $"Questionable — {part} creates week-to-week uncertainty.",
            "injury-limited" => $"Limited practice — {part} is a moderate health risk.",
            "injury-positive" => $"Positive recovery signal — {part} trending toward availability.",
            _ => $"Injury update — {record.Status}"
        };
    }

    /// <summary>
    /// Conservative projection multiplier for major current designations.
    /// Out/IR heavily suppress; Questionable/Doubtful modestly trim; else null (no direct override).
    /// </summary>
    public static decimal? ProjectionHealthMultiplier(PlayerInjuryRecord? current)
    {
        if (current is null)
        {
            return null;
        }

        var rule = ResolveRuleId(current);
        return rule switch
        {
            "injury-out" => 0.15m,
            "injury-ir" => 0.10m,
            "injury-doubtful" => 0.55m,
            "injury-questionable" => 0.85m,
            "injury-limited" => 0.90m,
            _ => null
        };
    }

    private static bool EqualsStatus(string? status, params string[] options) =>
        options.Any(o => string.Equals(status?.Trim(), o, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string text, params string[] phrases)
    {
        var hay = $" {text.ToLowerInvariant()} ";
        return phrases.Any(p => hay.Contains(p.ToLowerInvariant(), StringComparison.Ordinal));
    }
}
