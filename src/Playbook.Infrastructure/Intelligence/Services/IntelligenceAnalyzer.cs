using System.Security.Cryptography;
using System.Text;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.News;
using Playbook.Core.Players;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Deterministic, explainable rule engine. Same articles + players ⇒ same facts.
/// No ML / LLMs — every match cites rule id and phrase.
/// </summary>
public sealed class IntelligenceAnalyzer : IIntelligenceAnalyzer
{
    private static readonly IntelligenceRule[] Rules =
    [
        Rule("injury-out", ["ruled out", " will be out", " is out ", "out for", "inactive"],
            IntelligenceCategory.Injury, IntelligenceImportance.Critical, 92,
            "High injury intelligence: player availability is compromised.",
            "Matched availability-out language"),
        Rule("injury-ir", ["injured reserve", " placed on ir", " to ir", " IR "],
            IntelligenceCategory.Injury, IntelligenceImportance.Critical, 90,
            "High injury intelligence: IR designation signals multi-week absence risk.",
            "Matched IR language"),
        Rule("injury-questionable", ["questionable"],
            IntelligenceCategory.Injury, IntelligenceImportance.High, 84,
            "Injury concern: questionable tag creates week-to-week uncertainty.",
            "Matched 'questionable'"),
        Rule("injury-doubtful", ["doubtful"],
            IntelligenceCategory.Injury, IntelligenceImportance.High, 86,
            "Injury concern: doubtful status implies low play probability.",
            "Matched 'doubtful'"),
        Rule("injury-limited", ["limited practice", "limited in practice", "limited participant"],
            IntelligenceCategory.Injury, IntelligenceImportance.High, 80,
            "Injury concern: limited practice participation.",
            "Matched limited-practice language"),
        Rule("injury-positive", ["full participant", "full practice", "returned to practice", "cleared"],
            IntelligenceCategory.Injury, IntelligenceImportance.Medium, 78,
            "Positive health signal: full/cleared participation language.",
            "Matched positive health language"),
        Rule("usage-first-team", ["first-team", "first team reps", "first-team reps", "with the ones"],
            IntelligenceCategory.Usage, IntelligenceImportance.High, 82,
            "Usage increasing: first-team reps indicate elevated opportunity.",
            "Matched first-team usage language"),
        Rule("usage-snap", ["snap share", "workload", "touch share", "target share"],
            IntelligenceCategory.Usage, IntelligenceImportance.High, 80,
            "Usage signal: volume/share language points to role change.",
            "Matched usage-share language"),
        Rule("opportunity-start", ["expected to start", "will start", "named the starter", "won the starting"],
            IntelligenceCategory.Opportunity, IntelligenceImportance.High, 85,
            "Opportunity increase: starting role language.",
            "Matched starting-opportunity language"),
        Rule("depth-chart", ["depth chart", "moved up the depth", "second string", "third string"],
            IntelligenceCategory.DepthChart, IntelligenceImportance.Medium, 76,
            "Depth chart movement detected.",
            "Matched depth-chart language"),
        Rule("practice-camp", ["training camp", "practice report", "walkthrough", "padded practice"],
            IntelligenceCategory.Practice, IntelligenceImportance.Low, 70,
            "Practice / camp context — monitor for role and health updates.",
            "Matched practice/camp language"),
        Rule("transaction-signed", [" signed ", "signing", "signs with", "agreed to terms"],
            IntelligenceCategory.Transaction, IntelligenceImportance.Medium, 83,
            "Transaction: signing activity may change roster opportunity.",
            "Matched signing language"),
        Rule("transaction-released", ["released", "waived", "cut from", "parted ways"],
            IntelligenceCategory.Transaction, IntelligenceImportance.High, 84,
            "Transaction: release/waive language reshapes depth.",
            "Matched release language"),
        Rule("transaction-trade", ["traded", "trade for", "acquired in a trade"],
            IntelligenceCategory.Transaction, IntelligenceImportance.High, 86,
            "Transaction: trade activity alters roster construction.",
            "Matched trade language"),
        Rule("suspension", ["suspended", "suspension"],
            IntelligenceCategory.Suspension, IntelligenceImportance.Critical, 90,
            "Suspension intelligence: availability and role are constrained.",
            "Matched suspension language"),
        Rule("contract", ["extension", "contract", "guaranteed money", "highest-paid"],
            IntelligenceCategory.Contract, IntelligenceImportance.Medium, 74,
            "Contract / commitment signal — may stabilize long-term role.",
            "Matched contract language"),
        Rule("coaching", ["head coach", "offensive coordinator", "play-caller", "scheme change"],
            IntelligenceCategory.Coaching, IntelligenceImportance.Medium, 72,
            "Coaching/scheme context that can shift usage patterns.",
            "Matched coaching language"),
        Rule("weather", ["weather", "windy", "heavy rain", "snow", "cold weather"],
            IntelligenceCategory.Weather, IntelligenceImportance.Medium, 75,
            "Game environment: weather may affect passing/rushing balance.",
            "Matched weather language"),
        Rule("game-environment", ["dome", "altitude", "short week", "primetime"],
            IntelligenceCategory.GameEnvironment, IntelligenceImportance.Low, 68,
            "Game environment factor worth tracking for script.",
            "Matched game-environment language"),
        Rule("team-chemistry", ["locker room", "leadership", "chemistry", "conflict"],
            IntelligenceCategory.TeamChemistry, IntelligenceImportance.Low, 65,
            "Team chemistry note — soft signal, low confidence alone.",
            "Matched chemistry language")
    ];

    public IReadOnlyList<IntelligenceFact> Analyze(
        IReadOnlyList<NewsArticle> articles,
        IReadOnlyList<Player> players)
    {
        var facts = new List<IntelligenceFact>();
        var orderedArticles = articles
            .OrderBy(a => a.Id)
            .ThenBy(a => a.Published)
            .ToList();

        foreach (var article in orderedArticles)
        {
            var text = Normalize($"{article.Title} {article.Summary}");
            var linkedPlayers = ResolvePlayers(article, players);
            var teamId = article.RelatedTeamIds.FirstOrDefault();
            var matchedAny = false;

            foreach (var rule in Rules)
            {
                var matchedPhrase = rule.Phrases.FirstOrDefault(p => text.Contains(Normalize(p)));
                if (matchedPhrase is null)
                {
                    continue;
                }

                matchedAny = true;

                if (linkedPlayers.Count > 0)
                {
                    foreach (var player in linkedPlayers.OrderBy(p => p.Id))
                    {
                        facts.Add(BuildFact(rule, article, matchedPhrase, player, teamId ?? player.Team));
                    }
                }
                else
                {
                    facts.Add(BuildFact(rule, article, matchedPhrase, player: null, teamId));
                }
            }

            if (!matchedAny)
            {
                // Deterministic general fact so every processed article is accounted for.
                var generalRule = Rule(
                    "general-news",
                    ["*"],
                    IntelligenceCategory.General,
                    MapPriority(article.Priority),
                    55,
                    "General football note — no high-signal keyword matched.",
                    "No specific heuristic matched");

                if (linkedPlayers.Count > 0)
                {
                    foreach (var player in linkedPlayers.OrderBy(p => p.Id))
                    {
                        facts.Add(BuildFact(generalRule, article, "unmatched", player, teamId ?? player.Team));
                    }
                }
                else
                {
                    facts.Add(BuildFact(generalRule, article, "unmatched", null, teamId));
                }
            }
        }

        return facts
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.Confidence)
            .ThenBy(f => f.Id)
            .ToList();
    }

    private static IntelligenceFact BuildFact(
        IntelligenceRule rule,
        NewsArticle article,
        string matchedPhrase,
        Player? player,
        string? teamId)
    {
        var subject = player?.FullName
                      ?? (!string.IsNullOrWhiteSpace(teamId) ? teamId : "League-wide");
        var title = $"{subject}: {rule.InsightTitle}";
        var description =
            $"{rule.InsightBody} Source headline: \"{article.Title}\".";

        var idSeed = $"{rule.Id}|{article.Id}|{player?.Id}|{teamId}";
        return new IntelligenceFact
        {
            Id = ToDeterministicGuid(idSeed),
            Title = title,
            Description = description,
            Category = rule.Category,
            Confidence = rule.Confidence,
            Importance = rule.Importance,
            Source = IntelligenceSource.News,
            Created = article.Published,
            Expires = article.Published.AddDays(14),
            RelatedPlayerId = player?.Id,
            RelatedTeamId = teamId,
            RelatedNewsArticleIds = [article.Id],
            SupportingEvidence =
            [
                $"Rule: {rule.Id}",
                $"Matched: {matchedPhrase}",
                $"Reason: {rule.Reason}",
                $"Article: {article.Title}",
                $"Source: {article.Source}"
            ],
            Tags = ["rule-based", rule.Id, rule.Category.ToString().ToLowerInvariant()]
        };
    }

    private static IReadOnlyList<Player> ResolvePlayers(NewsArticle article, IReadOnlyList<Player> players)
    {
        if (article.RelatedPlayerIds.Count > 0)
        {
            return players.Where(p => article.RelatedPlayerIds.Contains(p.Id)).ToList();
        }

        var names = article.RelatedPlayerNames;
        if (names.Count == 0)
        {
            // Fall back to scanning known player names inside the headline/summary.
            var text = $"{article.Title} {article.Summary}";
            return players
                .Where(p => text.Contains(p.FullName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Id)
                .Take(3)
                .ToList();
        }

        var matched = new List<Player>();
        foreach (var name in names)
        {
            var player = players.FirstOrDefault(p =>
                p.FullName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                (name.Contains(p.FirstName, StringComparison.OrdinalIgnoreCase) &&
                 name.Contains(p.LastName, StringComparison.OrdinalIgnoreCase)));
            if (player is not null && matched.All(m => m.Id != player.Id))
            {
                matched.Add(player);
            }
        }

        return matched;
    }

    private static IntelligenceImportance MapPriority(NewsPriority priority) => priority switch
    {
        NewsPriority.Critical => IntelligenceImportance.High,
        NewsPriority.High => IntelligenceImportance.Medium,
        _ => IntelligenceImportance.Low
    };

    private static string Normalize(string value) =>
        $" {value.ToLowerInvariant()} ";

    private static Guid ToDeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"playbook:intel:{value}"));
        return new Guid(bytes);
    }

    private static IntelligenceRule Rule(
        string id,
        string[] phrases,
        IntelligenceCategory category,
        IntelligenceImportance importance,
        int confidence,
        string insightBody,
        string reason) =>
        new(id, phrases, category, importance, confidence, ShortTitle(insightBody), insightBody, reason);

    private static string ShortTitle(string insightBody)
    {
        var cut = insightBody.Split(':')[0].Trim();
        return cut.Length <= 48 ? cut : cut[..48];
    }

    private sealed record IntelligenceRule(
        string Id,
        string[] Phrases,
        IntelligenceCategory Category,
        IntelligenceImportance Importance,
        int Confidence,
        string InsightTitle,
        string InsightBody,
        string Reason);
}
