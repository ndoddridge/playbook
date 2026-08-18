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
            ImportedAtUtc = source.ImportedAtUtc }, errors, warnings);
    }
    private static HistoricalDraftPick CloneWithRoster(HistoricalDraftPick p, IReadOnlyDictionary<string, int> roster) => new() { PickNumber=p.PickNumber, Round=p.Round, DraftSlot=p.DraftSlot, OwnerKey=p.OwnerKey, OwnerName=p.OwnerName, SleeperUserId=p.SleeperUserId, RosterId=p.RosterId, SleeperPlayerId=p.SleeperPlayerId, PlaybookPlayerId=p.PlaybookPlayerId, PlayerName=p.PlayerName, Position=p.Position, NflTeam=p.NflTeam, PickedAtUtc=p.PickedAtUtc, IsKeeper=p.IsKeeper, RosterBefore=roster, HistoricalAdp=p.HistoricalAdp, HistoricalProjection=p.HistoricalProjection, HistoricalOverallRank=p.HistoricalOverallRank, HistoricalPositionRank=p.HistoricalPositionRank };
    private List<HistoricalLeagueDraft> LoadMutable() { lock (_gate) { return _drafts ??= _store.Load().ToList(); } }
    private static string PlayerKey(HistoricalDraftPick p) => p.SleeperPlayerId ?? p.PlaybookPlayerId?.ToString() ?? p.PlayerName;
    private static HistoricalEvidenceStrength Strength(int n) => n switch { <= 0 => HistoricalEvidenceStrength.Unavailable, 1 or 2 => HistoricalEvidenceStrength.Insufficient, <= 5 => HistoricalEvidenceStrength.Limited, <= 11 => HistoricalEvidenceStrength.Moderate, _ => HistoricalEvidenceStrength.Strong };
    private static LeagueType MapType(string? raw, int fallback) => raw switch { "2" => LeagueType.Dynasty, "1" => LeagueType.Keeper, "bestball" => LeagueType.BestBall, _ => fallback switch { 2 => LeagueType.Dynasty, 1 => LeagueType.Keeper, _ => LeagueType.Redraft } };
    private static HistoricalImportResult Fail(string error) => new(false, [error], []);
    private static HistoricalImportResult Success(HistoricalLeagueDraft draft, IReadOnlyList<string> warnings) => new(true, [], warnings, draft);
}
