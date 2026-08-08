using System.Security.Cryptography;
using System.Text;
using Playbook.Application.Injuries;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Builds explainable IntelligenceFact rows from injury profiles.
/// Only CURRENT designations produce scored health signals.
/// Missing historical data must never produce a false "healthy" (or false unhealthy) signal.
/// </summary>
public static class InjuryFactBuilder
{
    public static IReadOnlyList<IntelligenceFact> BuildFacts(IEnumerable<PlayerInjuryProfile> profiles)
    {
        var facts = new List<IntelligenceFact>();
        foreach (var profile in profiles)
        {
            facts.AddRange(BuildForProfile(profile));
        }

        return facts;
    }

    public static IReadOnlyList<IntelligenceFact> BuildForProfile(PlayerInjuryProfile profile)
    {
        var facts = new List<IntelligenceFact>();

        // Current injury → meaningful Health signal via existing rule ids.
        if (profile.CurrentDataStatus == CurrentInjuryDataStatus.Available &&
            profile.CurrentInjury is { } current)
        {
            var ruleId = InjuryIntelligenceMapping.ResolveRuleId(current);
            if (ruleId is not null)
            {
                var description = string.IsNullOrWhiteSpace(current.Description)
                    ? $"{current.Status} ({current.BodyPart ?? "undisclosed"})."
                    : current.Description!;

                facts.Add(new IntelligenceFact
                {
                    Id = StableId(profile.PlayerId, ruleId, current.ExternalId ?? current.Date.ToString("O")),
                    Title = InjuryIntelligenceMapping.HeadlineForRule(ruleId, current),
                    Description = description,
                    Category = IntelligenceCategory.Injury,
                    Confidence = InjuryIntelligenceMapping.ConfidenceForRule(ruleId),
                    Importance = InjuryIntelligenceMapping.ImportanceForRule(ruleId),
                    Source = IntelligenceSource.InjuryReport,
                    RelatedPlayerId = profile.PlayerId,
                    RelatedNewsArticleIds = [],
                    Created = current.Date,
                    SupportingEvidence =
                    [
                        $"Rule: {ruleId}",
                        "Scope: Current",
                        $"Status: {current.Status}",
                        $"Source: {current.Source ?? "Injury provider"}",
                        string.IsNullOrWhiteSpace(current.PracticeStatus)
                            ? "Practice: unavailable"
                            : $"Practice: {current.PracticeStatus}",
                        string.IsNullOrWhiteSpace(current.GameStatus)
                            ? "Game status: unavailable"
                            : $"Game status: {current.GameStatus}",
                        $"HistoricalDataStatus: {profile.HistoricalDataStatus}"
                    ],
                    Tags = ["current"]
                });
            }
        }

        // Intentionally emit nothing when:
        // - NoCurrentInjury + historical NotSupported/Unavailable/NotSynced
        // That state must not become a "healthy" or "unhealthy" scored fact.
        // UI surfaces HistoricalAvailabilityMessage / RiskSummary instead.

        return facts;
    }

    private static Guid StableId(Guid playerId, string ruleId, string key)
    {
        var raw = $"injury-fact:{playerId:N}:{ruleId}:{key}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return new Guid(hash);
    }
}
