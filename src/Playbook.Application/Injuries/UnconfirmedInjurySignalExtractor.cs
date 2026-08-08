using System.Security.Cryptography;
using System.Text;
using Playbook.Core.Injuries.Models;
using Playbook.Core.News;

namespace Playbook.Application.Injuries;

/// <summary>
/// Extracts possible injury concerns from news. Never promotes speculation to verified records.
/// </summary>
public static class UnconfirmedInjurySignalExtractor
{
    private static readonly string[] SpeculativePhrases =
    [
        "reportedly",
        "dealing with",
        "appeared limited",
        "missed practice",
        "did not practice",
        "monitoring",
        "being monitored",
        "injury buzz",
        "concern about",
        "could be dealing",
        "expected to be limited",
        "held out",
        "sidelined with",
        "nursing a",
        "bothered by",
        "day-to-day",
        "questionable injury",
        "soft tissue",
        "undisclosed"
    ];

    private static readonly string[] InjuryContextPhrases =
    [
        "injur",
        "hamstring",
        "ankle",
        "knee",
        "shoulder",
        "concussion",
        "quad",
        "groin",
        "calf",
        "achilles",
        "practice",
        "limited",
        "questionable",
        "doubtful",
        "out "
    ];

    private static readonly string[] PositiveContradictionPhrases =
    [
        "full participant",
        "full practice",
        "cleared",
        "no injury",
        "not injured",
        "returned to practice",
        "healthy"
    ];

    public static IReadOnlyList<UnconfirmedInjurySignal> ExtractForPlayer(
        Guid playerId,
        IEnumerable<NewsArticle> articles,
        bool hasConfirmedCurrentInjury)
    {
        var candidates = new List<UnconfirmedInjurySignal>();
        foreach (var article in articles)
        {
            if (!article.RelatedPlayerIds.Contains(playerId) &&
                !ArticleMentionsPlayerLoosely(article, playerId))
            {
                continue;
            }

            var text = $"{article.Title} {article.Summary}";
            if (!ContainsAny(text, InjuryContextPhrases) && article.Category != NewsCategory.Injury)
            {
                continue;
            }

            var speculative = ContainsAny(text, SpeculativePhrases);
            var designationReport = ContainsAny(text,
                "placed on ir", "injured reserve", "ruled out", "listed as questionable",
                "listed as doubtful", "inactive", "missed practice", "did not practice",
                "limited participant", "limited practice");

            if (!speculative && !designationReport && article.Category != NewsCategory.Injury)
            {
                continue;
            }

            // If we already have a confirmed current designation, skip pure designation echoes
            // unless the article is clearly speculative buzz.
            if (hasConfirmedCurrentInjury && !speculative)
            {
                continue;
            }

            var confidence = designationReport && !speculative ? 62 : 45;
            var sourceConfidence = speculative
                ? InjurySourceConfidence.Unconfirmed
                : designationReport
                    ? InjurySourceConfidence.Reported
                    : InjurySourceConfidence.Unconfirmed;

            if (speculative)
            {
                confidence += 5;
            }

            if (article.Category == NewsCategory.Injury)
            {
                confidence += 8;
            }

            if (ContainsAny(text, "missed practice", "did not practice", "limited"))
            {
                confidence += 12;
            }

            if (ContainsAny(text, "reportedly", "could be", "buzz"))
            {
                confidence -= 8;
            }

            var contradicted = ContainsAny(text, PositiveContradictionPhrases);
            if (contradicted)
            {
                confidence -= 20;
            }

            confidence = Math.Clamp(confidence, 15, 85);

            candidates.Add(new UnconfirmedInjurySignal
            {
                Id = StableId(playerId, article.Id),
                PlayerId = playerId,
                Headline = article.Title,
                Detail = string.IsNullOrWhiteSpace(article.Summary) ? null : article.Summary,
                BodyPart = ExtractBodyPart(text),
                Source = article.Source,
                SourceUrl = article.Url,
                Published = article.Published,
                LastUpdated = article.Published,
                Confidence = confidence,
                SourceCount = 1,
                RelatedNewsArticleIds = [article.Id],
                IsContradicted = contradicted,
                SourceConfidence = sourceConfidence
            });
        }

        // Merge overlapping headlines / body parts into multi-source signals.
        return Merge(candidates)
            .OrderByDescending(s => s.Published)
            .ThenByDescending(s => s.Confidence)
            .ToList();
    }

    private static IEnumerable<UnconfirmedInjurySignal> Merge(List<UnconfirmedInjurySignal> signals)
    {
        var groups = signals.GroupBy(s =>
            $"{Normalize(s.BodyPart)}|{Normalize(s.Headline)[..Math.Min(40, Normalize(s.Headline).Length)]}");

        foreach (var group in groups)
        {
            var ordered = group.OrderByDescending(s => s.Published).ToList();
            var primary = ordered[0];
            if (ordered.Count == 1)
            {
                yield return primary;
                continue;
            }

            var confidences = ordered.Select(s => s.Confidence).ToList();
            var avg = (int)Math.Round(confidences.Average());
            // Conflicting confidence → pull toward center rather than picking extremes.
            if (confidences.Max() - confidences.Min() >= 25)
            {
                avg = Math.Clamp(avg - 8, 15, 85);
            }

            var confidenceLevel = ordered.Any(s => s.SourceConfidence == InjurySourceConfidence.Unconfirmed)
                ? InjurySourceConfidence.Unconfirmed
                : ordered.Any(s => s.SourceConfidence == InjurySourceConfidence.Reported)
                    ? InjurySourceConfidence.Reported
                    : InjurySourceConfidence.Unconfirmed;

            yield return new UnconfirmedInjurySignal
            {
                Id = primary.Id,
                PlayerId = primary.PlayerId,
                Headline = primary.Headline,
                Detail = primary.Detail,
                BodyPart = primary.BodyPart ?? ordered.Select(s => s.BodyPart).FirstOrDefault(b => b is not null),
                Source = string.Join(", ", ordered.Select(s => s.Source).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)),
                SourceUrl = primary.SourceUrl,
                Published = ordered.Max(s => s.Published),
                LastUpdated = ordered.Max(s => s.LastUpdated),
                Confidence = avg,
                SourceCount = ordered.Count,
                RelatedNewsArticleIds = ordered.SelectMany(s => s.RelatedNewsArticleIds).Distinct().ToList(),
                IsContradicted = ordered.Any(s => s.IsContradicted),
                SourceConfidence = confidenceLevel
            };
        }
    }

    private static bool ArticleMentionsPlayerLoosely(NewsArticle article, Guid playerId) =>
        article.RelatedPlayerIds.Contains(playerId);

    private static string? ExtractBodyPart(string text)
    {
        string[] parts =
        [
            "Achilles", "Hamstring", "Ankle", "Knee", "Shoulder", "Concussion",
            "Quad", "Quadriceps", "Groin", "Calf", "Foot", "Wrist", "Elbow", "Hip", "Back"
        ];
        foreach (var part in parts)
        {
            if (text.Contains(part, StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] phrases)
    {
        var hay = text.ToLowerInvariant();
        return phrases.Any(p => hay.Contains(p.ToLowerInvariant(), StringComparison.Ordinal));
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static Guid StableId(Guid playerId, Guid articleId)
    {
        var raw = $"unconfirmed-injury:{playerId:N}:{articleId:N}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return new Guid(hash);
    }
}
