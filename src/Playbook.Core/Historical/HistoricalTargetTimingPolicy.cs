namespace Playbook.Core.Historical;

/// <summary>Converts observed historical ranges into deliberately non-probabilistic target timing.</summary>
public static class HistoricalTargetTimingPolicy
{
    public static HistoricalTargetTimingAssessment Assess(
        HistoricalAvailabilityResult availability, bool elevatedOwnerRisk = false, bool positionRun = false)
    {
        var range = availability.LeagueRange;
        if (range is null || range.DraftCount < 3 || availability.EvidenceStrength is HistoricalEvidenceStrength.Unavailable or HistoricalEvidenceStrength.Insufficient)
            return new(HistoricalTargetTiming.Unknown, HistoricalTargetRecommendation.Unknown, availability, false, positionRun, "Historical evidence is insufficient; no live-score adjustment is applied.");

        if (availability.CurrentPick > range.MaximumPick)
            return new(HistoricalTargetTiming.TooLateLikelyGone, HistoricalTargetRecommendation.TakeNow, availability, elevatedOwnerRisk, positionRun, "The current pick is already beyond this league's observed historical range.");

        if (availability.NextPick >= range.MedianPick || elevatedOwnerRisk)
            return new(HistoricalTargetTiming.LastSafePick, HistoricalTargetRecommendation.TakeNow, availability, elevatedOwnerRisk, positionRun, elevatedOwnerRisk ? "Elevated availability risk from a repeat historical owner before your next turn." : "Your next pick reaches this league's historical median selection point.");

        if (availability.NextPick >= range.MinimumPick || positionRun)
            return new(HistoricalTargetTiming.Early, HistoricalTargetRecommendation.ProbablySurvives, availability, elevatedOwnerRisk, positionRun, positionRun ? "A current positional run warrants caution, but the historical range does not yet make this a last-safe pick." : "Your next pick enters the observed historical range before its median.");

        return new(HistoricalTargetTiming.SafeToWait, HistoricalTargetRecommendation.LikelyToSurvive, availability, elevatedOwnerRisk, positionRun, "Your next pick remains before this league's observed historical selection range.");
    }
}
