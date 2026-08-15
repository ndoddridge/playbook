using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Research;
using Playbook.Application.Stats.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.News;
using Playbook.Core.Projections.Models;
using Playbook.Core.Research;
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
    private readonly ISharedEvidenceService _evidence;

    public PlayerIntelligenceAssessmentService(
        IIntelligenceService intelligence,
        IPlayerInjuryService injuries,
        IProjectionService projections,
        IPlayerStatisticalContextService stats,
        INewsProvider news,
        ISharedEvidenceService evidence)
    {
        _intelligence = intelligence;
        _injuries = injuries;
        _projections = projections;
        _stats = stats;
        _news = news;
        _evidence = evidence;
    }

    public PlayerIntelligenceAssessment GetAssessment(Guid playerId)
    {
        var profile = _intelligence.GetPlayerProfile(playerId);
        var injury = _injuries.GetPlayerInjuryProfile(playerId);
        var projection = _projections.GetProjection(playerId);
        var stats = _stats.GetContext(playerId);
        var facts = _intelligence.GetFactsForPlayer(playerId);
        var news = _news.GetForPlayer(playerId, 8);
        var evidence = _evidence.GetEvidenceForPlayer(playerId);

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

        var positive = DeduplicateFactors(BuildPositiveFactors(profile, injury, projection, stats, facts));
        var negative = DeduplicateFactors(BuildNegativeFactors(profile, injury, projection, stats, facts));
        var recent = DeduplicateRecent(BuildRecentIntelligence(facts, news, injury));
        var (outlook, outlookLabel) = DeriveOutlook(profile, injury, positive, negative);
        var headline = BuildHeadline(outlook, injury, profile, negative, positive);
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
            Headline = headline,
            AssessmentConfidence = confidence,
            ConfidenceNote = confidence switch
            {
                >= 70 => "Intelligence confidence reflects multiple supporting signals",
                >= 45 => "Intelligence confidence is limited by partial signal coverage",
                _ => "Intelligence confidence is low — incomplete underlying data"
            },
            OpportunityScore = profile?.OpportunityScore,
            UsageScore = profile?.UsageScore,
            HealthScore = profile?.HealthScore,
            HealthStatusLabel = healthLabel,
            ProjectionSummary = projectionSummary,
            ProjectionConfidence = projection?.Confidence,
            PositiveFactors = positive,
            NegativeFactors = negative,
            KeyFactors = SelectKeyFactors(outlook, positive, negative),
            RecentIntelligence = recent,
            UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            DetailSections = BuildDetailSections(profile, injury, projection, stats, unavailable, confidence, evidence),
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
                factors.Add(Factor("Increased opportunity", $"Opportunity score {profile.OpportunityScore}/100", "Intelligence", true));
            }

            if (profile.UsageScore >= 60)
            {
                factors.Add(Factor("Strong usage", $"Usage score {profile.UsageScore}/100", "Intelligence", true));
            }

            if (profile.TrendDirection == Core.Players.TrendDirection.Up)
            {
                factors.Add(Factor("Positive trend", DescribeProfileSignal(profile), "Intelligence", true));
            }

            // Only claim healthy when injury data confirms no current injury AND health score supports it.
            if (profile.HealthScore >= 65 &&
                injury?.CurrentDataStatus == CurrentInjuryDataStatus.NoCurrentInjury &&
                injury.UnconfirmedSignals.Count == 0)
            {
                factors.Add(Factor(
                    "No current designation",
                    $"Health score {profile.HealthScore}/100 with no confirmed injury",
                    "Intelligence",
                    true));
            }

            if (profile.ChangeSignal is IntelligenceChangeSignal.OpportunityIncreasing
                or IntelligenceChangeSignal.UsageIncreasing
                or IntelligenceChangeSignal.HealthImproving)
            {
                factors.Add(Factor(ReadableSignal(profile.ChangeSignal), DescribeProfileSignal(profile), "Change signal", true));
            }
        }

        if (stats?.Usage?.WorkloadTrend is string workload &&
            workload.Contains("up", StringComparison.OrdinalIgnoreCase))
        {
            factors.Add(Factor("Workload trending up", workload, "Statistics", true));
        }

        if (stats?.Trend == StatisticalTrendSignal.Increasing)
        {
            factors.Add(Factor("Strong recent production", stats.RecentProduction?.Label, "Statistics", true));
        }

        if (projection is not null && projection.Confidence >= 65 && projection.Volatility <= 45)
        {
            factors.Add(Factor(
                "Low-volatility projection",
                $"Projection confidence {projection.Confidence}% · volatility {projection.Volatility}",
                "Projection",
                true));
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
            factors.Add(Factor(fact.Title, fact.Description, fact.Source.ToString(), true));
        }

        return factors;
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
                current.Source ?? "Injury report",
                false));
        }

        if (injury is { UnconfirmedSignals.Count: > 0 })
        {
            var signal = injury.UnconfirmedSignals[0];
            factors.Add(Factor(
                "Unconfirmed injury report",
                signal.Headline,
                signal.Source,
                false));
        }

        if (profile is not null)
        {
            if (profile.HealthScore <= 40)
            {
                factors.Add(Factor("Health concern", $"Health score {profile.HealthScore}/100", "Intelligence", false));
            }

            if (profile.OpportunityScore <= 40)
            {
                factors.Add(Factor("Reduced opportunity", $"Opportunity score {profile.OpportunityScore}/100", "Intelligence", false));
            }

            if (profile.UsageScore <= 40)
            {
                factors.Add(Factor("Reduced usage", $"Usage score {profile.UsageScore}/100", "Intelligence", false));
            }

            if (profile.OverallRisk >= 65)
            {
                factors.Add(Factor("Elevated risk", $"Risk {profile.OverallRisk}/100", "Intelligence", false));
            }

            if (profile.ChangeSignal is IntelligenceChangeSignal.HealthConcern
                or IntelligenceChangeSignal.OpportunityDecreasing
                or IntelligenceChangeSignal.ElevatedRisk)
            {
                factors.Add(Factor(ReadableSignal(profile.ChangeSignal), DescribeProfileSignal(profile), "Change signal", false));
            }

            if (profile.TrendDirection == Core.Players.TrendDirection.Down)
            {
                factors.Add(Factor("Negative trend", DescribeProfileSignal(profile), "Intelligence", false));
            }
        }

        foreach (var summarized in SummarizeInjuryHistory(injury))
        {
            factors.Add(summarized);
        }

        if (stats?.Trend == StatisticalTrendSignal.Decreasing)
        {
            factors.Add(Factor("Recent production declining", stats.RecentProduction?.Label, "Statistics", false));
        }

        if (projection is not null && projection.Volatility >= 70)
        {
            factors.Add(Factor("High projection volatility", $"Volatility {projection.Volatility}", "Projection", false));
        }

        foreach (var fact in facts
                     .Where(f => f.Category is IntelligenceCategory.Injury
                         or IntelligenceCategory.Suspension
                         or IntelligenceCategory.Situation)
                     .Where(f => f.Importance >= IntelligenceImportance.Medium)
                     .OrderByDescending(f => f.Importance)
                     .Take(2))
        {
            factors.Add(Factor(
                fact.Title + (IsUnconfirmed(fact) ? " (unconfirmed)" : ""),
                fact.Description,
                fact.Source.ToString(),
                false));
        }

        return factors;
    }

    private static IEnumerable<IntelligenceFactor> SummarizeInjuryHistory(PlayerInjuryProfile? injury)
    {
        if (injury is null)
        {
            yield break;
        }

        var relevant = injury.RecentHistory
            .Where(e => e.Band is InjuryRelevanceBand.High or InjuryRelevanceBand.Moderate)
            .ToList();

        foreach (var group in relevant
                     .GroupBy(e => NormalizeInjuryKey(e.Record.BodyPart ?? e.Record.Status))
                     .OrderByDescending(g => g.Max(x => x.RelevanceScore))
                     .Take(3))
        {
            var sample = group.OrderByDescending(e => e.RelevanceScore).First();
            var body = sample.Record.BodyPart ?? sample.Record.Status;
            if (group.Count() > 1)
            {
                yield return Factor(
                    $"Repeated {body} history — {group.Count()} recorded occurrences",
                    sample.RelevanceReason,
                    sample.Record.Source ?? "Injury history",
                    false);
            }
            else
            {
                yield return Factor(
                    $"Relevant injury history — {body}",
                    sample.RelevanceReason,
                    sample.Record.Source ?? "Injury history",
                    false);
            }
        }
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
        IReadOnlyList<string> unavailable,
        int intelligenceConfidence,
        PlayerEvidenceSummary evidence)
    {
        var sections = new List<AssessmentDetailSection>();

        if (profile is not null)
        {
            sections.Add(new AssessmentDetailSection(
                "Intelligence confidence & scores",
                [
                    $"Intelligence confidence: {intelligenceConfidence}%",
                    $"Profile confidence: {profile.OverallConfidence}%",
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
                "Projection reasoning",
                [
                    $"Projection confidence: {projection.Confidence}%",
                    $"Median: {projection.Median:0.0}",
                    $"Floor: {projection.Floor:0.0}",
                    $"Ceiling: {projection.Ceiling:0.0}",
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

            sections.Add(new AssessmentDetailSection("Health & injury reasoning", injuryItems));
        }

        if (stats is not null)
        {
            var usage = stats.Usage;
            sections.Add(new AssessmentDetailSection(
                "Usage & opportunity reasoning",
                [
                    $"Trend: {stats.Trend}",
                    $"Game logs: {stats.GameLogsAvailable}",
                    $"NFL seasons: {stats.NflSeasonsAvailable}",
                    usage?.TargetsPerGame is decimal t ? $"Targets/g: {t:0.0}" : "Targets/g: unavailable",
                    usage?.CarriesPerGame is decimal c ? $"Carries/g: {c:0.0}" : "Carries/g: unavailable",
                    stats.PrimarySourceProvider is null ? "Source: unavailable" : $"Source: {stats.PrimarySourceProvider}"
                ]));
        }

        if (evidence.HasEvidence)
        {
            sections.Add(new AssessmentDetailSection(
                "Recent research evidence",
                evidence.Items
                    .OrderByDescending(i => i.Weight)
                    .Take(5)
                    .Select(i => $"{i.Summary} (evidentiary weight {i.Weight:0.00})")
                    .ToList()));
        }

        if (unavailable.Count > 0)
        {
            sections.Add(new AssessmentDetailSection("Data quality / unavailable", unavailable.ToList()));
        }

        return sections;
    }

    private static (PlayerOutlook Outlook, string Label) DeriveOutlook(
        PlayerIntelligenceProfile? profile,
        PlayerInjuryProfile? injury,
        IReadOnlyList<IntelligenceFactor> positive,
        IReadOnlyList<IntelligenceFactor> negative)
    {
        if (profile is null && injury is null)
        {
            return (PlayerOutlook.Unknown, "Unknown");
        }

        if (HasActiveConcern(profile, injury))
        {
            return (PlayerOutlook.Concerning, "Concerning");
        }

        if (profile is not null &&
            profile.OpportunityScore >= 65 &&
            profile.HealthScore >= 60 &&
            profile.OverallConfidence >= 55 &&
            injury?.CurrentDataStatus == CurrentInjuryDataStatus.NoCurrentInjury &&
            injury.UnconfirmedSignals.Count == 0 &&
            negative.Count <= 1)
        {
            return (PlayerOutlook.Strong, "Strong");
        }

        if (positive.Count > negative.Count && (profile?.OpportunityScore ?? 50) >= 55)
        {
            return (PlayerOutlook.Positive, "Positive");
        }

        // Historical or mild negatives without an active concern stay Stable — not Concerning.
        return (PlayerOutlook.Neutral, "Stable");
    }

    private static bool HasActiveConcern(PlayerIntelligenceProfile? profile, PlayerInjuryProfile? injury) =>
        injury?.CurrentInjury is not null ||
        injury is { UnconfirmedSignals.Count: > 0 } ||
        profile?.ChangeSignal is IntelligenceChangeSignal.HealthConcern
            or IntelligenceChangeSignal.ElevatedRisk ||
        (profile?.OverallRisk ?? 0) >= 70 ||
        (profile?.HealthScore is int hs && hs <= 35);

    private static string BuildHeadline(
        PlayerOutlook outlook,
        PlayerInjuryProfile? injury,
        PlayerIntelligenceProfile? profile,
        IReadOnlyList<IntelligenceFactor> negative,
        IReadOnlyList<IntelligenceFactor> positive)
    {
        return outlook switch
        {
            PlayerOutlook.Concerning when injury?.CurrentInjury is { } current =>
                string.IsNullOrWhiteSpace(current.BodyPart)
                    ? $"Active injury designation: {current.Status}."
                    : $"Active injury designation: {current.Status} ({current.BodyPart}).",
            PlayerOutlook.Concerning when injury is { UnconfirmedSignals.Count: > 0 } =>
                $"Unconfirmed reports are weighing on the outlook — {injury.UnconfirmedSignals[0].Headline}.",
            PlayerOutlook.Concerning when negative.Count > 0 =>
                negative[0].Detail is { Length: > 0 } detail
                    ? $"{negative[0].Text}: {detail}"
                    : negative[0].Text,
            PlayerOutlook.Concerning =>
                "Material concerns in current health or risk signals.",
            PlayerOutlook.Strong =>
                "Supportive opportunity, usage, and health signals.",
            PlayerOutlook.Positive when positive.Count > 0 =>
                positive[0].Detail is { Length: > 0 } detail
                    ? $"{positive[0].Text}: {detail}"
                    : positive[0].Text,
            PlayerOutlook.Positive =>
                "Favorable signals outweigh current concerns.",
            PlayerOutlook.Unknown =>
                "Limited intelligence available for a firm outlook.",
            _ =>
                "No strong directional change in current signals."
        };
    }

    private static IReadOnlyList<IntelligenceFactor> SelectKeyFactors(
        PlayerOutlook outlook,
        IReadOnlyList<IntelligenceFactor> positive,
        IReadOnlyList<IntelligenceFactor> negative)
    {
        var selected = new List<IntelligenceFactor>();

        // Prefer the side that drives the outlook, then balance.
        if (outlook is PlayerOutlook.Concerning)
        {
            selected.AddRange(negative.Take(3));
            selected.AddRange(positive.Take(Math.Max(0, 4 - selected.Count)));
        }
        else if (outlook is PlayerOutlook.Strong or PlayerOutlook.Positive)
        {
            selected.AddRange(positive.Take(3));
            selected.AddRange(negative.Take(Math.Max(0, 4 - selected.Count)));
        }
        else
        {
            selected.AddRange(negative.Take(2));
            selected.AddRange(positive.Take(Math.Max(0, 4 - selected.Count)));
        }

        return DeduplicateFactors(selected).Take(4).ToList();
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
            return "Limited information";
        }

        if (injury.CurrentInjury is { } current)
        {
            return string.IsNullOrWhiteSpace(current.BodyPart)
                ? $"Current concern · {current.Status}"
                : $"Current concern · {current.Status} · {current.BodyPart}";
        }

        if (injury.CurrentDataStatus == CurrentInjuryDataStatus.Unavailable)
        {
            return "Limited information";
        }

        if (injury.UnconfirmedSignals.Count > 0)
        {
            return "No current designation · unconfirmed reports";
        }

        if (injury.CurrentDataStatus == CurrentInjuryDataStatus.NoCurrentInjury)
        {
            if (profile?.HealthScore is int score && score <= 40)
            {
                return "Current concern";
            }

            if (profile?.HealthScore is int healthy && healthy >= 65)
            {
                return "Healthy / No current designation";
            }

            // Confirmed no current injury, but incomplete health scoring — do not claim "Healthy".
            return "No current designation";
        }

        return "Unknown";
    }

    private static IReadOnlyList<IntelligenceFactor> DeduplicateFactors(IEnumerable<IntelligenceFactor> factors)
    {
        var result = new List<IntelligenceFactor>();
        foreach (var factor in factors)
        {
            var key = NormalizeFactorKey(factor.Text);
            if (result.Any(existing =>
                    NormalizeFactorKey(existing.Text) == key ||
                    AreEquivalentInjuryFactors(existing, factor)))
            {
                continue;
            }

            result.Add(factor);
            if (result.Count >= 6)
            {
                break;
            }
        }

        return result;
    }

    private static IReadOnlyList<RecentIntelligenceItem> DeduplicateRecent(IEnumerable<RecentIntelligenceItem> items)
    {
        var result = new List<RecentIntelligenceItem>();
        foreach (var item in items)
        {
            var key = NormalizeFactorKey(item.Title);
            if (result.Any(existing => NormalizeFactorKey(existing.Title) == key))
            {
                continue;
            }

            result.Add(item);
            if (result.Count >= 8)
            {
                break;
            }
        }

        return result;
    }

    private static bool AreEquivalentInjuryFactors(IntelligenceFactor a, IntelligenceFactor b)
    {
        var aInjury = a.Text.Contains("injury", StringComparison.OrdinalIgnoreCase) ||
                      a.Text.Contains("history", StringComparison.OrdinalIgnoreCase);
        var bInjury = b.Text.Contains("injury", StringComparison.OrdinalIgnoreCase) ||
                      b.Text.Contains("history", StringComparison.OrdinalIgnoreCase);
        if (!aInjury || !bInjury)
        {
            return false;
        }

        var aBody = ExtractBodyPartHint(a.Text) ?? ExtractBodyPartHint(a.Detail);
        var bBody = ExtractBodyPartHint(b.Text) ?? ExtractBodyPartHint(b.Detail);
        return aBody is not null &&
               bBody is not null &&
               string.Equals(aBody, bBody, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractBodyPartHint(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var token in new[] { "Foot", "Ankle", "Knee", "Hamstring", "Shoulder", "Back", "Hip", "Wrist", "Hand", "Quad", "Groin", "Calf", "Concussion", "Rib" })
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return token.ToLowerInvariant();
            }
        }

        return null;
    }

    private static string NormalizeFactorKey(string text) =>
        string.Join(' ', text
            .ToLowerInvariant()
            .Replace("(unconfirmed)", "", StringComparison.Ordinal)
            .Split([' ', '—', '-', '·', ',', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t is not ("the" or "a" or "an" or "of" or "and")));

    private static string NormalizeInjuryKey(string value) =>
        value.Trim().ToLowerInvariant();

    private static bool IsUnconfirmed(IntelligenceFact fact) =>
        fact.Tags.Any(t => t.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase)) ||
        fact.SupportingEvidence.Any(e => e.Contains("Unconfirmed", StringComparison.OrdinalIgnoreCase));

    private static string DescribeProfileSignal(PlayerIntelligenceProfile profile)
    {
        // Avoid reusing aggregator labels like "Stable Outlook" that compete with the canonical outlook.
        if (profile.ChangeSignal is IntelligenceChangeSignal.Neutral)
        {
            return $"Opportunity {profile.OpportunityScore} · usage {profile.UsageScore} · health {profile.HealthScore}";
        }

        return ReadableSignal(profile.ChangeSignal);
    }

    private static string ReadableSignal(IntelligenceChangeSignal signal) => signal switch
    {
        IntelligenceChangeSignal.HealthConcern => "Health concern",
        IntelligenceChangeSignal.OpportunityDecreasing => "Opportunity decreasing",
        IntelligenceChangeSignal.ElevatedRisk => "Elevated risk",
        IntelligenceChangeSignal.OpportunityIncreasing => "Opportunity increasing",
        IntelligenceChangeSignal.UsageIncreasing => "Usage increasing",
        IntelligenceChangeSignal.HealthImproving => "Health improving",
        _ => "No directional change signal"
    };

    private static IntelligenceFactor Factor(string text, string? detail, string source, bool isPositive) =>
        new()
        {
            Text = text,
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
            Source = source,
            IsPositive = isPositive
        };
}
