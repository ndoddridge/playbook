using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.News;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Intelligence.Services;

/// <summary>
/// Builds a user-facing assessment from existing Playbook services only.
/// Missing data is labeled unavailable — never treated as a positive signal.
/// </summary>
public sealed class PlayerIntelligenceAssessmentService : IPlayerIntelligenceAssessmentService
{
    private readonly IIntelligenceService _intelligence;
    private readonly IPlayerInjuryService _injuries;
    private readonly IProjectionService _projections;
    private readonly IPlayerStatisticalContextService _stats;
    private readonly INewsProvider _news;

    public PlayerIntelligenceAssessmentService(
        IIntelligenceService intelligence,
        IPlayerInjuryService injuries,
        IProjectionService projections,
        IPlayerStatisticalContextService stats,
        INewsProvider news)
    {
        _intelligence = intelligence;
        _injuries = injuries;
        _projections = projections;
        _stats = stats;
        _news = news;
    }

    public PlayerIntelligenceAssessment GetAssessment(Guid playerId)
    {
        var profile = _intelligence.GetPlayerProfile(playerId);
        var injury = _injuries.GetPlayerInjuryProfile(playerId);
        var projection = _projections.GetProjection(playerId);
        var stats = _stats.GetContext(playerId);
        var facts = _intelligence.GetFactsForPlayer(playerId);
        var news = _news.GetForPlayer(playerId, 8);

        var unavailable = new List<string>();
        if (profile is null)
        {
            unavailable.Add("Aggregated intelligence profile unavailable");
        }

        if (injury is null)
        {
            unavailable.Add("Injury profile unavailable");
        }
        else if (injury.CurrentDataStatus == CurrentInjuryDataStatus.Unavailable)
        {
            unavailable.Add("Current injury data unavailable");
        }

        if (projection is null)
        {
            unavailable.Add("Projection unavailable");
        }
        else if (projection.InputsUsed.UnavailableInputs.Count > 0)
        {
            unavailable.AddRange(projection.InputsUsed.UnavailableInputs.Select(i => $"{i} unavailable"));
        }

        if (stats is null)
        {
            unavailable.Add("Statistical context unavailable");
        }

        var positive = BuildPositiveFactors(profile, injury, projection, stats, facts);
        var negative = BuildNegativeFactors(profile, injury, projection, stats, facts);
        var recent = BuildRecentIntelligence(facts, news, injury);
        var (outlook, outlookLabel) = DeriveOutlook(profile, injury, positive.Count, negative.Count);
        var confidence = DeriveConfidence(profile, unavailable.Count, facts.Count);
        var healthLabel = HealthLabel(injury, profile);
        var projectionSummary = projection is null
            ? null
            : $"{projection.ProjectedFantasyPoints:0.0} pts (floor {projection.Floor:0.0} · ceiling {projection.Ceiling:0.0})";

        return new PlayerIntelligenceAssessment
        {
            PlayerId = playerId,
            Outlook = outlook,
            OutlookLabel = outlookLabel,
            Headline = profile?.Headline
                       ?? injury?.RiskSummary
                       ?? (projection is null ? "Limited intelligence available" : "Projection available"),
            AssessmentConfidence = confidence,
            ConfidenceNote = confidence switch
            {
                >= 70 => "Based on multiple supporting signals",
                >= 45 => "Partial signal coverage — treat with caution",
                _ => "Limited or incomplete intelligence"
            },
            OpportunityScore = profile?.OpportunityScore,
            UsageScore = profile?.UsageScore,
            HealthScore = profile?.HealthScore,
            HealthStatusLabel = healthLabel,
            ProjectionSummary = projectionSummary,
            PositiveFactors = positive,
            NegativeFactors = negative,
            RecentIntelligence = recent,
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            DetailSections = BuildDetailSections(profile, injury, projection, stats, unavailable),
            Profile = profile,
            InjuryProfile = injury,
            Projection = projection,
            StatisticalContext = stats,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<IntelligenceFactor> BuildPositiveFactors(
        PlayerIntelligenceProfile? profile,
        PlayerInjuryProfile? injury,
        PlayerProjection? projection,
        PlayerStatisticalContext? stats,
        IReadOnlyList<IntelligenceFact> facts)
    {
        var factors = new List<IntelligenceFactor>();

        if (profile is not null)
        {
            if (profile.OpportunityScore >= 60)
            {
                factors.Add(Factor("Increased opportunity", $"Opportunity score {profile.OpportunityScore}/100", "Intelligence"));
            }

            if (profile.UsageScore >= 60)
            {
                factors.Add(Factor("Strong usage", $"Usage score {profile.UsageScore}/100", "Intelligence"));
            }

            if (profile.TrendDirection == Core.Players.TrendDirection.Up)
            {
                factors.Add(Factor("Positive trend", profile.Headline, "Intelligence"));
            }

            if (profile.HealthScore >= 65 &&
                injury?.CurrentDataStatus == CurrentInjuryDataStatus.NoCurrentInjury)
            {
                factors.Add(Factor("Healthy", $"Health score {profile.HealthScore}/100", "Intelligence"));
            }

            if (profile.ChangeSignal is IntelligenceChangeSignal.OpportunityIncreasing
                or IntelligenceChangeSignal.UsageIncreasing
                or IntelligenceChangeSignal.HealthImproving)
            {
                factors.Add(Factor(profile.ChangeSignal.ToString().Replace("Increasing", " increasing").Replace("Improving", " improving"),
                    profile.Headline, "Change signal"));
            }
        }

        if (stats?.Usage?.WorkloadTrend is string workload &&
            workload.Contains("up", StringComparison.OrdinalIgnoreCase))
        {
            factors.Add(Factor("Workload trending up", workload, "Statistics"));
        }

        if (stats?.Trend == StatisticalTrendSignal.Increasing)
        {
            factors.Add(Factor("Recent production improving", stats.RecentProduction?.Label, "Statistics"));
        }

        if (projection is not null && projection.Confidence >= 65 && projection.Volatility <= 45)
        {
            factors.Add(Factor(
                "Stable projection",
                $"Confidence {projection.Confidence}% · volatility {projection.Volatility}",
                "Projection"));
        }

        foreach (var fact in facts
                     .Where(f => !IsUnconfirmed(f))
                     .Where(f => f.Category is IntelligenceCategory.Opportunity
                         or IntelligenceCategory.Usage
                         or IntelligenceCategory.Efficiency
                         or IntelligenceCategory.DepthChart)
                     .Where(f => f.Importance >= IntelligenceImportance.Medium)
                     .OrderByDescending(f => f.Importance)
                     .ThenByDescending(f => f.Confidence)
                     .Take(3))
        {
            if (factors.All(x => !string.Equals(x.Text, fact.Title, StringComparison.OrdinalIgnoreCase)))
            {
                factors.Add(Factor(fact.Title, fact.Description, fact.Source.ToString()));
            }
        }

        return factors.Take(6).ToList();
    }

    private static IReadOnlyList<IntelligenceFactor> BuildNegativeFactors(
        PlayerIntelligenceProfile? profile,
        PlayerInjuryProfile? injury,
        PlayerProjection? projection,
        PlayerStatisticalContext? stats,
        IReadOnlyList<IntelligenceFact> facts)
    {
        var factors = new List<IntelligenceFactor>();

        if (injury?.CurrentInjury is { } current)
        {
            factors.Add(Factor(
                "Current injury",
                $"{current.Status}" +
                (string.IsNullOrWhiteSpace(current.BodyPart) ? "" : $" — {current.BodyPart}"),
                current.Source ?? "Injury report"));
        }

        if (injury is { UnconfirmedSignals.Count: > 0 })
        {
            var signal = injury.UnconfirmedSignals[0];
            factors.Add(Factor(
                "Unconfirmed injury report",
                signal.Headline,
                signal.Source));
        }

        if (profile is not null)
        {
            if (profile.HealthScore <= 40)
            {
                factors.Add(Factor("Health concern", $"Health score {profile.HealthScore}/100", "Intelligence"));
            }

            if (profile.OpportunityScore <= 40)
            {
                factors.Add(Factor("Reduced opportunity", $"Opportunity score {profile.OpportunityScore}/100", "Intelligence"));
            }

            if (profile.UsageScore <= 40)
            {
                factors.Add(Factor("Reduced usage", $"Usage score {profile.UsageScore}/100", "Intelligence"));
            }

            if (profile.OverallRisk >= 65)
            {
                factors.Add(Factor("Elevated risk", $"Risk {profile.OverallRisk}/100", "Intelligence"));
            }

            if (profile.ChangeSignal is IntelligenceChangeSignal.HealthConcern
                or IntelligenceChangeSignal.OpportunityDecreasing
                or IntelligenceChangeSignal.ElevatedRisk)
            {
                factors.Add(Factor(ReadableSignal(profile.ChangeSignal), profile.Headline, "Change signal"));
            }

            if (profile.TrendDirection == Core.Players.TrendDirection.Down)
            {
                factors.Add(Factor("Negative trend", profile.Headline, "Intelligence"));
            }
        }

        foreach (var entry in injury?.RecentHistory.Where(e => e.Band is InjuryRelevanceBand.High or InjuryRelevanceBand.Moderate).Take(2)
                     ?? [])
        {
            factors.Add(Factor(
                "Relevant injury history",
                $"{entry.Record.BodyPart ?? entry.Record.Status} — {entry.RelevanceReason}",
                entry.Record.Source ?? "Injury history"));
        }

        if (stats?.Trend == StatisticalTrendSignal.Decreasing)
        {
            factors.Add(Factor("Recent production declining", stats.RecentProduction?.Label, "Statistics"));
        }

        if (projection is not null && projection.Volatility >= 70)
        {
            factors.Add(Factor("High projection volatility", $"Volatility {projection.Volatility}", "Projection"));
        }

        foreach (var fact in facts
                     .Where(f => f.Category is IntelligenceCategory.Injury
                         or IntelligenceCategory.Suspension
                         or IntelligenceCategory.Situation)
                     .Where(f => f.Importance >= IntelligenceImportance.Medium)
                     .OrderByDescending(f => f.Importance)
                     .Take(2))
        {
            if (factors.All(x => !string.Equals(x.Text, fact.Title, StringComparison.OrdinalIgnoreCase)))
            {
                factors.Add(Factor(
                    fact.Title + (IsUnconfirmed(fact) ? " (unconfirmed)" : ""),
                    fact.Description,
                    fact.Source.ToString()));
            }
        }

        return factors.Take(6).ToList();
    }

    private static IReadOnlyList<RecentIntelligenceItem> BuildRecentIntelligence(
        IReadOnlyList<IntelligenceFact> facts,
        IReadOnlyList<NewsArticle> news,
        PlayerInjuryProfile? injury)
    {
        var items = new List<RecentIntelligenceItem>();

        foreach (var signal in injury?.UnconfirmedSignals ?? [])
        {
            items.Add(new RecentIntelligenceItem
            {
                Title = signal.Headline,
                Summary = signal.Detail ?? "Unconfirmed injury-related report.",
                IsConfirmed = false,
                VerificationLabel = "Unconfirmed",
                Category = "Injury",
                Source = signal.Source,
                Timestamp = signal.LastUpdated
            });
        }

        foreach (var fact in facts.OrderByDescending(f => f.Created).Take(8))
        {
            var unconfirmed = IsUnconfirmed(fact);
            items.Add(new RecentIntelligenceItem
            {
                Title = fact.Title,
                Summary = fact.Description,
                IsConfirmed = !unconfirmed,
                VerificationLabel = unconfirmed ? "Unconfirmed" : "Confirmed",
                Category = fact.Category.ToString(),
                Source = fact.Source.ToString(),
                Timestamp = fact.Created
            });
        }

        foreach (var article in news.Take(4))
        {
            if (items.Any(i => string.Equals(i.Title, article.Title, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            items.Add(new RecentIntelligenceItem
            {
                Title = article.Title,
                Summary = article.Summary,
                IsConfirmed = false,
                VerificationLabel = "Reported",
                Category = article.Category.ToString(),
                Source = article.Source,
                Timestamp = article.Published,
                Url = article.Url
            });
        }

        return items
            .OrderByDescending(i => i.Timestamp)
            .Take(8)
            .ToList();
    }

    private static IReadOnlyList<AssessmentDetailSection> BuildDetailSections(
        PlayerIntelligenceProfile? profile,
        PlayerInjuryProfile? injury,
        PlayerProjection? projection,
        PlayerStatisticalContext? stats,
        IReadOnlyList<string> unavailable)
    {
        var sections = new List<AssessmentDetailSection>();

        if (profile is not null)
        {
            sections.Add(new AssessmentDetailSection(
                "Intelligence scores",
                [
                    $"Headline: {profile.Headline}",
                    $"Confidence: {profile.OverallConfidence}%",
                    $"Opportunity: {profile.OpportunityScore}",
                    $"Usage: {profile.UsageScore}",
                    $"Health: {profile.HealthScore}",
                    $"Risk: {profile.OverallRisk}",
                    $"Updated: {profile.LastUpdated.ToLocalTime():MMM d · h:mm tt}"
                ]));
        }

        if (projection is not null)
        {
            sections.Add(new AssessmentDetailSection(
                "Projection",
                [
                    $"Median: {projection.Median:0.0}",
                    $"Floor: {projection.Floor:0.0}",
                    $"Ceiling: {projection.Ceiling:0.0}",
                    $"Confidence: {projection.Confidence}%",
                    $"Volatility: {projection.Volatility}",
                    $"Engine: {projection.ProjectionVersion}",
                    .. projection.ProjectionReasoning.Take(4)
                ]));
        }

        if (injury is not null)
        {
            var injuryItems = new List<string>
            {
                $"Current status: {injury.CurrentStatus ?? InjuryAvailabilityPresentation.CurrentStatusLabel(injury.CurrentDataStatus)}"
            };
            if (injury.CurrentInjury is { } cur)
            {
                injuryItems.Add($"Current injury: {cur.Status} {cur.BodyPart}".Trim());
                injuryItems.Add($"Verification: {cur.VerificationLabel}");
            }

            if (injury.UnconfirmedSignals.Count > 0)
            {
                injuryItems.Add($"Unconfirmed reports: {injury.UnconfirmedSignals.Count}");
            }

            injuryItems.Add($"Recent relevant history: {injury.RecentHistory.Count}");
            if (!string.IsNullOrWhiteSpace(injury.RiskSummary))
            {
                injuryItems.Add(injury.RiskSummary);
            }

            sections.Add(new AssessmentDetailSection("Health & injury", injuryItems));
        }

        if (stats is not null)
        {
            var usage = stats.Usage;
            sections.Add(new AssessmentDetailSection(
                "Usage & production",
                [
                    $"Trend: {stats.Trend}",
                    $"Game logs: {stats.GameLogsAvailable}",
                    $"NFL seasons: {stats.NflSeasonsAvailable}",
                    usage?.TargetsPerGame is decimal t ? $"Targets/g: {t:0.0}" : "Targets/g: unavailable",
                    usage?.CarriesPerGame is decimal c ? $"Carries/g: {c:0.0}" : "Carries/g: unavailable",
                    stats.PrimarySourceProvider is null ? "Source: unavailable" : $"Source: {stats.PrimarySourceProvider}"
                ]));
        }

        if (unavailable.Count > 0)
        {
            sections.Add(new AssessmentDetailSection("Unavailable signals", unavailable.ToList()));
        }

        return sections;
    }

    private static (PlayerOutlook Outlook, string Label) DeriveOutlook(
        PlayerIntelligenceProfile? profile,
        PlayerInjuryProfile? injury,
        int positiveCount,
        int negativeCount)
    {
        if (profile is null && injury is null)
        {
            return (PlayerOutlook.Unknown, "Unknown");
        }

        if (injury?.CurrentInjury is not null ||
            profile?.ChangeSignal is IntelligenceChangeSignal.HealthConcern
                or IntelligenceChangeSignal.ElevatedRisk ||
            (profile?.OverallRisk ?? 0) >= 70 ||
            (profile?.HealthScore is int hs && hs <= 35))
        {
            return (PlayerOutlook.Concerning, "Concerning");
        }

        if (profile is not null &&
            profile.OpportunityScore >= 65 &&
            profile.HealthScore >= 60 &&
            profile.OverallConfidence >= 55 &&
            negativeCount <= 1)
        {
            return (PlayerOutlook.Strong, "Strong");
        }

        if (positiveCount > negativeCount && (profile?.OpportunityScore ?? 50) >= 55)
        {
            return (PlayerOutlook.Positive, "Positive");
        }

        if (negativeCount > positiveCount)
        {
            return (PlayerOutlook.Concerning, "Concerning");
        }

        return (PlayerOutlook.Neutral, "Neutral");
    }

    private static int DeriveConfidence(
        PlayerIntelligenceProfile? profile,
        int unavailableCount,
        int factCount)
    {
        var baseConfidence = profile?.OverallConfidence ?? 25;
        var penalty = Math.Min(35, unavailableCount * 8);
        if (factCount == 0)
        {
            penalty += 10;
        }

        return Math.Clamp(baseConfidence - penalty, 5, 95);
    }

    private static string HealthLabel(PlayerInjuryProfile? injury, PlayerIntelligenceProfile? profile)
    {
        if (injury is null)
        {
            return "Unavailable";
        }

        if (injury.CurrentInjury is { } current)
        {
            return string.IsNullOrWhiteSpace(current.BodyPart)
                ? current.Status.ToString()
                : $"{current.Status} · {current.BodyPart}";
        }

        if (injury.CurrentDataStatus == CurrentInjuryDataStatus.Unavailable)
        {
            return "Unavailable";
        }

        if (injury.UnconfirmedSignals.Count > 0)
        {
            return "No confirmed injury · unconfirmed reports";
        }

        if (injury.CurrentDataStatus == CurrentInjuryDataStatus.NoCurrentInjury)
        {
            return profile?.HealthScore is int score ? $"Healthy · {score}" : "No confirmed injury";
        }

        return InjuryAvailabilityPresentation.CurrentStatusLabel(injury.CurrentDataStatus);
    }

    private static bool IsUnconfirmed(IntelligenceFact fact) =>
        fact.Tags.Any(t => t.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase)) ||
        fact.SupportingEvidence.Any(e => e.Contains("Unconfirmed", StringComparison.OrdinalIgnoreCase));

    private static string ReadableSignal(IntelligenceChangeSignal signal) => signal switch
    {
        IntelligenceChangeSignal.HealthConcern => "Health concern",
        IntelligenceChangeSignal.OpportunityDecreasing => "Opportunity decreasing",
        IntelligenceChangeSignal.ElevatedRisk => "Elevated risk",
        IntelligenceChangeSignal.OpportunityIncreasing => "Opportunity increasing",
        IntelligenceChangeSignal.UsageIncreasing => "Usage increasing",
        IntelligenceChangeSignal.HealthImproving => "Health improving",
        _ => signal.ToString()
    };

    private static IntelligenceFactor Factor(string text, string? detail, string source) =>
        new()
        {
            Text = text,
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
            Source = source
        };
}
