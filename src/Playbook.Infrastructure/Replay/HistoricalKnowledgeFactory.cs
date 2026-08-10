using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Composes <see cref="PlayerKnowledge"/> exclusively from a cutoff-safe historical snapshot.
/// Never consults live assessment, news, injury, or projection services.
/// </summary>
public sealed class HistoricalKnowledgeFactory : IHistoricalKnowledgeFactory
{
    public IReadOnlyList<PlayerKnowledge> BuildKnowledge(HistoricalSnapshot snapshot, DecisionContext context)
    {
        return snapshot.Players
            .Select(p => Compose(p, snapshot, context))
            .ToList();
    }

    private static PlayerKnowledge Compose(
        HistoricalPlayerState player,
        HistoricalSnapshot snapshot,
        DecisionContext context)
    {
        var now = snapshot.InformationCutoff;
        var facts = new List<KnowledgeFact>();
        var signals = new List<KnowledgeSignal>();
        var missing = player.UnavailableSignals.ToList();

        if (player.ProjectedPoints is decimal pts)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "projection.points",
                Statement =
                    $"Projected {pts:0.0} fantasy points (floor {player.Floor:0.0}, ceiling {player.Ceiling:0.0}).",
                Source = "HistoricalSnapshot",
                ObservedAt = now,
                Status = EvidenceStatus.Known
            });

            var conf = player.ProjectionConfidence ?? 50;
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Projection,
                Value = (double)pts,
                Direction = pts >= 12m ? SignalDirection.Positive : pts <= 6m ? SignalDirection.Negative : SignalDirection.Neutral,
                Strength = pts >= 16m || pts <= 5m ? SignalStrength.Strong : SignalStrength.Moderate,
                Confidence = Math.Clamp(conf, 0, 100),
                Status = conf >= 55 ? EvidenceStatus.Known : EvidenceStatus.LowConfidence,
                Source = "HistoricalSnapshot",
                Explanation = $"Projection {pts:0.0} pts with confidence {conf}%.",
                ObservedAt = now,
                Category = "Projection"
            });

            if (player.Floor is decimal floor)
            {
                signals.Add(new KnowledgeSignal
                {
                    Type = SignalType.Floor,
                    Value = (double)floor,
                    Direction = SignalDirection.Neutral,
                    Strength = SignalStrength.Weak,
                    Confidence = Math.Clamp(conf - 5, 0, 100),
                    Status = EvidenceStatus.Known,
                    Source = "HistoricalSnapshot",
                    Explanation = $"Floor {floor:0.0} pts.",
                    ObservedAt = now,
                    Category = "Projection"
                });
            }

            if (player.Ceiling is decimal ceiling)
            {
                signals.Add(new KnowledgeSignal
                {
                    Type = SignalType.Ceiling,
                    Value = (double)ceiling,
                    Direction = SignalDirection.Neutral,
                    Strength = SignalStrength.Weak,
                    Confidence = Math.Clamp(conf - 5, 0, 100),
                    Status = EvidenceStatus.Known,
                    Source = "HistoricalSnapshot",
                    Explanation = $"Ceiling {ceiling:0.0} pts.",
                    ObservedAt = now,
                    Category = "Projection"
                });
            }
        }
        else
        {
            missing.Add("Projection");
            facts.Add(new KnowledgeFact
            {
                Key = "projection",
                Statement = "No projection available at information cutoff.",
                Source = "HistoricalSnapshot",
                ObservedAt = now,
                Status = EvidenceStatus.Unknown
            });
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Projection,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Strong,
                Confidence = 20,
                Status = EvidenceStatus.Unknown,
                Source = "HistoricalSnapshot",
                Explanation = "Projection unavailable at cutoff.",
                ObservedAt = now,
                Category = "Projection"
            });
        }

        AddScoreSignal(facts, signals, missing, SignalType.Opportunity, player.OpportunityScore, "Opportunity", now);
        AddScoreSignal(facts, signals, missing, SignalType.Usage, player.UsageScore, "Usage", now);

        if (!string.IsNullOrWhiteSpace(player.HealthLabel))
        {
            facts.Add(new KnowledgeFact
            {
                Key = "health.label",
                Statement = $"Health status: {player.HealthLabel}.",
                Source = "HistoricalSnapshot",
                ObservedAt = now,
                Status = EvidenceStatus.Known
            });
        }

        if (player.InjuryStatus is not null)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "health.injury.current",
                Statement = string.IsNullOrWhiteSpace(player.InjuryBodyPart)
                    ? $"Listed as {player.InjuryStatus} at cutoff."
                    : $"Listed as {player.InjuryStatus} ({player.InjuryBodyPart}) at cutoff.",
                Source = "HistoricalSnapshot",
                ObservedAt = player.InjuryObservedAt ?? now,
                Status = EvidenceStatus.Known
            });

            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Direction = SignalDirection.Negative,
                Strength = player.InjuryStatus.Contains("Out", StringComparison.OrdinalIgnoreCase)
                    ? SignalStrength.Strong
                    : SignalStrength.Moderate,
                Confidence = 80,
                Status = EvidenceStatus.Known,
                Source = "HistoricalSnapshot",
                Explanation = $"Injury designation known at cutoff: {player.InjuryStatus}.",
                ObservedAt = player.InjuryObservedAt ?? now,
                Category = "Health"
            });
        }
        else if (player.HealthLabel is not null &&
                 player.HealthLabel.Contains("Healthy", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Health,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 70,
                Status = EvidenceStatus.Known,
                Source = "HistoricalSnapshot",
                Explanation = "No injury designation known at cutoff; listed as healthy.",
                ObservedAt = now,
                Category = "Health"
            });
        }

        if (player.RecentNewsHeadline is not null)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "news.latest",
                Statement = player.RecentNewsHeadline,
                Source = "HistoricalSnapshot",
                ObservedAt = player.RecentNewsObservedAt ?? now,
                Status = player.RecentNewsConfirmed ? EvidenceStatus.Known : EvidenceStatus.LowConfidence
            });

            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.News,
                Direction = SignalDirection.Neutral,
                Strength = player.RecentNewsConfirmed ? SignalStrength.Moderate : SignalStrength.Weak,
                Confidence = player.RecentNewsConfirmed ? 70 : 45,
                Status = player.RecentNewsConfirmed ? EvidenceStatus.Known : EvidenceStatus.LowConfidence,
                Source = "HistoricalSnapshot",
                Explanation = player.RecentNewsHeadline,
                ObservedAt = player.RecentNewsObservedAt ?? now,
                Category = "News"
            });
        }

        if (player.RoleNote is not null)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "role.signal",
                Statement = player.RoleNote,
                Source = "HistoricalSnapshot",
                ObservedAt = now,
                Status = EvidenceStatus.Known
            });
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Role,
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Confidence = 65,
                Status = EvidenceStatus.Known,
                Source = "HistoricalSnapshot",
                Explanation = player.RoleNote,
                ObservedAt = now,
                Category = "Role"
            });
        }

        if (player.RecentProductionScore is int prod)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "production.recent",
                Statement = $"Recent production score {prod}/100 known at cutoff.",
                Source = "HistoricalSnapshot",
                ObservedAt = now,
                Status = EvidenceStatus.Known
            });
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.RecentProduction,
                Value = prod,
                Direction = prod >= 60 ? SignalDirection.Positive : prod <= 40 ? SignalDirection.Negative : SignalDirection.Neutral,
                Strength = SignalStrength.Moderate,
                Confidence = 60,
                Status = EvidenceStatus.Known,
                Source = "HistoricalSnapshot",
                Explanation = $"Recent production score {prod}/100.",
                ObservedAt = now,
                Category = "Production"
            });
        }
        else
        {
            missing.Add("Recent production");
        }

        if (missing.Count >= 2)
        {
            signals.Add(new KnowledgeSignal
            {
                Type = SignalType.Coverage,
                Value = missing.Count,
                Direction = SignalDirection.Uncertainty,
                Strength = SignalStrength.Moderate,
                Confidence = 70,
                Status = EvidenceStatus.LowConfidence,
                Source = "HistoricalSnapshot",
                Explanation = "Limited historical intelligence coverage at cutoff.",
                ObservedAt = now,
                Category = "Coverage"
            });
        }

        var distinctMissing = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var knowledgeConfidence = DeriveKnowledgeConfidence(signals, distinctMissing, player.ProjectionConfidence);
        var overall = DeriveOverallStatus(signals, distinctMissing);

        return new PlayerKnowledge
        {
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            PositionLabel = player.Position.ToString(),
            Facts = facts,
            Signals = signals,
            OverallStatus = overall,
            KnowledgeConfidence = knowledgeConfidence,
            MissingEvidence = distinctMissing,
            GeneratedAt = now,
            InformationCutoff = context.InformationCutoff ?? snapshot.InformationCutoff,
            ProjectedPoints = player.ProjectedPoints,
            Floor = player.Floor,
            Ceiling = player.Ceiling,
            ProjectionConfidence = player.ProjectionConfidence,
            OpportunityScore = player.OpportunityScore,
            UsageScore = player.UsageScore,
            HealthLabel = player.HealthLabel
        };
    }

    private static void AddScoreSignal(
        List<KnowledgeFact> facts,
        List<KnowledgeSignal> signals,
        List<string> missing,
        SignalType type,
        int? score,
        string label,
        DateTimeOffset now)
    {
        if (score is not int value)
        {
            missing.Add(label);
            return;
        }

        facts.Add(new KnowledgeFact
        {
            Key = $"{label.ToLowerInvariant()}.score",
            Statement = $"{label} score is {value}/100.",
            Source = "HistoricalSnapshot",
            ObservedAt = now,
            Status = EvidenceStatus.Known
        });

        SignalDirection direction;
        SignalStrength strength;
        if (value >= 60)
        {
            direction = SignalDirection.Positive;
            strength = value >= 75 ? SignalStrength.Strong : SignalStrength.Moderate;
        }
        else if (value <= 40)
        {
            direction = SignalDirection.Negative;
            strength = value <= 25 ? SignalStrength.Strong : SignalStrength.Moderate;
        }
        else
        {
            direction = SignalDirection.Neutral;
            strength = SignalStrength.Weak;
        }

        signals.Add(new KnowledgeSignal
        {
            Type = type,
            Value = value,
            Direction = direction,
            Strength = strength,
            Confidence = 70,
            Status = EvidenceStatus.Known,
            Source = "HistoricalSnapshot",
            Explanation = $"{label} score {value}/100.",
            ObservedAt = now,
            Category = label
        });
    }

    private static int DeriveKnowledgeConfidence(
        IReadOnlyList<KnowledgeSignal> signals,
        IReadOnlyList<string> missing,
        int? projectionConfidence)
    {
        var baseConf = projectionConfidence ?? 45;
        var penalty = Math.Min(30, missing.Count * 5);
        var unknownPenalty = signals.Count(s => s.Status == EvidenceStatus.Unknown) * 4;
        return Math.Clamp(baseConf - penalty - unknownPenalty, 12, 95);
    }

    private static EvidenceStatus DeriveOverallStatus(
        IReadOnlyList<KnowledgeSignal> signals,
        IReadOnlyList<string> missing)
    {
        var hasPos = signals.Any(s => s.Direction == SignalDirection.Positive && s.Strength != SignalStrength.Weak);
        var hasNeg = signals.Any(s => s.Direction == SignalDirection.Negative && s.Strength != SignalStrength.Weak);
        if (hasPos && hasNeg)
        {
            return EvidenceStatus.Conflicting;
        }

        if (missing.Count >= 3 || signals.Count(s => s.Status == EvidenceStatus.Unknown) >= 2)
        {
            return EvidenceStatus.LowConfidence;
        }

        return EvidenceStatus.Known;
    }
}
