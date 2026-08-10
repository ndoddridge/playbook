using System.Security.Cryptography;
using System.Text;
using Playbook.Application.Injuries;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Builds explainable IntelligenceFact rows from injury profiles.
/// Distinguishes current verified, historical relevance, and unconfirmed news signals.
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

        // Current verified injury → strong Health signal.
        if (profile.CurrentDataStatus == CurrentInjuryDataStatus.Available &&
            profile.CurrentInjury is { } current)
        {
            var ruleId = InjuryIntelligenceMapping.ResolveRuleId(current);
            if (ruleId is not null)
            {
                facts.Add(BuildCurrentFact(profile, current, ruleId));
            }
        }

        // High-relevance historical → Historical Risk context (weaker than current).
        foreach (var entry in profile.HistoricalEntries.Where(e => e.Band == InjuryRelevanceBand.High).Take(2))
        {
            facts.Add(BuildHistoricalFact(profile, entry));
        }

        // Unconfirmed news signals → weak "Possible injury concern — unconfirmed".
        foreach (var signal in profile.UnconfirmedSignals.Take(3))
        {
            facts.Add(BuildUnconfirmedFact(profile, signal));
        }

        return facts;
    }

    private static IntelligenceFact BuildCurrentFact(
        PlayerInjuryProfile profile,
        PlayerInjuryRecord current,
        string ruleId)
    {
        var description = string.IsNullOrWhiteSpace(current.Description)
            ? $"{current.Status} ({current.BodyPart ?? "undisclosed"})."
            : current.Description!;

        return new IntelligenceFact
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
                "Scope: Current Injury",
                "Verification: Verified",
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
            Tags = ["current", "verified", "health-risk"]
        };
    }

    private static IntelligenceFact BuildHistoricalFact(
        PlayerInjuryProfile profile,
        InjuryHistoryEntry entry)
    {
        var record = entry.Record;
        var level = record.Level == InjuryCompetitionLevel.College ? "College" : "NFL";
        return new IntelligenceFact
        {
            Id = StableId(profile.PlayerId, "injury-historical", record.ExternalId ?? record.Date.ToString("O")),
            Title = $"Historical risk — {record.BodyPart ?? "injury"} ({level})",
            Description =
                $"High-relevance historical {level.ToLowerInvariant()} injury context " +
                $"({record.Status}, relevance {entry.RelevanceScore}/100). " +
                (entry.RelevanceReason ?? "Recency/severity weighted."),
            Category = IntelligenceCategory.Injury,
            Confidence = Math.Clamp(55 + entry.RelevanceScore / 5, 55, 78),
            Importance = IntelligenceImportance.Medium,
            Source = IntelligenceSource.Historical,
            RelatedPlayerId = profile.PlayerId,
            RelatedNewsArticleIds = [],
            Created = record.Date,
            SupportingEvidence =
            [
                "Rule: injury-historical",
                "Scope: Historical Risk",
                "Verification: Historical",
                $"Relevance: {entry.RelevanceScore} ({entry.Band})",
                $"Reason: {entry.RelevanceReason}",
                $"Level: {level}",
                $"Source: {record.Source ?? "Historical provider"}"
            ],
            Tags = ["historical", "historical-risk", "verified"]
        };
    }

    private static IntelligenceFact BuildUnconfirmedFact(
        PlayerInjuryProfile profile,
        UnconfirmedInjurySignal signal)
    {
        var confidence = signal.IsContradicted
            ? Math.Max(20, signal.Confidence - 15)
            : signal.Confidence;

        var isReported = signal.SourceConfidence == InjurySourceConfidence.Reported;
        var ruleId = isReported ? "injury-reported" : "injury-unconfirmed";
        var scope = isReported ? "Reported Injury Concern" : "Unconfirmed Injury Concern";
        var title = isReported
            ? "Injury concern — reported (not a structured designation)"
            : "Possible injury concern — unconfirmed";

        return new IntelligenceFact
        {
            Id = signal.Id,
            Title = title,
            Description = string.IsNullOrWhiteSpace(signal.Detail)
                ? signal.Headline
                : $"{signal.Headline}. {signal.Detail}",
            Category = IntelligenceCategory.Injury,
            Confidence = confidence,
            Importance = confidence >= 60 ? IntelligenceImportance.Medium : IntelligenceImportance.Low,
            Source = IntelligenceSource.News,
            RelatedPlayerId = profile.PlayerId,
            RelatedNewsArticleIds = signal.RelatedNewsArticleIds,
            Created = signal.Published,
            SupportingEvidence =
            [
                $"Rule: {ruleId}",
                $"Scope: {scope}",
                $"Verification: {signal.VerificationLabel}",
                $"Confidence: {signal.ConfidenceLabel} ({signal.Confidence})",
                $"Sources: {signal.SourceCount}",
                $"Source: {signal.Source}",
                signal.IsContradicted
                    ? "Note: contradictory positive language present — confidence reduced"
                    : "Note: not an official structured injury designation"
            ],
            Tags = [isReported ? "reported" : "unconfirmed", "injury-buzz", "news"]
        };
    }

    private static Guid StableId(Guid playerId, string ruleId, string key)
    {
        var raw = $"injury-fact:{playerId:N}:{ruleId}:{key}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return new Guid(hash);
    }
}
