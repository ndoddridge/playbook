using Playbook.Application.Abstractions;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Decisions;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;

namespace Playbook.Infrastructure.Decisions;

/// <summary>
/// Composes <see cref="PlayerKnowledge"/> from assessment + projection sources only.
/// Missing sources become explicit unknowns — never invented values.
/// </summary>
public sealed class PlayerKnowledgeComposer : IPlayerKnowledgeComposer
{
    private readonly IPlayerIntelligenceAssessmentService _assessments;
    private readonly IProjectionService _projections;

    public PlayerKnowledgeComposer(
        IPlayerIntelligenceAssessmentService assessments,
        IProjectionService projections)
    {
        _assessments = assessments;
        _projections = projections;
    }

    public Task<PlayerKnowledge> ComposeAsync(
        Player player,
        DecisionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Compose(player, context));
    }

    public async Task<IReadOnlyList<PlayerKnowledge>> ComposeManyAsync(
        IEnumerable<Player> players,
        DecisionContext context,
        CancellationToken cancellationToken = default)
    {
        var list = new List<PlayerKnowledge>();
        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(await ComposeAsync(player, context, cancellationToken).ConfigureAwait(false));
        }

        return list;
    }

    private PlayerKnowledge Compose(Player player, DecisionContext context)
    {
        var assessment = _assessments.GetAssessment(player.Id);
        var projection = assessment.Projection ?? _projections.GetProjection(player.Id);
        var now = DateTimeOffset.UtcNow;

        var facts = new List<KnowledgeFact>();
        var signals = new List<KnowledgeSignal>();
        var missing = new List<string>();

        AddProjectionEvidence(facts, signals, missing, projection, assessment, now);
        AddOpportunityEvidence(facts, signals, missing, assessment, now);
        AddUsageEvidence(facts, signals, missing, assessment, now);
        AddHealthEvidence(facts, signals, missing, assessment, now);
        AddRoleEvidence(facts, signals, missing, assessment, now);
        AddNewsEvidence(facts, signals, assessment, now);
        AddCoverageEvidence(facts, signals, missing, assessment, now);
        AddOutlookEvidence(facts, signals, assessment, now);
        AddRecentProductionEvidence(facts, signals, missing, assessment, now);

        var overall = DeriveOverallStatus(signals, missing, assessment);
        var knowledgeConfidence = DeriveKnowledgeConfidence(signals, missing, assessment);

        return new PlayerKnowledge
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            PositionLabel = player.Position.ToString(),
            Facts = facts,
            Signals = signals,
            OverallStatus = overall,
            KnowledgeConfidence = knowledgeConfidence,
            MissingEvidence = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            GeneratedAt = now,
            InformationCutoff = context.InformationCutoff,
            ProjectedPoints = projection?.ProjectedFantasyPoints,
            Floor = projection?.Floor,
            Ceiling = projection?.Ceiling,
            ProjectionConfidence = projection?.Confidence ?? assessment.ProjectionConfidence,
            OpportunityScore = assessment.OpportunityScore,
            UsageScore = assessment.UsageScore,
            HealthLabel = assessment.HealthStatusLabel
        };
    }

    private static void AddProjectionEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerProjection? projection,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        if (projection is null)
        {
            missing.Add("Projection");
            facts.Add(new KnowledgeFact
            {
                Key = "projection",
                Statement = "No projection is available for this player.",
                Source = "ProjectionService",
                ObservedAt = now,
                Status = EvidenceStatus.Unknown
            });
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Projection,
                Value = null,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Strong,
                Confidence = 20,
                Status = EvidenceStatus.Unknown,
                Source = "ProjectionService",
                Explanation = "Projection unavailable — expected value is unknown.",
                ObservedAt = now,
                Category = "Projection"
            });
            return;
        }

        facts.Add(new KnowledgeFact
        {
            Key = "projection.points",
            Statement =
                $"Projected {projection.ProjectedFantasyPoints:0.0} fantasy points (floor {projection.Floor:0.0}, ceiling {projection.Ceiling:0.0}).",
            Source = "ProjectionService",
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        var conf = projection.Confidence;
        var status = conf >= 55 ? EvidenceStatus.Known : EvidenceStatus.LowConfidence;
        var direction = projection.ProjectedFantasyPoints >= 12m
            ? SignalDirection.Positive
            : projection.ProjectedFantasyPoints <= 6m
                ? SignalDirection.Negative
                : SignalDirection.Neutral;

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.Projection,
            Value = (double)projection.ProjectedFantasyPoints,
            Direction = direction,
            Strength = projection.ProjectedFantasyPoints >= 16m || projection.ProjectedFantasyPoints <= 5m
                ? SignalStrength.Strong
                : SignalStrength.Moderate,
            Confidence = Math.Clamp(conf, 0, 100),
            Status = status,
            Source = "ProjectionService",
            Explanation =
                $"Projection {projection.ProjectedFantasyPoints:0.0} pts with model confidence {conf}%.",
            ObservedAt = now,
            Category = "Projection"
        });

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.Floor,
            Value = (double)projection.Floor,
            Direction = SignalDirection.Neutral,
            Strength = SignalStrength.Weak,
            Confidence = Math.Clamp(conf - 5, 0, 100),
            Status = status,
            Source = "ProjectionService",
            Explanation = $"Floor {projection.Floor:0.0} pts.",
            ObservedAt = now,
            Category = "Projection"
        });

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.Ceiling,
            Value = (double)projection.Ceiling,
            Direction = SignalDirection.Neutral,
            Strength = SignalStrength.Weak,
            Confidence = Math.Clamp(conf - 5, 0, 100),
            Status = status,
            Source = "ProjectionService",
            Explanation = $"Ceiling {projection.Ceiling:0.0} pts.",
            ObservedAt = now,
            Category = "Projection"
        });

        var spread = (double)(projection.Ceiling - projection.Floor);
        if (spread >= 12)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Volatility,
                Value = spread,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Moderate,
                Confidence = Math.Clamp(conf, 0, 100),
                Status = EvidenceStatus.Known,
                Source = "ProjectionService",
                Explanation = $"Wide floor/ceiling spread ({spread:0.0}) indicates elevated outcome volatility.",
                ObservedAt = now,
                Category = "Projection"
            });
        }

        _ = assessment;
    }

    private static void AddOpportunityEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        if (assessment.OpportunityScore is not int opp)
        {
            missing.Add("Opportunity");
            facts.Add(FactUnknown("opportunity", "Opportunity score is unavailable.", "Assessment", now));
            return;
        }

        facts.Add(new KnowledgeFact
        {
            Key = "opportunity.score",
            Statement = $"Opportunity score is {opp}/100.",
            Source = "PlayerIntelligenceAssessment",
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        signals.Add(ScoreSignal(
            SignalType.Opportunity,
            opp,
            "Opportunity",
            now,
            positiveThreshold: 60,
            negativeThreshold: 40));
    }

    private static void AddUsageEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        if (assessment.UsageScore is not int usage)
        {
            missing.Add("Usage");
            facts.Add(FactUnknown("usage", "Usage score is unavailable.", "Assessment", now));
            return;
        }

        facts.Add(new KnowledgeFact
        {
            Key = "usage.score",
            Statement = $"Usage score is {usage}/100.",
            Source = "PlayerIntelligenceAssessment",
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        signals.Add(ScoreSignal(
            SignalType.Usage,
            usage,
            "Usage",
            now,
            positiveThreshold: 60,
            negativeThreshold: 40));
    }

    private static void AddHealthEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        var label = assessment.HealthStatusLabel;
        facts.Add(new KnowledgeFact
        {
            Key = "health.label",
            Statement = $"Health status: {label}.",
            Source = "Injury/Assessment",
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        if (assessment.InjuryProfile?.CurrentInjury is { } injury)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "health.injury.current",
                Statement = string.IsNullOrWhiteSpace(injury.BodyPart)
                    ? $"Currently listed as {injury.Status}."
                    : $"Currently listed as {injury.Status} ({injury.BodyPart}).",
                Source = "InjuryProfile",
                ObservedAt = now,
                Status = EvidenceStatus.Known
            });

            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Value = assessment.HealthScore,
                Direction = SignalDirection.Negative,
                Strength = SignalStrength.Strong,
                Confidence = 85,
                Status = EvidenceStatus.Known,
                Source = "InjuryProfile",
                Explanation = "Active injury designation is present.",
                ObservedAt = now,
                Category = "Health"
            });
            return;
        }

        if (assessment.InjuryProfile is { UnconfirmedSignals.Count: > 0 })
        {
            facts.Add(new KnowledgeFact
            {
                Key = "health.injury.unconfirmed",
                Statement = "Unconfirmed injury-related reports exist.",
                Source = "InjuryProfile",
                ObservedAt = now,
                Status = EvidenceStatus.LowConfidence
            });

            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Value = assessment.HealthScore,
                Direction = SignalDirection.Negative,
                Strength = SignalStrength.Moderate,
                Confidence = 55,
                Status = EvidenceStatus.LowConfidence,
                Source = "InjuryProfile",
                Explanation = "Unconfirmed injury reports reduce health certainty.",
                ObservedAt = now,
                Category = "Health"
            });
            return;
        }

        if (label.Contains("Limited information", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            missing.Add("Health certainty");
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Value = assessment.HealthScore,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Moderate,
                Confidence = 35,
                Status = EvidenceStatus.Unknown,
                Source = "Assessment",
                Explanation = "Health information is limited or unknown.",
                ObservedAt = now,
                Category = "Health"
            });
            return;
        }

        if (label.Contains("Healthy", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Value = assessment.HealthScore,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 75,
                Status = EvidenceStatus.Known,
                Source = "Assessment",
                Explanation = "No active injury designation; listed as healthy.",
                ObservedAt = now,
                Category = "Health"
            });
        }
    }

    private static void AddRoleEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        var roleFactor = assessment.KeyFactors
            .Concat(assessment.PositiveFactors)
            .Concat(assessment.NegativeFactors)
            .FirstOrDefault(f =>
                f.Text.Contains("role", StringComparison.OrdinalIgnoreCase) ||
                f.Source.Contains("role", StringComparison.OrdinalIgnoreCase) ||
                f.Text.Contains("snap", StringComparison.OrdinalIgnoreCase));

        if (roleFactor is null)
        {
            if (assessment.OpportunityScore is null && assessment.UsageScore is null)
            {
                missing.Add("Role");
            }

            return;
        }

        facts.Add(new KnowledgeFact
        {
            Key = "role.signal",
            Statement = roleFactor.Text,
            Source = roleFactor.Source,
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.Role,
            Value = null,
            Direction = roleFactor.IsPositive ? SignalDirection.Positive : SignalDirection.Negative,
            Strength = SignalStrength.Moderate,
            Confidence = Math.Clamp(assessment.AssessmentConfidence, 30, 90),
            Status = assessment.AssessmentConfidence >= 50 ? EvidenceStatus.Known : EvidenceStatus.LowConfidence,
            Source = roleFactor.Source,
            Explanation = roleFactor.Text,
            ObservedAt = now,
            Category = "Role"
        });
    }

    private static void AddNewsEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        var news = assessment.RecentIntelligence.FirstOrDefault();
        if (news is null)
        {
            return;
        }

        // Respect information cutoff when present (historical replay readiness).
        facts.Add(new KnowledgeFact
        {
            Key = "news.latest",
            Statement = news.Title,
            Source = news.Source,
            ObservedAt = news.Timestamp,
            Status = news.IsConfirmed ? EvidenceStatus.Known : EvidenceStatus.LowConfidence
        });

        var negative = news.Category.Contains("Injury", StringComparison.OrdinalIgnoreCase) ||
                       news.Title.Contains("out", StringComparison.OrdinalIgnoreCase) ||
                       news.Title.Contains("doubtful", StringComparison.OrdinalIgnoreCase);

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.News,
            Value = null,
            Direction = negative
                ? SignalDirection.Negative
                : news.IsConfirmed
                    ? SignalDirection.Neutral
                    : SignalDirection.Uncertainty,
            Strength = news.IsConfirmed ? SignalStrength.Moderate : SignalStrength.Weak,
            Confidence = news.IsConfirmed ? 70 : 45,
            Status = news.IsConfirmed ? EvidenceStatus.Known : EvidenceStatus.LowConfidence,
            Source = news.Source,
            Explanation = news.Title,
            ObservedAt = news.Timestamp,
            Category = news.Category
        });
    }

    private static void AddCoverageEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        foreach (var unavailable in assessment.UnavailableSignals.Take(6))
        {
            missing.Add(unavailable);
        }

        if (assessment.UnavailableSignals.Count == 0 && assessment.AssessmentConfidence >= 50)
        {
            return;
        }

        var limited = assessment.AssessmentConfidence < 40 || assessment.UnavailableSignals.Count >= 3;
        facts.Add(new KnowledgeFact
        {
            Key = "coverage.intelligence",
            Statement = limited
                ? $"Intelligence coverage is limited (assessment confidence {assessment.AssessmentConfidence}%, {assessment.UnavailableSignals.Count} unavailable signal(s))."
                : $"Assessment confidence is {assessment.AssessmentConfidence}%.",
            Source = "PlayerIntelligenceAssessment",
            ObservedAt = now,
            Status = limited ? EvidenceStatus.LowConfidence : EvidenceStatus.Known
        });

        if (limited)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Coverage,
                Value = assessment.AssessmentConfidence,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Strong,
                Confidence = Math.Clamp(100 - assessment.AssessmentConfidence, 40, 95),
                Status = EvidenceStatus.LowConfidence,
                Source = "PlayerIntelligenceAssessment",
                Explanation = "Limited intelligence coverage strongly reduces decision confidence.",
                ObservedAt = now,
                Category = "Coverage"
            });
        }
    }

    private static void AddOutlookEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        facts.Add(new KnowledgeFact
        {
            Key = "outlook.label",
            Statement = $"Assessment outlook label: {assessment.OutlookLabel}.",
            Source = "PlayerIntelligenceAssessment",
            ObservedAt = now,
            Status = assessment.Outlook == PlayerOutlook.Unknown
                ? EvidenceStatus.Unknown
                : EvidenceStatus.Known
        });

        var (direction, strength) = assessment.Outlook switch
        {
            PlayerOutlook.Strong => (SignalDirection.Positive, SignalStrength.Strong),
            PlayerOutlook.Positive => (SignalDirection.Positive, SignalStrength.Moderate),
            PlayerOutlook.Concerning => (SignalDirection.Negative, SignalStrength.Strong),
            PlayerOutlook.Unknown => (SignalDirection.Uncertainty, SignalStrength.Moderate),
            _ => (SignalDirection.Neutral, SignalStrength.Weak)
        };

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.Outlook,
            Value = null,
            Direction = direction,
            Strength = strength,
            Confidence = Math.Clamp(assessment.AssessmentConfidence, 20, 95),
            Status = assessment.Outlook == PlayerOutlook.Unknown
                ? EvidenceStatus.Unknown
                : assessment.AssessmentConfidence < 45
                    ? EvidenceStatus.LowConfidence
                    : EvidenceStatus.Known,
            Source = "PlayerIntelligenceAssessment",
            Explanation = assessment.Headline,
            ObservedAt = now,
            Category = "Outlook"
        });
    }

    private static void AddRecentProductionEvidence(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        PlayerIntelligenceAssessment assessment,
        DateTimeOffset now)
    {
        var trend = assessment.Profile?.TrendDirection;
        if (trend is null)
        {
            missing.Add("Recent production trend");
            return;
        }

        facts.Add(new KnowledgeFact
        {
            Key = "production.trend",
            Statement = $"Recent production trend: {trend}.",
            Source = "PlayerIntelligenceProfile",
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        var direction = trend.Value switch
        {
            TrendDirection.Up => SignalDirection.Positive,
            TrendDirection.Down => SignalDirection.Negative,
            _ => SignalDirection.Neutral
        };

        signals.Add(new KnowledgeSignal
        {
            Type = SignalType.RecentProduction,
            Value = null,
            Direction = direction,
            Strength = SignalStrength.Moderate,
            Confidence = Math.Clamp(assessment.AssessmentConfidence, 30, 85),
            Status = assessment.AssessmentConfidence >= 50 ? EvidenceStatus.Known : EvidenceStatus.LowConfidence,
            Source = "PlayerIntelligenceProfile",
            Explanation = $"Recent production trend is {trend}.",
            ObservedAt = now,
            Category = "Production"
        });
    }

    private static KnowledgeSignal ScoreSignal(
        SignalType type,
        int score,
        string label,
        DateTimeOffset now,
        int positiveThreshold,
        int negativeThreshold)
    {
        SignalDirection direction;
        SignalStrength strength;
        if (score >= positiveThreshold)
        {
            direction = SignalDirection.Positive;
            strength = score >= 75 ? SignalStrength.Strong : SignalStrength.Moderate;
        }
        else if (score <= negativeThreshold)
        {
            direction = SignalDirection.Negative;
            strength = score <= 25 ? SignalStrength.Strong : SignalStrength.Moderate;
        }
        else
        {
            direction = SignalDirection.Neutral;
            strength = SignalStrength.Weak;
        }

        return new KnowledgeSignal
        {
            Type = type,
            Value = score,
            Direction = direction,
            Strength = strength,
            Confidence = 70,
            Status = EvidenceStatus.Known,
            Source = "PlayerIntelligenceAssessment",
            Explanation = $"{label} score {score}/100.",
            ObservedAt = now,
            Category = label
        };
    }

    private static KnowledgeFact FactUnknown(string key, string statement, string source, DateTimeOffset now) =>
        new()
        {
            Key = key,
            Statement = statement,
            Source = source,
            ObservedAt = now,
            Status = EvidenceStatus.Unknown
        };

    private static EvidenceStatus DeriveOverallStatus(
        IReadOnlyList<KnowledgeSignal> signals,
        IReadOnlyList<string> missing,
        PlayerIntelligenceAssessment assessment)
    {
        var directional = signals
            .Where(s => s.Direction is SignalDirection.Positive or SignalDirection.Negative)
            .Where(s => s.Status is EvidenceStatus.Known or EvidenceStatus.LowConfidence)
            .ToList();

        var hasPos = directional.Any(s => s.Direction == SignalDirection.Positive && s.Strength != SignalStrength.Weak);
        var hasNeg = directional.Any(s => s.Direction == SignalDirection.Negative && s.Strength != SignalStrength.Weak);
        if (hasPos && hasNeg)
        {
            return EvidenceStatus.Conflicting;
        }

        if (assessment.AssessmentConfidence < 40 || missing.Count >= 3 || signals.Count(s => s.Status == EvidenceStatus.Unknown) >= 2)
        {
            return EvidenceStatus.LowConfidence;
        }

        if (signals.All(s => s.Status is EvidenceStatus.Unknown) || assessment.Outlook == PlayerOutlook.Unknown)
        {
            return EvidenceStatus.Unknown;
        }

        return EvidenceStatus.Known;
    }

    /// <summary>
    /// Knowledge confidence (0–100) = evidence completeness × agreement, not calibrated probability.
    /// </summary>
    private static int DeriveKnowledgeConfidence(
        IReadOnlyList<KnowledgeSignal> signals,
        IReadOnlyList<string> missing,
        PlayerIntelligenceAssessment assessment)
    {
        var baseConf = assessment.AssessmentConfidence;
        var penalty = Math.Min(35, missing.Count * 6);
        var unknownPenalty = signals.Count(s => s.Status == EvidenceStatus.Unknown) * 4;
        var coveragePenalty = signals.Any(s => s.Type == SignalType.Coverage) ? 12 : 0;
        var conflictPenalty = signals.Any(s => s.Direction == SignalDirection.Positive) &&
                              signals.Any(s => s.Direction == SignalDirection.Negative)
            ? 8
            : 0;

        return Math.Clamp(baseConf - penalty - unknownPenalty - coveragePenalty - conflictPenalty, 10, 95);
    }
}
