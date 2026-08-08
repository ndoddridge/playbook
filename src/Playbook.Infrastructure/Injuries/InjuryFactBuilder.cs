using System.Security.Cryptography;
using System.Text;
using Playbook.Application.Injuries;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Builds explainable IntelligenceFact rows from structured injury records.
/// </summary>
public static class InjuryFactBuilder
{
    public static IReadOnlyList<IntelligenceFact> BuildFacts(IEnumerable<PlayerInjuryRecord> currentInjuries)
    {
        var facts = new List<IntelligenceFact>();
        foreach (var injury in currentInjuries.Where(r => r.IsCurrent))
        {
            var ruleId = InjuryIntelligenceMapping.ResolveRuleId(injury);
            if (ruleId is null)
            {
                continue;
            }

            var title = InjuryIntelligenceMapping.HeadlineForRule(ruleId, injury);
            var description = string.IsNullOrWhiteSpace(injury.Description)
                ? $"{injury.Status} ({injury.BodyPart ?? "undisclosed"})."
                : injury.Description!;

            facts.Add(new IntelligenceFact
            {
                Id = StableId(injury, ruleId),
                Title = title,
                Description = description,
                Category = IntelligenceCategory.Injury,
                Confidence = InjuryIntelligenceMapping.ConfidenceForRule(ruleId),
                Importance = InjuryIntelligenceMapping.ImportanceForRule(ruleId),
                Source = IntelligenceSource.InjuryReport,
                RelatedPlayerId = injury.PlayerId,
                RelatedNewsArticleIds = [],
                Created = injury.Date,
                SupportingEvidence =
                [
                    $"Rule: {ruleId}",
                    $"Status: {injury.Status}",
                    $"Source: {injury.Source ?? "Injury provider"}",
                    string.IsNullOrWhiteSpace(injury.PracticeStatus)
                        ? "Practice: unavailable"
                        : $"Practice: {injury.PracticeStatus}",
                    string.IsNullOrWhiteSpace(injury.GameStatus)
                        ? "Game status: unavailable"
                        : $"Game status: {injury.GameStatus}"
                ]
            });
        }

        return facts;
    }

    private static Guid StableId(PlayerInjuryRecord injury, string ruleId)
    {
        var raw = $"injury-fact:{injury.PlayerId:N}:{ruleId}:{injury.ExternalId}:{injury.Date:O}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return new Guid(hash);
    }
}
