using Playbook.Core.Injuries.Models;

namespace Playbook.Application.Injuries;

/// <summary>
/// Transparent, testable relevance of historical injuries to current evaluation.
/// Does not erase history — only scores emphasis.
/// </summary>
public static class InjuryRelevanceCalculator
{
    public static InjuryHistoryEntry Score(
        PlayerInjuryRecord record,
        DateTimeOffset asOf,
        IReadOnlyList<PlayerInjuryRecord>? siblingHistory = null)
    {
        var score = 40;
        var reasons = new List<string>();

        if (record.IsCurrent)
        {
            score += 45;
            reasons.Add("currently active designation");
        }

        var ageDays = Math.Max(0, (asOf - record.Date).TotalDays);
        if (ageDays <= 14)
        {
            score += 30;
            reasons.Add("within 14 days");
        }
        else if (ageDays <= 45)
        {
            score += 20;
            reasons.Add("within 45 days");
        }
        else if (ageDays <= 120)
        {
            score += 10;
            reasons.Add("within ~4 months");
        }
        else if (ageDays <= 365)
        {
            score += 0;
            reasons.Add("within the past year");
        }
        else if (ageDays <= 365 * 2)
        {
            score -= 15;
            reasons.Add("1–2 years old");
        }
        else
        {
            score -= 30;
            reasons.Add("older than 2 years");
        }

        var severityBoost = record.Severity switch
        {
            InjurySeverity.Major => 25,
            InjurySeverity.Significant => 18,
            InjurySeverity.Moderate => 10,
            InjurySeverity.Minor => 4,
            _ => InferSeverityBoost(record)
        };
        if (severityBoost != 0)
        {
            score += severityBoost;
            reasons.Add($"severity +{severityBoost}");
        }

        if (record.GamesMissed is int missed)
        {
            if (missed >= 6)
            {
                score += 15;
                reasons.Add($"{missed} games missed");
            }
            else if (missed >= 2)
            {
                score += 8;
                reasons.Add($"{missed} games missed");
            }
        }

        if (siblingHistory is { Count: > 0 } &&
            !string.IsNullOrWhiteSpace(record.BodyPart))
        {
            var repeats = siblingHistory.Count(r =>
                !string.Equals(r.ExternalId, record.ExternalId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.BodyPart, record.BodyPart, StringComparison.OrdinalIgnoreCase));
            if (repeats >= 2)
            {
                score += 12;
                reasons.Add($"repeated {record.BodyPart} history ({repeats + 1}x)");
            }
            else if (repeats == 1)
            {
                score += 6;
                reasons.Add($"prior {record.BodyPart} history");
            }
        }

        if (IsFullParticipationReturn(record))
        {
            score -= 12;
            reasons.Add("returned to full participation");
        }

        if (record.Level == InjuryCompetitionLevel.College && ageDays > 365)
        {
            score -= 10;
            reasons.Add("college injury without recent NFL recurrence weight");
        }

        score = Math.Clamp(score, 0, 100);
        var band = score switch
        {
            >= 70 => InjuryRelevanceBand.High,
            >= 45 => InjuryRelevanceBand.Moderate,
            >= 25 => InjuryRelevanceBand.Low,
            _ => InjuryRelevanceBand.Minimal
        };

        return new InjuryHistoryEntry
        {
            Record = record,
            RelevanceScore = score,
            Band = band,
            RelevanceReason = reasons.Count == 0 ? "baseline historical context" : string.Join("; ", reasons)
        };
    }

    public static IReadOnlyList<InjuryHistoryEntry> ScoreAll(
        IEnumerable<PlayerInjuryRecord> records,
        DateTimeOffset asOf)
    {
        var list = records.ToList();
        return list
            .Select(r => Score(r, asOf, list))
            .OrderByDescending(e => e.Record.Date)
            .ThenByDescending(e => e.RelevanceScore)
            .ToList();
    }

    private static int InferSeverityBoost(PlayerInjuryRecord record)
    {
        var status = record.Status ?? string.Empty;
        if (status.Contains("Injured Reserve", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("IR", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("PUP", StringComparison.OrdinalIgnoreCase))
        {
            return 22;
        }

        if (status.Equals("Out", StringComparison.OrdinalIgnoreCase))
        {
            return 16;
        }

        if (status.Equals("Doubtful", StringComparison.OrdinalIgnoreCase))
        {
            return 12;
        }

        if (status.Equals("Questionable", StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }

        return 0;
    }

    private static bool IsFullParticipationReturn(PlayerInjuryRecord record)
    {
        var blob = $"{record.PracticeStatus} {record.Description} {record.Status}";
        return blob.Contains("Full Participant", StringComparison.OrdinalIgnoreCase) ||
               blob.Contains("full practice", StringComparison.OrdinalIgnoreCase) ||
               blob.Contains("cleared", StringComparison.OrdinalIgnoreCase);
    }
}
