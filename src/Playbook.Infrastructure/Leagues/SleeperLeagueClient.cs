using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Leagues.Sleeper;

namespace Playbook.Infrastructure.Leagues;

public sealed class SleeperLeagueClient : ISleeperLeagueClient
{
    public const string HttpClientName = "SleeperLeague";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SleeperLeagueClient> _logger;

    public SleeperLeagueClient(
        IHttpClientFactory httpClientFactory,
        ILogger<SleeperLeagueClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SleeperLeagueSnapshot?> GetLeagueSnapshotAsync(
        string leagueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueId);
        var normalizedId = leagueId.Trim();
        var client = _httpClientFactory.CreateClient(HttpClientName);

        var league = await GetAsync<SleeperLeagueDto>(
            client,
            $"league/{Uri.EscapeDataString(normalizedId)}",
            cancellationToken).ConfigureAwait(false);

        if (league is null || string.IsNullOrWhiteSpace(league.LeagueId))
        {
            return null;
        }

        var usersTask = GetListAsync<SleeperUserDto>(
            client,
            $"league/{Uri.EscapeDataString(normalizedId)}/users",
            cancellationToken);
        var rostersTask = GetListAsync<SleeperRosterDto>(
            client,
            $"league/{Uri.EscapeDataString(normalizedId)}/rosters",
            cancellationToken);
        var stateTask = GetAsync<SleeperNflStateDto>(client, "state/nfl", cancellationToken);

        await Task.WhenAll(usersTask, rostersTask, stateTask).ConfigureAwait(false);

        var users = await usersTask.ConfigureAwait(false);
        var rosters = await rostersTask.ConfigureAwait(false);
        var nflState = await stateTask.ConfigureAwait(false);

        var userLookup = users
            .Where(u => !string.IsNullOrWhiteSpace(u.UserId))
            .GroupBy(u => u.UserId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var rosterSnapshots = rosters
            .OrderBy(r => r.RosterId)
            .Select(r =>
            {
                userLookup.TryGetValue(r.OwnerId ?? string.Empty, out var owner);
                var ownerName = string.IsNullOrWhiteSpace(owner?.DisplayName)
                    ? "Unknown owner"
                    : owner!.DisplayName!;
                var teamName = string.IsNullOrWhiteSpace(owner?.Metadata?.TeamName)
                    ? ownerName
                    : owner!.Metadata!.TeamName!;

                var fantasyPoints = r.Settings is null
                    ? 0d
                    : r.Settings.FantasyPoints + (r.Settings.FantasyPointsDecimal / 100d);

                return new SleeperRosterSnapshot
                {
                    RosterId = r.RosterId,
                    OwnerId = r.OwnerId,
                    TeamName = teamName,
                    OwnerName = ownerName,
                    SleeperPlayerIds = r.Players ?? [],
                    StarterSleeperPlayerIds = r.Starters ?? [],
                    ReserveSleeperPlayerIds = r.Reserve ?? [],
                    TaxiSleeperPlayerIds = r.Taxi ?? [],
                    Wins = r.Settings?.Wins ?? 0,
                    Losses = r.Settings?.Losses ?? 0,
                    Ties = r.Settings?.Ties ?? 0,
                    FantasyPoints = fantasyPoints
                };
            })
            .ToList();

        var teamCount = league.Settings?.NumTeams > 0
            ? league.Settings.NumTeams
            : league.TotalRosters;

        var currentWeek = nflState?.DisplayWeek > 0
            ? nflState.DisplayWeek
            : nflState?.Week > 0
                ? nflState.Week
                : 1;

        return new SleeperLeagueSnapshot
        {
            ExternalLeagueId = league.LeagueId!,
            Name = string.IsNullOrWhiteSpace(league.Name) ? $"Sleeper {normalizedId}" : league.Name!,
            Season = string.IsNullOrWhiteSpace(league.Season) ? "unknown" : league.Season!,
            Status = string.IsNullOrWhiteSpace(league.Status) ? "unknown" : league.Status!,
            TeamCount = teamCount,
            CurrentWeek = currentWeek,
            SleeperLeagueType = league.Settings?.Type ?? 0,
            ScoringSettings = league.ScoringSettings is null
                ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, double>(league.ScoringSettings, StringComparer.OrdinalIgnoreCase),
            RosterPositions = league.RosterPositions ?? [],
            Rosters = rosterSnapshots
        };
    }

    public async Task<IReadOnlyList<SleeperDraftSummary>> GetDraftsForLeagueAsync(
        string leagueId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leagueId);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var drafts = await GetListAsync<SleeperDraftDto>(
            client,
            $"league/{Uri.EscapeDataString(leagueId.Trim())}/drafts",
            cancellationToken).ConfigureAwait(false);

        return drafts
            .Where(d => !string.IsNullOrWhiteSpace(d.DraftId))
            .Select(d => new SleeperDraftSummary
            {
                DraftId = d.DraftId!,
                Status = d.Status ?? "unknown",
                Season = d.Season ?? "unknown",
                StartTime = d.StartTime
            })
            .ToList();
    }

    public async Task<SleeperDraftSnapshot?> GetDraftAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var draft = await GetAsync<SleeperDraftDto>(
            client,
            $"draft/{Uri.EscapeDataString(draftId.Trim())}",
            cancellationToken).ConfigureAwait(false);

        if (draft is null || string.IsNullOrWhiteSpace(draft.DraftId))
        {
            return null;
        }

        return new SleeperDraftSnapshot
        {
            DraftId = draft.DraftId!,
            LeagueId = draft.LeagueId ?? string.Empty,
            Season = draft.Season ?? "unknown",
            Status = draft.Status ?? "unknown",
            Type = string.IsNullOrWhiteSpace(draft.Type) ? "snake" : draft.Type!,
            Rounds = draft.Settings?.Rounds ?? 0,
            Teams = draft.Settings?.Teams ?? 0,
            DraftOrderByUserId = draft.DraftOrder is null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(draft.DraftOrder, StringComparer.Ordinal),
            SlotToRosterId = ParseSlotToRosterId(draft.SlotToRosterId),
            RosterPositions = draft.Settings?.ToRosterPositions() ?? [],
            ScoringType = draft.Metadata?.ScoringType,
            LeagueTypeRaw = draft.Metadata?.LeagueType,
            Name = draft.Metadata?.Name
        };
    }

    /// <summary>
    /// Sleeper publishes slot_to_roster_id keyed by slot-as-string, and a slot can legitimately
    /// map to null before a draft is populated. Null entries are dropped rather than coerced.
    /// </summary>
    private static IReadOnlyDictionary<int, int> ParseSlotToRosterId(
        IReadOnlyDictionary<string, int?>? raw)
    {
        var result = new Dictionary<int, int>();
        if (raw is null)
        {
            return result;
        }

        foreach (var (slotText, rosterId) in raw)
        {
            if (rosterId is int roster && int.TryParse(slotText, out var slot))
            {
                result[slot] = roster;
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<SleeperDraftPickSnapshot>> GetDraftPicksAsync(
        string draftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var picks = await GetListAsync<SleeperDraftPickDto>(
            client,
            $"draft/{Uri.EscapeDataString(draftId.Trim())}/picks",
            cancellationToken).ConfigureAwait(false);

        return picks
            .OrderBy(p => p.PickNo)
            .Select(p => new SleeperDraftPickSnapshot
            {
                PickNumber = p.PickNo,
                Round = p.Round,
                DraftSlot = p.DraftSlot,
                RosterId = p.RosterId,
                PickedByUserId = p.PickedBy,
                SleeperPlayerId = p.PlayerId,
                IsKeeper = p.IsKeeper ?? false
            })
            .ToList();
    }

    private async Task<T?> GetAsync<T>(
        HttpClient client,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Sleeper league request failed for {Url}: {StatusCode}",
                    relativeUrl,
                    (int)response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            return await response.Content
                .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sleeper league request failed for {Url}", relativeUrl);
            throw;
        }
    }

    private async Task<List<T>> GetListAsync<T>(
        HttpClient client,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        var result = await GetAsync<List<T>>(client, relativeUrl, cancellationToken).ConfigureAwait(false);
        return result ?? [];
    }
}
