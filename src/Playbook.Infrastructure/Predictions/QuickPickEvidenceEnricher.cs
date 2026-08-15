using Playbook.Core.Predictions;
using Playbook.Core.Research;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Adds shared research-memory evidence to a Quick Pick's supporting intelligence text — purely
/// informational. Only <see cref="Prediction.SupportingIntelligence"/> ever changes; Confidence,
/// Edge, Probability, and OpportunityScore are always copied through unchanged, so evidence can
/// never double-count into the pick's actual score. Reuses <see cref="PlayerEvidenceItem.Weight"/>
/// as-is (classification reliability × preseason discount × recency decay already computed by
/// <c>SharedEvidenceService</c>) rather than re-deriving trust here.
/// </summary>
public static class QuickPickEvidenceEnricher
{
    /// <summary>Below this weight, an evidence item is too weak/stale/preseason-noisy to surface on a pick.</summary>
    private const double MinWeightToSurface = 0.3;

    private const int MaxItemsConsidered = 5;

    public static Prediction Apply(Prediction prediction, PlayerEvidenceSummary evidence)
    {
        var relevant = evidence.Items
            .Where(i => i.Weight >= MinWeightToSurface)
            .OrderByDescending(i => i.Weight)
            .Take(MaxItemsConsidered)
            .ToList();

        if (relevant.Count == 0)
        {
            // No meaningful evidence — Quick Picks behaves exactly as it does today.
            return prediction;
        }

        var top = relevant[0];
        // Repeated/confirmed evidence of the same kind is more informative than one result —
        // note the corroboration instead of just repeating the top item.
        var corroborating = relevant.Count(i => i.Type == top.Type);
        var label = Label(top.Type, top.Phase);
        var line = corroborating > 1
            ? $"Research evidence — {label} (confirmed across {corroborating} recent results): {top.Summary}"
            : $"Research evidence — {label}: {top.Summary}";

        var supporting = prediction.SupportingIntelligence.ToList();
        supporting.Add(line);

        return new Prediction
        {
            Id = prediction.Id,
            Event = prediction.Event,
            PlayerId = prediction.PlayerId,
            PlayerName = prediction.PlayerName,
            TeamName = prediction.TeamName,
            Market = prediction.Market,
            Line = prediction.Line,
            PlaybookProjection = prediction.PlaybookProjection,
            Probability = prediction.Probability,
            Edge = prediction.Edge,
            Confidence = prediction.Confidence,
            Direction = prediction.Direction,
            Reasoning = prediction.Reasoning,
            SupportingIntelligence = supporting,
            SignalContributions = prediction.SignalContributions,
            CalculationNotes = prediction.CalculationNotes,
            Source = prediction.Source,
            LineFreshness = prediction.LineFreshness,
            LastUpdated = prediction.LastUpdated,
            LineUpdatedAt = prediction.LineUpdatedAt,
            Bookmaker = prediction.Bookmaker,
            EngineVersion = prediction.EngineVersion,
            OpportunityScore = prediction.OpportunityScore
        };
    }

    private static string Label(EvidenceType type, NflSeasonPhase phase) => type switch
    {
        EvidenceType.ProjectionAccuracy => "projection accuracy",
        EvidenceType.ProjectionError => "projection miss",
        EvidenceType.RoleConcern => "role concern",
        EvidenceType.MeaningfulRoleChange => "role change",
        EvidenceType.InjurySignal => "injury history",
        EvidenceType.ParticipationGap => "participation gap",
        EvidenceType.PhaseNoise => phase == NflSeasonPhase.Preseason ? "preseason noise" : "normal variance",
        _ => "research note"
    };
}
