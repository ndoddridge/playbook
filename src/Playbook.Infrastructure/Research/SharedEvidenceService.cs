using Playbook.Application.Research;
using Playbook.Core.Predictions;
using Playbook.Core.Research;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Turns permanent research memory (immutable pre-event snapshot + post-event assessment pairs)
/// into structured, attributable <see cref="PlayerEvidenceItem"/>s. Pure read-side transformation
/// over the existing single <see cref="IPredictionResearchStore"/> — nothing new is persisted, and
/// nothing here can influence Projection V2, Confidence V2, or the Decision Policy directly.
/// </summary>
public sealed class SharedEvidenceService : ISharedEvidenceService
{
    /// <summary>Cap so a single player's evidence list can't grow without bound over a season.</summary>
    private const int MaxItemsPerPlayer = 12;

    /// <summary>Half-life, in days, for recency decay — evidence fades but never disappears.</summary>
    private const double RecencyHalfLifeDays = 21.0;

    private const double MinWeight = 0.05;

    /// <summary>
    /// Preseason findings are real signal, but roles/usage shift meaningfully once the regular
    /// season starts, so a preseason-phase item carries less evidentiary weight than the same
    /// classification would in-season.
    /// </summary>
    private const double PreseasonPhaseFactor = 0.5;

    private readonly IPredictionResearchStore _store;

    public SharedEvidenceService(IPredictionResearchStore store)
    {
        _store = store;
    }

    public PlayerEvidenceSummary GetEvidenceForPlayer(Guid playerId)
    {
        var snapshotsById = _store.GetAllSnapshots()
            .Where(s => s.PlayerId == playerId)
            .ToDictionary(s => s.SnapshotId);

        if (snapshotsById.Count == 0)
        {
            return Empty(playerId);
        }

        var now = DateTimeOffset.UtcNow;
        var items = _store.GetAllAssessments()
            .Where(a => snapshotsById.ContainsKey(a.SnapshotId))
            .Select(a => BuildItem(snapshotsById[a.SnapshotId], a, now))
            .OrderByDescending(i => i.ObservedAt)
            .Take(MaxItemsPerPlayer)
            .ToList();

        if (items.Count == 0)
        {
            return Empty(playerId);
        }

        var headline = items.OrderByDescending(i => i.Weight).First().Summary;
        return new PlayerEvidenceSummary
        {
            PlayerId = playerId,
            Items = items,
            Headline = headline
        };
    }

    private static PlayerEvidenceItem BuildItem(
        PredictionSnapshot snapshot, PredictionOutcomeAssessment assessment, DateTimeOffset now)
    {
        var type = ToEvidenceType(assessment.Classification);
        var weight = ComputeWeight(assessment.Classification, snapshot.SeasonPhase, assessment.GradedAt, now);

        return new PlayerEvidenceItem
        {
            SnapshotId = snapshot.SnapshotId,
            PlayerId = snapshot.PlayerId!.Value,
            PlayerName = snapshot.PlayerName,
            Type = type,
            Phase = snapshot.SeasonPhase,
            Season = snapshot.Season,
            Week = snapshot.Week,
            Market = snapshot.Market,
            Summary = BuildSummary(snapshot, assessment),
            Weight = weight,
            ObservedAt = assessment.GradedAt,
            Source = "Quick Picks research memory"
        };
    }

    private static EvidenceType ToEvidenceType(PredictionOutcomeClassification classification) =>
        classification switch
        {
            PredictionOutcomeClassification.Success => EvidenceType.ProjectionAccuracy,
            PredictionOutcomeClassification.ProjectionError => EvidenceType.ProjectionError,
            PredictionOutcomeClassification.RoleError => EvidenceType.RoleConcern,
            PredictionOutcomeClassification.MeaningfulRoleSignal => EvidenceType.MeaningfulRoleChange,
            PredictionOutcomeClassification.InjurySignal => EvidenceType.InjurySignal,
            PredictionOutcomeClassification.DataGap => EvidenceType.ParticipationGap,
            PredictionOutcomeClassification.PreseasonNoise => EvidenceType.PhaseNoise,
            PredictionOutcomeClassification.RegularSeasonNoise => EvidenceType.PhaseNoise,
            _ => EvidenceType.ParticipationGap
        };

    /// <summary>
    /// Base reliability by classification (how much this kind of finding should count at all),
    /// discounted for preseason phase, then decayed by recency. Every factor is bounded so no
    /// single item — however dramatic — can ever reach full/unconditional weight, and a
    /// DataGap/PhaseNoise item never outweighs a confirmed pattern.
    /// </summary>
    private static double ComputeWeight(
        PredictionOutcomeClassification classification,
        NflSeasonPhase phase,
        DateTimeOffset gradedAt,
        DateTimeOffset now)
    {
        var baseReliability = classification switch
        {
            PredictionOutcomeClassification.Success => 0.6,
            PredictionOutcomeClassification.ProjectionError => 0.55,
            PredictionOutcomeClassification.RoleError => 0.55,
            PredictionOutcomeClassification.MeaningfulRoleSignal => 0.6,
            PredictionOutcomeClassification.InjurySignal => 0.5,
            PredictionOutcomeClassification.PreseasonNoise => 0.2,
            PredictionOutcomeClassification.RegularSeasonNoise => 0.3,
            PredictionOutcomeClassification.DataGap => 0.1,
            _ => 0.2
        };

        var phaseFactor = phase == NflSeasonPhase.Preseason ? PreseasonPhaseFactor : 1.0;

        var daysOld = Math.Max(0, (now - gradedAt).TotalDays);
        var recencyDecay = Math.Pow(0.5, daysOld / RecencyHalfLifeDays);

        return Math.Clamp(baseReliability * phaseFactor * recencyDecay, MinWeight, 1.0);
    }

    private static string BuildSummary(PredictionSnapshot snapshot, PredictionOutcomeAssessment assessment)
    {
        var phaseLabel = snapshot.SeasonPhase switch
        {
            NflSeasonPhase.Preseason => "Preseason",
            NflSeasonPhase.Postseason => "Postseason",
            _ => "Regular season"
        };

        var actual = assessment.ActualValue is { } av ? av.ToString("0.#") : "unknown";
        var projection = snapshot.PlaybookProjection is { } pp ? pp.ToString("0.#") : "n/a";

        return $"{phaseLabel} Wk {snapshot.Week}: {snapshot.Market} actual {actual} vs projection " +
               $"{projection} — {assessment.Classification}.";
    }

    private static PlayerEvidenceSummary Empty(Guid playerId) => new()
    {
        PlayerId = playerId,
        Items = [],
        Headline = null
    };
}
