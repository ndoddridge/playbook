using System.Text.Json;
using Playbook.Application.Historical;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players;
using Playbook.Core.Historical;
using Playbook.Core.Leagues;

namespace Playbook.Infrastructure.Historical;

public sealed class HistoricalLeagueIntelligenceService : IHistoricalLeagueIntelligenceService
{
    private readonly IHistoricalLeagueDraftStore _store;
    private readonly ISleeperLeagueClient _sleeper;
    private readonly IPlayerIdentityDirectory _identities;
    private readonly object _gate = new();
    private List<HistoricalLeagueDraft>? _drafts;

    public HistoricalLeagueIntelligenceService(IHistoricalLeagueDraftStore store, ISleeperLeagueClient sleeper, IPlayerIdentityDirectory identities)
    { _store = store; _sleeper = sleeper; _identities = identities; }

    public async Task<HistoricalImportResult> ImportSleeperLeagueHistoryAsync(string leagueId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(leagueId)) return Fail("A Sleeper league ID is required.");
        var imported = new List<HistoricalLeagueDraft>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var next = leagueId.Trim();
        while (visited.Add(next))
        {
            var league = await _sleeper.GetLeagueSnapshotAsync(next, cancellationToken);
            if (league is null) return imported.Count == 0 ? Fail($"Sleeper league '{next}' was not found.") : Success(imported.Last(), ["The linked league history ended at an unavailable league."]);
            var summaries = await _sleeper.GetDraftsForLeagueAsync(next, cancellationToken);
            foreach (var summary in summaries)
            {
                var draft = await _sleeper.GetDraftAsync(summary.DraftId, cancellationToken);
                if (draft is null) continue;
                var picks = await _sleeper.GetDraftPicksAsync(draft.DraftId, cancellationToken);
                var result = await ImportAsync(BuildSleeperDraft(league, draft, picks), cancellationToken);
                if (!result.Succeeded) return result;
                imported.Add(result.Draft!);
            }
            if (string.IsNullOrWhiteSpace(league.PreviousLeagueId)) break;
            next = league.PreviousLeagueId;
        }
        return imported.Count == 0 ? Fail("Sleeper returned no drafts for this league history.") : Success(imported.Last(), []);
    }

    public async Task<HistoricalImportResult> ImportJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            var draft = JsonSerializer.Deserialize<HistoricalLeagueDraft>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return draft is null ? Fail("The import JSON did not contain a historical draft.") : await ImportAsync(draft, cancellationToken);
        }
        catch (JsonException ex) { return Fail($"Malformed historical import JSON: {ex.Message}"); }
    }

    public Task<HistoricalImportResult> ImportAsync(HistoricalLeagueDraft draft, CancellationToken cancellationToken = default)
    {
        var validation = ValidateAndReconstruct(draft);
        if (validation.Errors.Count > 0) return Task.FromResult(new HistoricalImportResult(false, validation.Errors, validation.Warnings));
        lock (_gate)
        {
            var all = LoadMutable();
            all.RemoveAll(existing => string.Equals(existing.HistoricalDraftId, validation.Draft!.HistoricalDraftId, StringComparison.Ordinal));
            all.Add(validation.Draft!);
            _store.Save(all);
            _drafts = all;
        }
        return Task.FromResult(new HistoricalImportResult(true, [], validation.Warnings, validation.Draft));
    }

    public Task<HistoricalImportResult> ImportAdpSnapshotJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<HistoricalAdpSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return snapshot is null ? Task.FromResult(Fail("The import JSON did not contain an ADP snapshot.")) : ImportAdpSnapshotAsync(snapshot, cancellationToken);
        }
        catch (JsonException ex) { return Task.FromResult(Fail($"Malformed ADP snapshot JSON: {ex.Message}")); }
    }

    public Task<HistoricalImportResult> ImportAdpSnapshotAsync(HistoricalAdpSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var errors = ValidateSnapshot(snapshot);
        if (errors.Count > 0) return Task.FromResult(new HistoricalImportResult(false, errors, []));
        lock (_gate)
        {
            var all = LoadMutable();
            var targets = all.Where(d => d.LeagueId == snapshot.LeagueId && d.Season == snapshot.Season && d.LeagueType == snapshot.LeagueType).ToList();
            if (targets.Count == 0) return Task.FromResult(Fail("Import the matching historical league draft before importing its ADP snapshot."));
            foreach (var draft in targets)
            {
                var snapshots = draft.HistoricalAdpSnapshots.Where(s => !(s.Source.Equals(snapshot.Source, StringComparison.OrdinalIgnoreCase) && s.SnapshotDateUtc == snapshot.SnapshotDateUtc && s.ScoringFormat.Equals(snapshot.ScoringFormat, StringComparison.OrdinalIgnoreCase))).Append(snapshot).ToList();
                var index = all.FindIndex(d => d.HistoricalDraftId == draft.HistoricalDraftId);
                all[index] = CopyDraft(draft, snapshots);
            }
            _store.Save(all); _drafts = all;
        }
        return Task.FromResult(new HistoricalImportResult(true, [], [], GetDrafts(snapshot.LeagueId, snapshot.LeagueType).First(d => d.Season == snapshot.Season)));
    }

    public IReadOnlyList<HistoricalLeagueDraft> GetDrafts(string? leagueId = null, LeagueType? leagueType = null) =>
        LoadMutable().Where(d => (leagueId is null || d.LeagueId == leagueId) && (leagueType is null || d.LeagueType == leagueType))
            .OrderByDescending(d => d.Season).ToList();

    public IReadOnlyList<HistoricalOwnerTendency> GetOwnerTendencies(string leagueId, LeagueType leagueType = LeagueType.Redraft) =>
        GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks.Select(p => new { d, p }))
            .GroupBy(x => new { x.p.OwnerKey, x.p.OwnerName, x.p.Position, x.p.Round })
            .Select(g => new HistoricalOwnerTendency(leagueId, g.Key.OwnerKey, g.Key.OwnerName, leagueType, g.Key.Position, g.Key.Round,
                g.Count(), g.Select(x => x.d.Season).Distinct().Count(), Strength(g.Count())))
            .OrderByDescending(x => x.SelectionCount).ThenBy(x => x.OwnerName).ToList();

    public HistoricalPlayerHistory GetPlayerHistory(string leagueId, string playerKey, LeagueType leagueType = LeagueType.Redraft)
    {
        var matches = GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks.Select(p => new { d, p }))
            .Where(x => string.Equals(PlayerKey(x.p), playerKey, StringComparison.OrdinalIgnoreCase)).ToList();
        return new HistoricalPlayerHistory(playerKey, matches.Count, matches.Select(x => x.d.Season).Distinct().Count(),
            matches.Count == 0 ? null : matches.Min(x => x.p.PickNumber), matches.Count == 0 ? null : matches.Max(x => x.p.PickNumber),
            matches.Select(x => x.p.OwnerKey).Distinct().ToList(), Strength(matches.Count));
    }

    public IReadOnlyList<HistoricalPositionDraftRange> GetPositionDraftRanges(string leagueId, LeagueType leagueType = LeagueType.Redraft) =>
        GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks).GroupBy(p => p.Position, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HistoricalPositionDraftRange(leagueId, leagueType, g.Key, g.Count(), g.Min(p => p.PickNumber), g.Max(p => p.PickNumber), Strength(g.Count())))
            .OrderBy(x => x.EarliestPick).ToList();

    public IReadOnlyList<HistoricalLeaguePlayerRange> GetLeaguePlayerRanges(string leagueId, LeagueType leagueType = LeagueType.Redraft) =>
        GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks.Select(p => new { d, p })).GroupBy(x => PlayerKey(x.p), StringComparer.OrdinalIgnoreCase)
            .Select(g => { var picks = g.Select(x => x.p.PickNumber).Order().ToList(); return new HistoricalLeaguePlayerRange(g.Key, g.First().p.PlayerName, g.First().p.Position,
                picks.Count, g.Select(x => x.d.Season).Distinct().Count(), picks.First(), Median(picks), picks.Average(), picks.Last(), g.Select(x => x.p.OwnerKey).Distinct().Count(),
                g.Select(x => x.p.OwnerKey).Distinct().ToList(), g.OrderByDescending(x => x.d.Season).Take(5).Select(x => x.p.PickNumber).ToList(), Strength(picks.Count)); })
            .OrderBy(x => x.MedianPick).ToList();

    public IReadOnlyList<HistoricalLeaguePositionTendency> GetLeaguePositionTendencies(string leagueId, LeagueType leagueType = LeagueType.Redraft) =>
        GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks).GroupBy(p => p.Position, StringComparer.OrdinalIgnoreCase)
            .Select(g => new HistoricalLeaguePositionTendency(g.Key, g.Count(), g.Min(x => x.PickNumber), g.Average(x => x.PickNumber),
                g.GroupBy(x => x.Round).ToDictionary(x => x.Key, x => x.Count()), Strength(g.Count()))).OrderBy(x => x.FirstSelectedPick).ToList();

    public IReadOnlyList<HistoricalOwnerMarketSignal> GetOwnerMarketSignals(string leagueId, LeagueType leagueType = LeagueType.Redraft) =>
        GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks.Select(p => new { d, p, comparison = Comparison(d, p) }))
            .GroupBy(x => new { x.p.OwnerKey, x.p.OwnerName, x.p.Position })
            .Select(g => new HistoricalOwnerMarketSignal(g.Key.OwnerKey, g.Key.OwnerName, g.Key.Position, g.Count(), g.Select(x => x.d.Season).Distinct().Count(),
                g.Average(x => (double?)x.p.PickNumber), AverageOrNull(g.Where(x => x.comparison.Adp is not null).Select(x => x.p.PickNumber - x.comparison.Adp!.Value)), Strength(g.Count())))
            .OrderByDescending(x => x.SelectionCount).ToList();

    public IReadOnlyList<HistoricalPickMarketComparison> GetPickMarketComparisons(string leagueId, LeagueType leagueType = LeagueType.Redraft) =>
        GetDrafts(leagueId, leagueType).SelectMany(d => d.Picks.Select(p => Comparison(d, p))).ToList();

    public HistoricalTargetPlayerQuery QueryTargetPlayer(string leagueId, string playerKey, int currentPick, int nextPick, LeagueType leagueType = LeagueType.Redraft)
    {
        var range = GetLeaguePlayerRanges(leagueId, leagueType).FirstOrDefault(x => x.PlayerKey.Equals(playerKey, StringComparison.OrdinalIgnoreCase));
        var comparisons = GetPickMarketComparisons(leagueId, leagueType).Where(x => x.PlayerKey.Equals(playerKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var adp = GetDrafts(leagueId, leagueType).SelectMany(d => d.HistoricalAdpSnapshots).OrderByDescending(x => x.SnapshotDateUtc).SelectMany(x => x.Players).FirstOrDefault(x => RecordKey(x).Equals(playerKey, StringComparison.OrdinalIgnoreCase));
        var evidence = range?.EvidenceStrength ?? HistoricalEvidenceStrength.Unavailable;
        var availability = range is null || range.DraftCount < 3 ? HistoricalAvailabilityAssessment.Unknown : currentPick >= range.MaximumPick || nextPick >= range.MedianPick ? HistoricalAvailabilityAssessment.Low : nextPick >= range.MinimumPick ? HistoricalAvailabilityAssessment.Medium : HistoricalAvailabilityAssessment.High;
        var explanation = availability == HistoricalAvailabilityAssessment.Unknown ? "Fewer than three league selections; availability is unknown." : $"Observed across {range!.DraftCount} drafts: picks {range.MinimumPick}-{range.MaximumPick}, median {range.MedianPick:0.#}.";
        return new HistoricalTargetPlayerQuery(range, adp, comparisons, new HistoricalAvailabilityResult(playerKey, currentPick, nextPick, availability, range, adp, evidence, explanation));
    }

    private HistoricalLeagueDraft BuildSleeperDraft(SleeperLeagueSnapshot league, SleeperDraftSnapshot draft, IReadOnlyList<SleeperDraftPickSnapshot> picks)
    {
        var owners = league.Rosters.Select(r => new HistoricalOwner { SleeperUserId = r.OwnerId, DisplayName = r.OwnerName, RosterId = r.RosterId }).ToList();
        var byUser = owners.Where(o => o.SleeperUserId is not null).ToDictionary(o => o.SleeperUserId!, StringComparer.Ordinal);
        var byRoster = owners.Where(o => o.RosterId is not null).ToDictionary(o => o.RosterId!.Value);
        var raw = picks.Select(p =>
        {
            byUser.TryGetValue(p.PickedByUserId ?? string.Empty, out var byUserOwner);
            byRoster.TryGetValue(p.RosterId ?? 0, out var byRosterOwner);
            var owner = byUserOwner ?? byRosterOwner;
            var identity = p.SleeperPlayerId is null ? null : _identities.GetBySleeperId(p.SleeperPlayerId);
            return new HistoricalDraftPick { PickNumber = p.PickNumber, Round = p.Round, DraftSlot = p.DraftSlot,
                OwnerKey = owner?.SleeperUserId ?? $"unresolved:{league.ExternalLeagueId}:{p.RosterId?.ToString() ?? p.DraftSlot.ToString()}",
                OwnerName = owner?.DisplayName ?? "Unresolved owner", SleeperUserId = owner?.SleeperUserId, RosterId = p.RosterId,
                SleeperPlayerId = p.SleeperPlayerId, PlaybookPlayerId = identity?.PlaybookId,
                PlayerName = string.IsNullOrWhiteSpace(p.PlayerName) ? identity?.FullName ?? "Unknown player" : p.PlayerName!,
                Position = string.IsNullOrWhiteSpace(p.Position) ? identity?.Position ?? "UNKNOWN" : p.Position!, NflTeam = p.NflTeam ?? identity?.Team,
                PickedAtUtc = p.PickedAtUtc, IsKeeper = p.IsKeeper };
        }).ToList();
        return new HistoricalLeagueDraft { HistoricalDraftId = draft.DraftId, LeagueId = league.ExternalLeagueId, Season = draft.Season,
            LeagueName = league.Name, LeagueType = MapType(draft.LeagueTypeRaw, league.SleeperLeagueType), DraftType = draft.Type,
            TeamCount = draft.Teams > 0 ? draft.Teams : league.TeamCount, RoundCount = draft.Rounds,
            ScoringSettings = league.ScoringSettings, RosterSettings = draft.RosterPositions.Count > 0 ? draft.RosterPositions : league.RosterPositions,
            Owners = owners, Picks = raw, DraftedAtUtc = draft.Status == "complete" ? raw.Select(p => p.PickedAtUtc).Where(x => x is not null).Max() : null,
            Source = "sleeper", IsComplete = draft.Status.Equals("complete", StringComparison.OrdinalIgnoreCase) };
    }

    private (HistoricalLeagueDraft? Draft, List<string> Errors, List<string> Warnings) ValidateAndReconstruct(HistoricalLeagueDraft source)
    {
        var errors = new List<string>(); var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(source.HistoricalDraftId) || string.IsNullOrWhiteSpace(source.LeagueId)) errors.Add("Draft ID and league ID are required.");
        if (source.TeamCount <= 0 || source.RoundCount <= 0) errors.Add("Team count and round count must be positive.");
        var owners = source.Owners ?? []; var picks = (source.Picks ?? []).OrderBy(p => p.PickNumber).ToList();
        if (picks.GroupBy(p => p.PickNumber).Any(g => g.Count() > 1)) errors.Add("Duplicate pick numbers are not allowed.");
        if (picks.Any(p => p.PickNumber <= 0 || p.PickNumber > source.TeamCount * source.RoundCount || p.Round <= 0 || p.Round > source.RoundCount)) errors.Add("A pick number or round is impossible for this draft's settings.");
        if (picks.Any(p => string.IsNullOrWhiteSpace(p.OwnerKey) || string.IsNullOrWhiteSpace(p.OwnerName))) errors.Add("Every pick must identify an owner; unresolved identity must be explicit.");
        if (picks.Any(p => string.IsNullOrWhiteSpace(p.PlayerName) || (string.IsNullOrWhiteSpace(p.SleeperPlayerId) && p.PlaybookPlayerId is null))) errors.Add("Every pick needs a player name and a Sleeper or Playbook player identity.");
        if (owners.Select(o => o.RosterId).Where(x => x is not null).Distinct().Count() > source.TeamCount) errors.Add("Owner roster count exceeds the declared team count.");
        if (picks.Count < source.TeamCount * source.RoundCount) warnings.Add($"Incomplete draft: {picks.Count} of {source.TeamCount * source.RoundCount} expected picks are present.");
        if (picks.Any(p => p.SleeperPlayerId is null && p.PlaybookPlayerId is not null)) warnings.Add("Some player mappings have no Sleeper ID and cannot be re-linked to Sleeper automatically.");
        if (errors.Count > 0) return (null, errors, warnings);
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var reconstructed = new List<HistoricalDraftPick>();
        foreach (var pick in picks)
        {
            if (!counts.TryGetValue(pick.OwnerKey, out var roster)) counts[pick.OwnerKey] = roster = new(StringComparer.OrdinalIgnoreCase);
            reconstructed.Add(CloneWithRoster(pick, new Dictionary<string, int>(roster, StringComparer.OrdinalIgnoreCase)));
            roster[pick.Position] = roster.GetValueOrDefault(pick.Position) + 1;
        }
        return (new HistoricalLeagueDraft { HistoricalDraftId = source.HistoricalDraftId, LeagueId = source.LeagueId, Season = source.Season,
            LeagueName = source.LeagueName, LeagueType = source.LeagueType, DraftType = source.DraftType, TeamCount = source.TeamCount, RoundCount = source.RoundCount,
            ScoringSettings = new Dictionary<string, double>(source.ScoringSettings), RosterSettings = source.RosterSettings.ToList(), Owners = owners,
            Picks = reconstructed, DraftedAtUtc = source.DraftedAtUtc, Source = source.Source, IsComplete = source.IsComplete && picks.Count == source.TeamCount * source.RoundCount,
            ImportedAtUtc = source.ImportedAtUtc, HistoricalAdpSnapshots = source.HistoricalAdpSnapshots ?? [] }, errors, warnings);
    }
    private static HistoricalDraftPick CloneWithRoster(HistoricalDraftPick p, IReadOnlyDictionary<string, int> roster) => new() { PickNumber=p.PickNumber, Round=p.Round, DraftSlot=p.DraftSlot, OwnerKey=p.OwnerKey, OwnerName=p.OwnerName, SleeperUserId=p.SleeperUserId, RosterId=p.RosterId, SleeperPlayerId=p.SleeperPlayerId, PlaybookPlayerId=p.PlaybookPlayerId, PlayerName=p.PlayerName, Position=p.Position, NflTeam=p.NflTeam, PickedAtUtc=p.PickedAtUtc, IsKeeper=p.IsKeeper, RosterBefore=roster, HistoricalAdp=p.HistoricalAdp, HistoricalProjection=p.HistoricalProjection, HistoricalOverallRank=p.HistoricalOverallRank, HistoricalPositionRank=p.HistoricalPositionRank };
    private static List<string> ValidateSnapshot(HistoricalAdpSnapshot s)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(s.LeagueId) || string.IsNullOrWhiteSpace(s.Season) || string.IsNullOrWhiteSpace(s.Source) || string.IsNullOrWhiteSpace(s.ScoringFormat)) errors.Add("ADP snapshot league, season, source, and scoring format are required.");
        if (s.SnapshotDateUtc == default) errors.Add("ADP snapshot date is required; undated market data cannot evaluate history.");
        if (s.Players is null || s.Players.Count == 0) errors.Add("ADP snapshot must contain players.");
        if (s.Players?.Any(p => string.IsNullOrWhiteSpace(p.PlayerName) || string.IsNullOrWhiteSpace(p.Position) || (p.SleeperPlayerId is null && p.PlaybookPlayerId is null)) == true) errors.Add("Every ADP player needs a name, position, and stable Sleeper or Playbook identity.");
        return errors;
    }
    private static HistoricalLeagueDraft CopyDraft(HistoricalLeagueDraft d, IReadOnlyList<HistoricalAdpSnapshot> snapshots) => new() { HistoricalDraftId=d.HistoricalDraftId, LeagueId=d.LeagueId, Season=d.Season, LeagueName=d.LeagueName, LeagueType=d.LeagueType, DraftType=d.DraftType, TeamCount=d.TeamCount, RoundCount=d.RoundCount, ScoringSettings=d.ScoringSettings, RosterSettings=d.RosterSettings, Owners=d.Owners, Picks=d.Picks, DraftedAtUtc=d.DraftedAtUtc, Source=d.Source, IsComplete=d.IsComplete, ImportedAtUtc=d.ImportedAtUtc, HistoricalAdpSnapshots=snapshots };
    private static HistoricalPickMarketComparison Comparison(HistoricalLeagueDraft draft, HistoricalDraftPick pick)
    {
        var key = PlayerKey(pick); var format = ScoringFormat(draft);
        var snapshot = draft.HistoricalAdpSnapshots.Where(s => s.LeagueType == draft.LeagueType && s.ScoringFormat.Equals(format, StringComparison.OrdinalIgnoreCase) && s.SnapshotDateUtc <= draft.DraftedAtUtc).OrderByDescending(s => s.SnapshotDateUtc).FirstOrDefault();
        if (draft.DraftedAtUtc is null) return new(draft.HistoricalDraftId, pick.PickNumber, key, null, null, null, null, HistoricalMarketValueClassification.Unavailable, "Draft time is unavailable.");
        if (snapshot is null) return new(draft.HistoricalDraftId, pick.PickNumber, key, null, null, null, null, HistoricalMarketValueClassification.Unavailable, "No season-, format-, and time-correct ADP snapshot is available.");
        var adp = snapshot.Players.FirstOrDefault(x => RecordKey(x).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (adp is null || adp.Adp is null) return new(draft.HistoricalDraftId, pick.PickNumber, key, null, adp?.OverallRank, adp?.PositionRank, snapshot.SnapshotDateUtc, HistoricalMarketValueClassification.Unavailable, "The snapshot has no ADP for this player identity.");
        var delta = pick.PickNumber - adp.Adp.Value;
        var classification = delta <= -18 ? HistoricalMarketValueClassification.MajorReach : delta <= -7 ? HistoricalMarketValueClassification.Reach : delta >= 18 ? HistoricalMarketValueClassification.MajorValue : delta >= 7 ? HistoricalMarketValueClassification.Value : HistoricalMarketValueClassification.Market;
        return new(draft.HistoricalDraftId, pick.PickNumber, key, adp.Adp, adp.OverallRank, adp.PositionRank, snapshot.SnapshotDateUtc, classification, null);
    }
    private static string ScoringFormat(HistoricalLeagueDraft d) => d.ScoringSettings.TryGetValue("rec", out var rec) ? rec switch { >= 0.99 => "PPR", >= .49 => "HalfPPR", _ => "Standard" } : "Unknown";
    private static string RecordKey(HistoricalAdpRecord p) => p.SleeperPlayerId ?? p.PlaybookPlayerId?.ToString() ?? string.Empty;
    private static double Median(IReadOnlyList<int> values) => values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2d;
    private static double? AverageOrNull(IEnumerable<double> values) { var v = values.ToList(); return v.Count == 0 ? null : v.Average(); }
    private List<HistoricalLeagueDraft> LoadMutable() { lock (_gate) { return _drafts ??= _store.Load().ToList(); } }
    private static string PlayerKey(HistoricalDraftPick p) => p.SleeperPlayerId ?? p.PlaybookPlayerId?.ToString() ?? p.PlayerName;
    private static HistoricalEvidenceStrength Strength(int n) => n switch { <= 0 => HistoricalEvidenceStrength.Unavailable, 1 or 2 => HistoricalEvidenceStrength.Insufficient, <= 5 => HistoricalEvidenceStrength.Limited, <= 11 => HistoricalEvidenceStrength.Moderate, _ => HistoricalEvidenceStrength.Strong };
    private static LeagueType MapType(string? raw, int fallback) => raw switch { "2" => LeagueType.Dynasty, "1" => LeagueType.Keeper, "bestball" => LeagueType.BestBall, _ => fallback switch { 2 => LeagueType.Dynasty, 1 => LeagueType.Keeper, _ => LeagueType.Redraft } };
    private static HistoricalImportResult Fail(string error) => new(false, [error], []);
    private static HistoricalImportResult Success(HistoricalLeagueDraft draft, IReadOnlyList<string> warnings) => new(true, [], warnings, draft);
}
