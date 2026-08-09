using System.Globalization;
using Microsoft.Extensions.Logging;
using Playbook.Application.Replay;
using Playbook.Application.Stats;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Replay.Nflverse;

/// <summary>
/// Builds <see cref="HistoricalRawWeekData"/> from official nflverse releases.
/// Separates pre-game signals from Week N outcomes. Does not invent projections.
/// </summary>
public sealed class NflverseHistoricalDataProvider : IHistoricalDataProvider
{
    public const string ProviderKey = "nflverse";

    private readonly NflverseCsvCache _cache;
    private readonly IHistoricalPlayerIdentityNormalizer _identities;
    private readonly ILogger<NflverseHistoricalDataProvider> _logger;

    public NflverseHistoricalDataProvider(
        NflverseCsvCache cache,
        IHistoricalPlayerIdentityNormalizer identities,
        ILogger<NflverseHistoricalDataProvider> logger)
    {
        _cache = cache;
        _identities = identities;
        _logger = logger;
    }

    public string ProviderId => ProviderKey;

    public bool Supports(int season, int week) =>
        season >= 2002 && week is >= 1 and <= 18;

    public async Task<HistoricalRawWeekData?> GetWeekAsync(
        int season,
        int week,
        ScoringType scoringType,
        CancellationToken cancellationToken = default)
    {
        if (!Supports(season, week))
        {
            return null;
        }

        var schedulesPath = await _cache.EnsureFileAsync(
                NflverseReleaseCatalog.SchedulesUrl,
                "games.csv",
                cancellationToken)
            .ConfigureAwait(false);
        var cutoff = await ResolveInformationCutoffAsync(schedulesPath, season, week, cancellationToken)
            .ConfigureAwait(false);

        var rosterPath = await _cache.EnsureFileAsync(
                string.Format(NflverseReleaseCatalog.WeeklyRostersUrl, season),
                $"roster_weekly_{season}.csv",
                cancellationToken)
            .ConfigureAwait(false);
        var statsPath = await _cache.EnsureFileAsync(
                string.Format(NflverseReleaseCatalog.PlayerStatsUrl, season),
                $"player_stats_{season}.csv.gz",
                cancellationToken)
            .ConfigureAwait(false);
        var injuriesPath = await _cache.EnsureFileAsync(
                string.Format(NflverseReleaseCatalog.InjuriesUrl, season),
                $"injuries_{season}.csv",
                cancellationToken)
            .ConfigureAwait(false);
        var snapsPath = await _cache.EnsureFileAsync(
                string.Format(NflverseReleaseCatalog.SnapCountsUrl, season),
                $"snap_counts_{season}.csv.gz",
                cancellationToken)
            .ConfigureAwait(false);
        var depthPath = await _cache.EnsureFileAsync(
                string.Format(NflverseReleaseCatalog.DepthChartsUrl, season),
                $"depth_charts_{season}.csv",
                cancellationToken)
            .ConfigureAwait(false);

        var identities = await LoadWeekIdentitiesAsync(rosterPath, season, week, cancellationToken)
            .ConfigureAwait(false);
        if (identities.Count == 0)
        {
            throw new InvalidOperationException($"No weekly roster skill players found for {season} week {week}.");
        }

        var priorStats = await LoadPlayerWeekStatsAsync(
                statsPath,
                season,
                minWeekInclusive: null,
                maxWeekInclusive: week - 1,
                cancellationToken)
            .ConfigureAwait(false);
        var weekOutcomes = await LoadPlayerWeekStatsAsync(
                statsPath,
                season,
                minWeekInclusive: week,
                maxWeekInclusive: week,
                cancellationToken)
            .ConfigureAwait(false);
        var priorSnaps = await LoadPriorSnapAveragesAsync(snapsPath, week, cancellationToken)
            .ConfigureAwait(false);
        var depthRoles = await LoadDepthRolesAsync(depthPath, week: Math.Max(1, week - 1), cancellationToken)
            .ConfigureAwait(false);
        var injuries = await LoadInjuriesAsync(injuriesPath, week, cutoff, cancellationToken)
            .ConfigureAwait(false);

        var ranked = BuildRankedCandidates(identities, priorStats, scoringType);
        var rosterPlayers = SelectLabRoster(ranked);
        if (rosterPlayers.Count < 4)
        {
            throw new InvalidOperationException(
                $"Insufficient pre-week production history to build a lab roster for {season} week {week}.");
        }

        var players = new List<HistoricalRawPlayerRecord>();
        var outcomes = new List<HistoricalPlayerOutcome>();
        var rosterSlots = new List<HistoricalRosterSlot>();

        foreach (var candidate in rosterPlayers)
        {
            var id = candidate.Identity;
            priorStats.TryGetValue(id.GsisId, out var priorWeeks);
            weekOutcomes.TryGetValue(id.GsisId, out var outcomeWeeks);
            injuries.TryGetValue(id.GsisId, out var injury);
            depthRoles.TryGetValue(id.GsisId, out var role);
            priorSnaps.TryGetValue(NormalizeName(id.FullName), out var snapPct);

            var unavailable = new List<string>
            {
                "Historical pre-week projection (unavailable — no as-of projection archive in nflverse)"
            };

            var (opportunity, usage, recentProduction) = DerivePreGameSignals(
                id.Position,
                priorWeeks,
                snapPct,
                scoringType);

            if (priorWeeks is null || priorWeeks.Count == 0)
            {
                unavailable.Add("Recent production (no weeks 1..N-1 stats)");
            }

            string? healthLabel;
            string? injuryStatus = null;
            string? injuryBody = null;
            DateTimeOffset? injuryAt = null;
            if (injury is not null)
            {
                injuryStatus = injury.Status;
                injuryBody = injury.BodyPart;
                injuryAt = injury.ObservedAt;
                healthLabel = $"{injury.Status}" + (string.IsNullOrWhiteSpace(injury.BodyPart) ? "" : $" ({injury.BodyPart})");
            }
            else
            {
                healthLabel = "Healthy";
                unavailable.Add("Injury certainty beyond weekly report rows (partial)");
            }

            players.Add(new HistoricalRawPlayerRecord
            {
                PlayerId = id.PlaybookId,
                PlayerName = id.FullName,
                Position = id.Position,
                Team = id.Team,
                ProjectedPoints = null,
                Floor = null,
                Ceiling = null,
                ProjectionConfidence = null,
                ProjectionObservedAt = null,
                OpportunityScore = opportunity,
                UsageScore = usage,
                HealthLabel = healthLabel,
                InjuryStatus = injuryStatus,
                InjuryBodyPart = injuryBody,
                InjuryObservedAt = injuryAt,
                RecentNewsHeadline = null,
                RecentNewsObservedAt = null,
                RecentNewsConfirmed = false,
                RoleNote = role,
                RecentProductionScore = recentProduction,
                UnavailableSignals = unavailable
            });

            rosterSlots.Add(new HistoricalRosterSlot
            {
                PlayerId = id.PlaybookId,
                IsStarter = candidate.IsStarter
            });

            if (outcomeWeeks is { Count: > 0 })
            {
                var actual = (double)LeagueFantasyScoring.Calculate(ToCounting(outcomeWeeks[0]), scoringType);
                outcomes.Add(new HistoricalPlayerOutcome
                {
                    PlayerId = id.PlaybookId,
                    PlayerName = id.FullName,
                    ActualFantasyPoints = actual,
                    Note = $"Week {week} actuals revealed post-decision from nflverse player_stats"
                });
            }
        }

        var unavailableSources = new List<string>
        {
            "Pre-week projections: UNAVAILABLE (not fabricated)",
            "Historical fantasy league ownership: UNAVAILABLE — using reconstructed lab roster from weeks 1.." + (week - 1) + " production",
            "News archive: UNAVAILABLE",
            "Week " + week + " depth charts: unused for pre-game (no trustworthy cutoff) — week " + Math.Max(1, week - 1) + " depth used instead (PARTIAL)",
            "Week " + week + " snap counts: post-game — excluded from pre-game context"
        };

        _logger.LogInformation(
            "Built nflverse historical week {Season} W{Week} cutoff={Cutoff:u} roster={Roster} outcomes={Outcomes}",
            season,
            week,
            cutoff,
            players.Count,
            outcomes.Count);

        return new HistoricalRawWeekData
        {
            Season = season,
            Week = week,
            InformationCutoff = cutoff,
            ScoringType = scoringType,
            LeagueName = $"nflverse Replay Lab ({season} W{week})",
            LeagueId = Guid.Parse("f2018000-0000-4000-8000-000000000007"),
            SelectedRosterId = 1,
            TeamName = "Historical Replay Lab",
            Players = players,
            Roster = rosterSlots,
            OpponentRoster = [],
            Outcomes = outcomes,
            UnavailableSources = unavailableSources,
            SourceLabel = $"{ProviderKey}-{season}-w{week}"
        };
    }

    private async Task<DateTimeOffset> ResolveInformationCutoffAsync(
        string schedulesPath,
        int season,
        int week,
        CancellationToken cancellationToken)
    {
        using var reader = await _cache.OpenTextAsync(schedulesPath, cancellationToken).ConfigureAwait(false);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Schedules CSV missing header.");
        var cols = SplitCsv(header);
        var idx = Index(cols);

        DateTimeOffset? earliest = null;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = SplitCsv(line);
            if (Get(row, idx, "season") != season.ToString(CultureInfo.InvariantCulture) ||
                Get(row, idx, "week") != week.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            var gameType = Get(row, idx, "game_type");
            if (!string.IsNullOrEmpty(gameType) &&
                !string.Equals(gameType, "REG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var gameday = Get(row, idx, "gameday");
            var gametime = Get(row, idx, "gametime");
            if (string.IsNullOrWhiteSpace(gameday))
            {
                continue;
            }

            // nflverse gametime is Eastern local clock.
            var time = string.IsNullOrWhiteSpace(gametime) ? "13:00" : gametime;
            if (!DateTime.TryParse(
                    $"{gameday}T{time}:00",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var localEastern))
            {
                continue;
            }

            var eastern = new DateTimeOffset(localEastern, ResolveEasternOffset(localEastern));
            if (earliest is null || eastern < earliest)
            {
                earliest = eastern;
            }
        }

        if (earliest is null)
        {
            throw new InvalidOperationException($"No schedule rows found for {season} week {week}.");
        }

        // 20 minutes before first kickoff — honest pre-game boundary.
        return earliest.Value.AddMinutes(-20);
    }

    private async Task<Dictionary<string, HistoricalPlayerIdentity>> LoadWeekIdentitiesAsync(
        string path,
        int season,
        int week,
        CancellationToken cancellationToken)
    {
        using var reader = await _cache.OpenTextAsync(path, cancellationToken).ConfigureAwait(false);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Weekly roster CSV missing header.");
        var idx = Index(SplitCsv(header));
        var map = new Dictionary<string, HistoricalPlayerIdentity>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = SplitCsv(line);
            if (Get(row, idx, "week") != week.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            var status = Get(row, idx, "status");
            if (!string.Equals(status, "ACT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var position = Get(row, idx, "position");
            if (!HistoricalPlayerIdentityNormalizer.IsSkillPosition(position))
            {
                continue;
            }

            var gsis = Get(row, idx, "gsis_id");
            var name = Get(row, idx, "full_name");
            var team = Get(row, idx, "team");
            if (string.IsNullOrWhiteSpace(gsis) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(team))
            {
                continue;
            }

            // First row wins; duplicates with same GSIS are ignored (validated later).
            if (map.ContainsKey(gsis))
            {
                continue;
            }

            map[gsis] = _identities.Normalize(
                gsis,
                name,
                position!,
                team!,
                season,
                week,
                sleeperId: Get(row, idx, "sleeper_id"),
                espnId: Get(row, idx, "espn_id"),
                yahooId: Get(row, idx, "yahoo_id"),
                rosterStatus: status);
        }

        return map;
    }

    private async Task<Dictionary<string, List<StatWeek>>> LoadPlayerWeekStatsAsync(
        string path,
        int season,
        int? minWeekInclusive,
        int? maxWeekInclusive,
        CancellationToken cancellationToken)
    {
        using var reader = await _cache.OpenTextAsync(path, cancellationToken).ConfigureAwait(false);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("player_stats CSV missing header.");
        var idx = Index(SplitCsv(header));
        var map = new Dictionary<string, List<StatWeek>>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = SplitCsv(line);
            if (Get(row, idx, "season") != season.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            var seasonType = Get(row, idx, "season_type");
            if (!string.IsNullOrEmpty(seasonType) &&
                !string.Equals(seasonType, "REG", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!int.TryParse(Get(row, idx, "week"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w))
            {
                continue;
            }

            if (minWeekInclusive is not null && w < minWeekInclusive)
            {
                continue;
            }

            if (maxWeekInclusive is not null && w > maxWeekInclusive)
            {
                continue;
            }

            var gsis = Get(row, idx, "player_id");
            if (string.IsNullOrWhiteSpace(gsis))
            {
                continue;
            }

            var position = Get(row, idx, "position");
            if (!HistoricalPlayerIdentityNormalizer.IsSkillPosition(position))
            {
                continue;
            }

            var stat = new StatWeek(
                w,
                ParseInt(Get(row, idx, "attempts")),
                ParseInt(Get(row, idx, "completions")),
                ParseInt(Get(row, idx, "passing_yards")),
                ParseInt(Get(row, idx, "passing_tds")),
                ParseInt(Get(row, idx, "interceptions")),
                ParseInt(Get(row, idx, "carries")),
                ParseInt(Get(row, idx, "rushing_yards")),
                ParseInt(Get(row, idx, "rushing_tds")),
                ParseInt(Get(row, idx, "targets")),
                ParseInt(Get(row, idx, "receptions")),
                ParseInt(Get(row, idx, "receiving_yards")),
                ParseInt(Get(row, idx, "receiving_tds")));

            if (!map.TryGetValue(gsis, out var list))
            {
                list = [];
                map[gsis] = list;
            }

            list.Add(stat);
        }

        return map;
    }

    private async Task<Dictionary<string, double>> LoadPriorSnapAveragesAsync(
        string path,
        int week,
        CancellationToken cancellationToken)
    {
        using var reader = await _cache.OpenTextAsync(path, cancellationToken).ConfigureAwait(false);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("snap_counts CSV missing header.");
        var idx = Index(SplitCsv(header));
        var sums = new Dictionary<string, (double Sum, int N)>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = SplitCsv(line);
            if (!int.TryParse(Get(row, idx, "week"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
                w >= week)
            {
                continue;
            }

            var name = Get(row, idx, "player");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!double.TryParse(Get(row, idx, "offense_pct"), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            {
                continue;
            }

            // offense_pct in nflverse is 0-1 fraction.
            var key = NormalizeName(name);
            if (!sums.TryGetValue(key, out var acc))
            {
                acc = (0, 0);
            }

            sums[key] = (acc.Sum + pct, acc.N + 1);
        }

        return sums.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.N == 0 ? 0 : kv.Value.Sum / kv.Value.N,
            StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, string>> LoadDepthRolesAsync(
        string path,
        int week,
        CancellationToken cancellationToken)
    {
        using var reader = await _cache.OpenTextAsync(path, cancellationToken).ConfigureAwait(false);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("depth_charts CSV missing header.");
        var idx = Index(SplitCsv(header));
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = SplitCsv(line);
            if (Get(row, idx, "week") != week.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            var formation = Get(row, idx, "formation");
            if (!string.Equals(formation, "Offense", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var gsis = Get(row, idx, "gsis_id");
            var depthPos = Get(row, idx, "depth_position");
            var depthTeam = Get(row, idx, "depth_team");
            if (string.IsNullOrWhiteSpace(gsis) || string.IsNullOrWhiteSpace(depthPos))
            {
                continue;
            }

            if (map.ContainsKey(gsis))
            {
                continue;
            }

            map[gsis] = string.IsNullOrWhiteSpace(depthTeam)
                ? $"{depthPos} depth"
                : $"{depthPos}{depthTeam} depth role (week {week} chart)";
        }

        return map;
    }

    private async Task<Dictionary<string, InjuryRow>> LoadInjuriesAsync(
        string path,
        int week,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        using var reader = await _cache.OpenTextAsync(path, cancellationToken).ConfigureAwait(false);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("injuries CSV missing header.");
        var idx = Index(SplitCsv(header));
        var map = new Dictionary<string, InjuryRow>(StringComparer.OrdinalIgnoreCase);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = SplitCsv(line);
            if (Get(row, idx, "week") != week.ToString(CultureInfo.InvariantCulture))
            {
                continue;
            }

            var gsis = Get(row, idx, "gsis_id");
            var status = Get(row, idx, "report_status");
            if (string.IsNullOrWhiteSpace(gsis) || string.IsNullOrWhiteSpace(status))
            {
                continue;
            }

            var modifiedRaw = Get(row, idx, "date_modified");
            if (!DateTimeOffset.TryParse(modifiedRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var modified))
            {
                // Without a timestamp we cannot prove pre-game knowledge — skip.
                continue;
            }

            if (modified > cutoff)
            {
                continue;
            }

            var body = Get(row, idx, "report_primary_injury");
            if (!map.ContainsKey(gsis))
            {
                map[gsis] = new InjuryRow(status!, body, modified);
            }
        }

        return map;
    }

    private static List<RosterCandidate> BuildRankedCandidates(
        Dictionary<string, HistoricalPlayerIdentity> identities,
        Dictionary<string, List<StatWeek>> priorStats,
        ScoringType scoring)
    {
        var list = new List<RosterCandidate>();
        foreach (var identity in identities.Values)
        {
            priorStats.TryGetValue(identity.GsisId, out var weeks);
            var avg = weeks is { Count: > 0 }
                ? weeks.Average(w => (double)LeagueFantasyScoring.Calculate(ToCounting(w), scoring))
                : 0d;
            list.Add(new RosterCandidate(identity, avg, IsStarter: false));
        }

        return list
            .OrderByDescending(c => c.PriorAveragePoints)
            .ThenBy(c => c.Identity.GsisId, StringComparer.Ordinal)
            .ToList();
    }

    private static List<RosterCandidate> SelectLabRoster(IReadOnlyList<RosterCandidate> ranked)
    {
        // Deterministic reconstructed fantasy roster — NOT historical league ownership.
        var selected = new List<RosterCandidate>();
        void Take(Position pos, int starters, int total)
        {
            var pool = ranked.Where(c => c.Identity.Position == pos).Take(total).ToList();
            for (var i = 0; i < pool.Count; i++)
            {
                selected.Add(pool[i] with { IsStarter = i < starters });
            }
        }

        Take(Position.QB, starters: 1, total: 2);
        Take(Position.RB, starters: 2, total: 4);
        Take(Position.WR, starters: 2, total: 5);
        Take(Position.TE, starters: 1, total: 2);
        return selected
            .OrderBy(c => c.Identity.Position)
            .ThenByDescending(c => c.IsStarter)
            .ThenBy(c => c.Identity.GsisId, StringComparer.Ordinal)
            .ToList();
    }

    private static (int? Opportunity, int? Usage, int? RecentProduction) DerivePreGameSignals(
        Position position,
        List<StatWeek>? priorWeeks,
        double? avgOffenseSnapPct,
        ScoringType scoring)
    {
        if (priorWeeks is null || priorWeeks.Count == 0)
        {
            return (null, null, null);
        }

        var avgPoints = priorWeeks.Average(w => (double)LeagueFantasyScoring.Calculate(ToCounting(w), scoring));
        var recent = (int)Math.Clamp(Math.Round(avgPoints * 5.0), 0, 100);

        double oppRaw = position switch
        {
            Position.QB => priorWeeks.Average(w => w.PassAttempts ?? 0),
            Position.RB => priorWeeks.Average(w => (w.RushAttempts ?? 0) + (w.Targets ?? 0)),
            Position.WR or Position.TE => priorWeeks.Average(w => w.Targets ?? 0),
            _ => 0
        };

        var opportunity = position switch
        {
            Position.QB => Scale(oppRaw, 20, 40),
            Position.RB => Scale(oppRaw, 8, 22),
            Position.WR => Scale(oppRaw, 4, 12),
            Position.TE => Scale(oppRaw, 3, 9),
            _ => 50
        };

        var usage = avgOffenseSnapPct is double pct
            ? (int)Math.Clamp(Math.Round(pct * 100.0), 0, 100)
            : opportunity;

        return (opportunity, usage, recent);
    }

    private static int Scale(double value, double low, double high)
    {
        if (high <= low)
        {
            return 50;
        }

        var t = (value - low) / (high - low);
        return (int)Math.Clamp(Math.Round(35 + t * 50), 5, 95);
    }

    private static CanonicalCountingStats ToCounting(StatWeek w) =>
        new()
        {
            PassAttempts = w.PassAttempts,
            PassCompletions = w.PassCompletions,
            PassYards = w.PassYards,
            PassTouchdowns = w.PassTouchdowns,
            PassInterceptions = w.PassInterceptions,
            RushAttempts = w.RushAttempts,
            RushYards = w.RushYards,
            RushTouchdowns = w.RushTouchdowns,
            Targets = w.Targets,
            Receptions = w.Receptions,
            ReceivingYards = w.ReceivingYards,
            ReceivingTouchdowns = w.ReceivingTouchdowns
        };

    private static TimeSpan ResolveEasternOffset(DateTime localEastern)
    {
        // US Eastern: EDT (UTC-4) second Sun Mar → first Sun Nov; else EST (UTC-5).
        // Sufficient for NFL regular-season kickoffs.
        var year = localEastern.Year;
        var dstStart = NthWeekdayOfMonth(year, 3, DayOfWeek.Sunday, 2);
        var dstEnd = NthWeekdayOfMonth(year, 11, DayOfWeek.Sunday, 1);
        var isDst = localEastern >= dstStart && localEastern < dstEnd;
        return isDst ? TimeSpan.FromHours(-4) : TimeSpan.FromHours(-5);
    }

    private static DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek day, int n)
    {
        var dt = new DateTime(year, month, 1);
        var count = 0;
        while (true)
        {
            if (dt.DayOfWeek == day)
            {
                count++;
                if (count == n)
                {
                    return dt;
                }
            }

            dt = dt.AddDays(1);
        }
    }

    private static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int? ParseInt(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static Dictionary<string, int> Index(IReadOnlyList<string> cols)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cols.Count; i++)
        {
            map[cols[i]] = i;
        }

        return map;
    }

    private static string? Get(IReadOnlyList<string> row, Dictionary<string, int> idx, string col) =>
        idx.TryGetValue(col, out var i) && i < row.Count ? row[i] : null;

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }

    private sealed record StatWeek(
        int Week,
        int? PassAttempts,
        int? PassCompletions,
        int? PassYards,
        int? PassTouchdowns,
        int? PassInterceptions,
        int? RushAttempts,
        int? RushYards,
        int? RushTouchdowns,
        int? Targets,
        int? Receptions,
        int? ReceivingYards,
        int? ReceivingTouchdowns);

    private sealed record InjuryRow(string Status, string? BodyPart, DateTimeOffset ObservedAt);

    private sealed record RosterCandidate(HistoricalPlayerIdentity Identity, double PriorAveragePoints, bool IsStarter);
}
