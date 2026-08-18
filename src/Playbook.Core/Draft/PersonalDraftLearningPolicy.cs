using Playbook.Core.Historical;
using Playbook.Core.Leagues;

namespace Playbook.Core.Draft;

/// <summary>
/// Pure personal-learning rules: resolve the selected owner in an imported draft, emit
/// player-vs-player evidence with roster context, merge repeats without averaging contradictions,
/// and apply a bounded live adjustment. No store dependency — persistence lives with historical
/// import.
/// </summary>
public static class PersonalDraftLearningPolicy
{
    public const string MissingScopeMessage =
        "Select a league and a team in Draft Assistant before importing a draft for personal learning.";

    public const string UnknownOwnerMessage =
        "Could not identify this team's picks in the uploaded draft, so no personal learning was stored.";

    /// <summary>
    /// Personal preference is a bounded adjustment. It is smaller than the urgent-need bonus (3.0)
    /// and cannot close a major projection gap (≥ 4 pts).
    /// </summary>
    public const decimal MaxAdjustment = 1.8m;
    public const decimal MajorObjectiveGap = 4.0m;

    public static HistoricalEvidenceStrength Strength(int n) => n switch
    {
        <= 0 => HistoricalEvidenceStrength.Unavailable,
        1 or 2 => HistoricalEvidenceStrength.Insufficient,
        <= 5 => HistoricalEvidenceStrength.Limited,
        <= 11 => HistoricalEvidenceStrength.Moderate,
        _ => HistoricalEvidenceStrength.Strong
    };

    public static string ScoringFormat(IReadOnlyDictionary<string, double> scoringSettings) =>
        scoringSettings.TryGetValue("rec", out var rec)
            ? rec switch { >= 0.99 => "PPR", >= .49 => "HalfPPR", _ => "Standard" }
            : "Unknown";

    public static string ScoringFormat(ScoringType scoring) => scoring switch
    {
        ScoringType.Ppr => "PPR",
        ScoringType.HalfPpr => "HalfPPR",
        _ => "Standard"
    };

    public static string? PlayerKey(Guid? playbookId, string? sleeperId)
    {
        if (playbookId is Guid id && id != Guid.Empty)
        {
            return id.ToString("N");
        }

        return string.IsNullOrWhiteSpace(sleeperId) ? null : $"sleeper:{sleeperId.Trim()}";
    }

    public static string? PlayerKey(HistoricalDraftPick pick) =>
        PlayerKey(pick.PlaybookPlayerId, pick.SleeperPlayerId);

    public static string ContextKey(PersonalPreferenceContext ctx) =>
        $"{ctx.LeagueType}|{ctx.ScoringFormat}|{ctx.LeagueSize}|r{RoundBucket(ctx.Round)}|"
        + $"WR:{DepthBucket(Count(ctx.RosterBefore, "WR"))}|"
        + $"RB:{DepthBucket(Count(ctx.RosterBefore, "RB"))}|"
        + $"TE:{DepthBucket(Count(ctx.RosterBefore, "TE"))}|"
        + $"QB:{DepthBucket(Count(ctx.RosterBefore, "QB"))}";

    /// <summary>
    /// Match the selected Draft Assistant team to one owner in the uploaded draft. Display names
    /// are used only when they uniquely identify a single owner. Ambiguous or missing identity
    /// yields null — never a guessed owner.
    /// </summary>
    public static HistoricalOwner? ResolveOwner(HistoricalLeagueDraft draft, PersonalDraftLearningRequest request)
    {
        var owners = draft.Owners ?? [];
        HistoricalOwner? byUser = null;
        HistoricalOwner? byRoster = null;
        HistoricalOwner? byName = null;

        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            var matches = owners
                .Where(o => string.Equals(o.SleeperUserId, request.OwnerUserId, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 1)
            {
                byUser = matches[0];
            }
            else if (matches.Count > 1)
            {
                return null;
            }
        }

        if (request.RosterId is int rosterId)
        {
            var matches = owners.Where(o => o.RosterId == rosterId).ToList();
            if (matches.Count == 1)
            {
                byRoster = matches[0];
            }
            else if (matches.Count > 1)
            {
                return null;
            }
        }

        var name = FirstNonEmpty(request.OwnerDisplayName, request.TeamName);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var matches = owners
                .Where(o => string.Equals(o.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1)
            {
                byName = matches[0];
            }
            else if (matches.Count > 1)
            {
                return null;
            }
        }

        var resolved = new[] { byUser, byRoster, byName }.Where(o => o is not null).Cast<HistoricalOwner>().ToList();
        if (resolved.Count == 0)
        {
            return ResolveOwnerFromPicks(draft, request);
        }

        var keys = resolved.Select(OwnerKey).Distinct(StringComparer.Ordinal).ToList();
        return keys.Count == 1 ? resolved[0] : null;
    }

    /// <summary>
    /// Player-vs-player evidence for one owner's picks in one reconstructed draft. Alternatives
    /// are later selections still on the board in the next round of picks — never fabricated.
    /// </summary>
    public static IReadOnlyList<PersonalPlayerPreference> ExtractPreferences(
        HistoricalLeagueDraft draft, HistoricalOwner owner)
    {
        var ownerKey = OwnerKey(owner);
        var picks = (draft.Picks ?? []).OrderBy(p => p.PickNumber).ToList();
        var mine = picks.Where(p => PickBelongsTo(p, owner, ownerKey) && !p.IsKeeper).ToList();
        if (mine.Count == 0)
        {
            return [];
        }

        var scoring = ScoringFormat(draft.ScoringSettings);
        var window = Math.Max(1, draft.TeamCount);
        var roster = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PersonalPlayerPreference>();

        foreach (var pick in mine)
        {
            var preferredKey = PlayerKey(pick);
            if (preferredKey is null)
            {
                Increment(roster, pick.Position);
                continue;
            }

            var alternatives = picks
                .Where(p => p.PickNumber > pick.PickNumber
                            && p.PickNumber <= pick.PickNumber + window
                            && !p.IsKeeper
                            && PlayerKey(p) is not null
                            && !string.Equals(PlayerKey(p), preferredKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var altKeys = alternatives.Select(p => PlayerKey(p)!).ToList();
            var context = new PersonalPreferenceContext(
                draft.LeagueType,
                scoring,
                draft.TeamCount,
                pick.Round,
                pick.PickNumber,
                new Dictionary<string, int>(roster, StringComparer.OrdinalIgnoreCase),
                altKeys);

            foreach (var alt in alternatives)
            {
                results.Add(new PersonalPlayerPreference(
                    preferredKey,
                    pick.PlayerName,
                    PlayerKey(alt)!,
                    alt.PlayerName,
                    context,
                    ObservationCount: 1,
                    SourceDraftIds: [draft.HistoricalDraftId]));
            }

            Increment(roster, pick.Position);
        }

        return results;
    }

    public static PersonalDraftKnowledge Merge(
        PersonalDraftKnowledge existing,
        HistoricalLeagueDraft draft,
        HistoricalOwner owner,
        PersonalDraftLearningRequest request,
        IReadOnlyList<PersonalPlayerPreference> incoming)
    {
        var learned = existing.LearnedDraftIds.ToList();
        if (learned.Any(id => string.Equals(id, draft.HistoricalDraftId, StringComparison.Ordinal)))
        {
            return existing;
        }

        learned.Add(draft.HistoricalDraftId);
        var decisions = existing.DecisionCount + CountOwnerDecisions(draft, owner);
        var merged = existing.Preferences.ToList();

        foreach (var next in incoming)
        {
            var index = merged.FindIndex(p => SameEvidence(p, next));
            if (index < 0)
            {
                merged.Add(next);
                continue;
            }

            var current = merged[index];
            var sources = current.SourceDraftIds.ToList();
            if (!sources.Contains(draft.HistoricalDraftId, StringComparer.Ordinal))
            {
                sources.Add(draft.HistoricalDraftId);
            }

            merged[index] = current with
            {
                ObservationCount = current.ObservationCount + next.ObservationCount,
                SourceDraftIds = sources
            };
        }

        return new PersonalDraftKnowledge
        {
            LeagueId = request.LeagueId,
            TeamId = request.TeamId,
            OwnerKey = OwnerKey(owner),
            LeagueName = request.LeagueName,
            TeamName = request.TeamName,
            DraftCount = learned.Count,
            DecisionCount = decisions,
            LearnedDraftIds = learned,
            Preferences = merged,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public static PersonalDraftKnowledge Empty(PersonalDraftLearningRequest request, HistoricalOwner? owner = null) => new()
    {
        LeagueId = request.LeagueId,
        TeamId = request.TeamId,
        OwnerKey = owner is null ? null : OwnerKey(owner),
        LeagueName = request.LeagueName,
        TeamName = request.TeamName,
        DraftCount = 0,
        DecisionCount = 0
    };

    /// <summary>
    /// Bounded live adjustment for one candidate versus the currently available players.
    /// Strong contextual matches outweigh similar-but-not-identical contexts. A major objective
    /// projection gap is never overturned.
    /// </summary>
    public static PersonalPreferenceAdjustment Adjust(
        string candidateKey,
        string candidateName,
        decimal candidateProjection,
        IReadOnlyList<(string Key, string Name, decimal Projection)> available,
        PersonalDraftKnowledge knowledge,
        PersonalPreferenceContext current)
    {
        decimal net = 0;
        PersonalPlayerPreference? strongest = null;
        var strongestWeight = 0m;

        foreach (var pref in knowledge.Preferences)
        {
            var similarity = ContextSimilarity(pref.Context, current);
            if (similarity <= 0)
            {
                continue;
            }

            var prefersThis = string.Equals(pref.PreferredPlayerKey, candidateKey, StringComparison.OrdinalIgnoreCase);
            var passedThis = string.Equals(pref.PassedPlayerKey, candidateKey, StringComparison.OrdinalIgnoreCase);
            if (!prefersThis && !passedThis)
            {
                continue;
            }

            var rivalKey = prefersThis ? pref.PassedPlayerKey : pref.PreferredPlayerKey;
            var rival = available.FirstOrDefault(a => string.Equals(a.Key, rivalKey, StringComparison.OrdinalIgnoreCase));
            if (rival.Key is null)
            {
                continue;
            }

            var weight = StrengthWeight(pref.ObservationCount) * similarity;
            var gapAgainst = prefersThis
                ? rival.Projection - candidateProjection
                : candidateProjection - rival.Projection;
            var bounded = BoundAgainstObjectiveGap(weight, gapAgainst);
            if (bounded == 0)
            {
                continue;
            }

            net += prefersThis ? bounded : -bounded;
            if (Math.Abs(bounded) >= strongestWeight)
            {
                strongestWeight = Math.Abs(bounded);
                strongest = pref;
            }
        }

        net = Math.Clamp(net, -MaxAdjustment, MaxAdjustment);
        if (net == 0 || strongest is null)
        {
            return new PersonalPreferenceAdjustment(0, null);
        }

        var factor = new DraftRecommendationFactor
        {
            Label = "Personal preference",
            Detail = net > 0
                ? $"Previously took {strongest.PreferredPlayerName} over {strongest.PassedPlayerName} in a similar roster context ({strongest.ObservationCount} time{(strongest.ObservationCount == 1 ? "" : "s")})."
                : $"Previously passed {candidateName} for {strongest.PreferredPlayerName} in a similar roster context ({strongest.ObservationCount} time{(strongest.ObservationCount == 1 ? "" : "s")}).",
            Direction = net > 0 ? FactorDirection.Positive : FactorDirection.Negative,
            Available = true
        };
        return new PersonalPreferenceAdjustment(net, factor);
    }

    public static decimal ContextSimilarity(PersonalPreferenceContext observed, PersonalPreferenceContext current)
    {
        if (observed.LeagueType != current.LeagueType)
        {
            return 0m;
        }

        var sameScoring = string.Equals(observed.ScoringFormat, current.ScoringFormat, StringComparison.OrdinalIgnoreCase);
        var sameSize = observed.LeagueSize == current.LeagueSize;
        var sameDepth = DepthFingerprint(observed) == DepthFingerprint(current);
        var sameRound = RoundBucket(observed.Round) == RoundBucket(current.Round);

        if (sameScoring && sameSize && sameDepth && sameRound)
        {
            return 1.0m;
        }

        if (sameScoring && sameSize && sameDepth)
        {
            return 0.6m;
        }

        if (sameScoring && sameSize)
        {
            return 0.25m;
        }

        return sameScoring ? 0.15m : 0m;
    }

    public static PersonalPreferenceContext LiveContext(
        League league, int teamCount, int round, int pickNumber, IReadOnlyDictionary<string, int> rosterBefore,
        IReadOnlyList<string> availableKeys) =>
        new(league.LeagueType, ScoringFormat(league.ScoringType), teamCount, round, pickNumber, rosterBefore, availableKeys);

    public static PersonalDraftKnowledgeStatus Status(League? league, FantasyTeam? team, PersonalDraftKnowledge? knowledge)
    {
        var request = PersonalDraftLearningRequest.From(league, team);
        return new PersonalDraftKnowledgeStatus(
            league is not null,
            team is not null,
            league?.Name,
            request?.TeamName,
            knowledge?.DraftCount ?? 0,
            knowledge?.DecisionCount ?? 0);
    }

    internal static decimal StrengthWeight(int observationCount) => Strength(observationCount) switch
    {
        HistoricalEvidenceStrength.Insufficient => 0.35m,
        HistoricalEvidenceStrength.Limited => 0.75m,
        HistoricalEvidenceStrength.Moderate => 1.2m,
        HistoricalEvidenceStrength.Strong => MaxAdjustment,
        _ => 0m
    };

    internal static decimal BoundAgainstObjectiveGap(decimal weight, decimal objectiveGapAgainstCandidate)
    {
        if (objectiveGapAgainstCandidate >= MajorObjectiveGap)
        {
            return 0m;
        }

        if (objectiveGapAgainstCandidate >= 2.0m)
        {
            return weight * 0.25m;
        }

        return weight;
    }

    private static HistoricalOwner? ResolveOwnerFromPicks(HistoricalLeagueDraft draft, PersonalDraftLearningRequest request)
    {
        var picks = draft.Picks ?? [];
        IEnumerable<HistoricalDraftPick> matches = [];
        if (!string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            matches = picks.Where(p => string.Equals(p.SleeperUserId, request.OwnerUserId, StringComparison.Ordinal));
        }

        if (!matches.Any() && request.RosterId is int rosterId)
        {
            matches = picks.Where(p => p.RosterId == rosterId);
        }

        var name = FirstNonEmpty(request.OwnerDisplayName, request.TeamName);
        if (!matches.Any() && !string.IsNullOrWhiteSpace(name))
        {
            matches = picks.Where(p => string.Equals(p.OwnerName, name, StringComparison.OrdinalIgnoreCase));
        }

        var keys = matches.Select(p => p.OwnerKey).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count != 1)
        {
            return null;
        }

        var key = keys[0];
        var sample = matches.First();
        return draft.Owners.FirstOrDefault(o => OwnerKey(o) == key)
               ?? new HistoricalOwner
               {
                   SleeperUserId = sample.SleeperUserId,
                   DisplayName = sample.OwnerName,
                   RosterId = sample.RosterId
               };
    }

    private static bool PickBelongsTo(HistoricalDraftPick pick, HistoricalOwner owner, string ownerKey)
    {
        if (string.Equals(pick.OwnerKey, ownerKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(owner.SleeperUserId)
            && string.Equals(pick.SleeperUserId, owner.SleeperUserId, StringComparison.Ordinal))
        {
            return true;
        }

        return owner.RosterId is int rosterId && pick.RosterId == rosterId;
    }

    private static int CountOwnerDecisions(HistoricalLeagueDraft draft, HistoricalOwner owner)
    {
        var ownerKey = OwnerKey(owner);
        return (draft.Picks ?? []).Count(p => PickBelongsTo(p, owner, ownerKey) && !p.IsKeeper);
    }

    private static bool SameEvidence(PersonalPlayerPreference a, PersonalPlayerPreference b) =>
        string.Equals(a.PreferredPlayerKey, b.PreferredPlayerKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.PassedPlayerKey, b.PassedPlayerKey, StringComparison.OrdinalIgnoreCase)
        && ContextKey(a.Context) == ContextKey(b.Context);

    private static string OwnerKey(HistoricalOwner owner) =>
        owner.SleeperUserId
        ?? (owner.RosterId is int rosterId ? $"roster:{rosterId}" : owner.DisplayName);

    private static string DepthFingerprint(PersonalPreferenceContext ctx) =>
        $"WR:{DepthBucket(Count(ctx.RosterBefore, "WR"))}|RB:{DepthBucket(Count(ctx.RosterBefore, "RB"))}|TE:{DepthBucket(Count(ctx.RosterBefore, "TE"))}|QB:{DepthBucket(Count(ctx.RosterBefore, "QB"))}";

    private static string DepthBucket(int n) => n <= 0 ? "low" : n <= 2 ? "mid" : "high";

    private static string RoundBucket(int round) => round <= 3 ? "early" : round <= 8 ? "mid" : "late";

    private static int Count(IReadOnlyDictionary<string, int> roster, string position) =>
        roster.TryGetValue(position, out var n) ? n : 0;

    private static void Increment(Dictionary<string, int> roster, string position)
    {
        if (string.IsNullOrWhiteSpace(position))
        {
            return;
        }

        roster[position] = roster.GetValueOrDefault(position) + 1;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
