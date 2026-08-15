using Microsoft.Extensions.Logging;
using Playbook.Application.Draft;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.Players;
using Playbook.Application.Projections.Interfaces;
using Playbook.Core.Draft;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Players;

namespace Playbook.Infrastructure.Draft;

/// <summary>
/// Live Sleeper draft board + pick recommendations for the currently connected league. Reuses
/// existing real services only (IPlayerService, IProjectionService, IPlayerInjuryService,
/// ILeagueState) — no parallel valuation engine. Read-only against Sleeper: never writes a pick.
/// Deliberately keeps two ranks per candidate ("best player available" vs "best fit for this
/// team right now") so roster construction and positional scarcity can override raw projection
/// rank without hiding what the raw rank actually was.
/// </summary>
public sealed class DraftAssistantService : IDraftAssistantService
{
    private const int MaxAlternatives = 5;

    // Roster-construction / scarcity weighting — deliberately separate, smaller-scoped constants
    // from DropPickupService's keep/drop valuation (different question: "who to draft next" vs
    // "who to keep/drop on an existing roster").
    private const decimal ReplacementValueWeight = 0.5m;
    private const decimal NeedUrgentBonus = 3.0m;
    private const decimal NeedSatisfiedPenalty = -2.0m;
    private const decimal DynastyYoungBonusPerYearUnder27 = 0.15m;
    private const decimal DynastyOldPenaltyPerYearOver29 = 0.15m;
    private const decimal SevereInjuryPenalty = -4.0m;
    private const decimal ModerateInjuryPenalty = -1.5m;

    private static readonly IReadOnlyList<string> KnownUnavailableSignals =
    [
        "Bye-week collision across your roster is not factored in (not available).",
        "Strength of schedule / fantasy playoff schedule is not factored in (not available).",
        "Market ADP / where this player is typically being drafted is not factored in (not available).",
        "Trade value is not factored in (not available)."
    ];

    private readonly ILeagueState _leagueState;
    private readonly ISleeperLeagueClient _sleeper;
    private readonly IPlayerService _players;
    private readonly IProjectionService _projections;
    private readonly IPlayerInjuryService _injuries;
    private readonly ILogger<DraftAssistantService> _logger;

    public DraftAssistantService(
        ILeagueState leagueState,
        ISleeperLeagueClient sleeper,
        IPlayerService players,
        IProjectionService projections,
        IPlayerInjuryService injuries,
        ILogger<DraftAssistantService> logger)
    {
        _leagueState = leagueState;
        _sleeper = sleeper;
        _players = players;
        _projections = projections;
        _injuries = injuries;
        _logger = logger;
    }

    public async Task<DraftAssistantReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var league = _leagueState.CurrentLeague;
        var team = _leagueState.CurrentUserTeam;

        if (league is null || league.DataSource != LeagueDataSource.Sleeper ||
            string.IsNullOrWhiteSpace(league.ExternalId))
        {
            return Empty(now, "Connect a live Sleeper league to use the Draft Assistant.");
        }

        SleeperDraftSummary? draftSummary;
        try
        {
            var drafts = await _sleeper.GetDraftsForLeagueAsync(league.ExternalId, cancellationToken)
                .ConfigureAwait(false);
            draftSummary = drafts.OrderByDescending(d => d.StartTime ?? 0).FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Draft Assistant: failed to list drafts for league {LeagueId}", league.ExternalId);
            return Empty(now, "Could not reach Sleeper to look up this league's draft.", isStale: true);
        }

        if (draftSummary is null)
        {
            return Empty(now, "No draft found for this league yet.");
        }

        SleeperDraftSnapshot? draft;
        IReadOnlyList<SleeperDraftPickSnapshot> picks;
        SleeperLeagueSnapshot? leagueSnapshot;
        try
        {
            var draftTask = _sleeper.GetDraftAsync(draftSummary.DraftId, cancellationToken);
            var picksTask = _sleeper.GetDraftPicksAsync(draftSummary.DraftId, cancellationToken);
            var leagueSnapshotTask = _sleeper.GetLeagueSnapshotAsync(league.ExternalId, cancellationToken);
            await Task.WhenAll(draftTask, picksTask, leagueSnapshotTask).ConfigureAwait(false);
            draft = draftTask.Result;
            picks = picksTask.Result;
            leagueSnapshot = leagueSnapshotTask.Result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Draft Assistant: failed to fetch live draft {DraftId}", draftSummary.DraftId);
            return Empty(now, "Could not reach Sleeper for live draft data.", isStale: true);
        }

        if (draft is null)
        {
            return Empty(now, "Draft data unavailable from Sleeper.", isStale: true);
        }

        var slotToRosterId = BuildSlotToRosterId(draft, leagueSnapshot);
        var pickRecords = picks
            .Select(p => MapPick(p, slotToRosterId))
            .OrderBy(p => p.PickNumber)
            .ToList();

        var status = ParseStatus(draft.Status);
        var isSnake = string.Equals(draft.Type, "snake", StringComparison.OrdinalIgnoreCase);
        var nextPickNumber = pickRecords.Count + 1;
        var nextSlot = DraftOrderCalculator.SlotForPick(nextPickNumber, draft.Teams, isSnake);
        var nextRosterId = slotToRosterId.GetValueOrDefault(nextSlot);

        var board = new DraftBoard
        {
            DraftId = draft.DraftId,
            LeagueId = league.Id,
            Season = draft.Season,
            Status = status,
            Type = draft.Type,
            TotalRounds = draft.Rounds,
            TeamCount = draft.Teams,
            Picks = pickRecords,
            NextPickNumber = nextPickNumber,
            NextRosterId = nextRosterId == 0 ? null : nextRosterId,
            UserRosterId = team?.RosterId,
            RetrievedAt = now
        };

        if (board.IsComplete)
        {
            return new DraftAssistantReport
            {
                Board = board,
                IsOnTheClock = false,
                Recommended = null,
                Alternatives = [],
                RosterNeeds = [],
                StatusMessage = "This draft is complete.",
                UnavailableSignals = KnownUnavailableSignals,
                IsStale = false,
                GeneratedAt = now
            };
        }

        if (status != DraftStatus.Drafting)
        {
            return new DraftAssistantReport
            {
                Board = board,
                IsOnTheClock = false,
                Recommended = null,
                Alternatives = [],
                RosterNeeds = [],
                StatusMessage = status == DraftStatus.NotStarted
                    ? "Draft has not started yet."
                    : "Draft is paused.",
                UnavailableSignals = KnownUnavailableSignals,
                IsStale = false,
                GeneratedAt = now
            };
        }

        var allPlayers = _players.GetAllPlayers().ToDictionary(p => p.Id);
        var draftedIds = pickRecords
            .Where(p => p.PlayerId is not null)
            .Select(p => p.PlayerId!.Value)
            .ToHashSet();
        var projectionByPlayer = _projections.GetAllProjections().ToDictionary(p => p.PlayerId);

        var userRosterId = team?.RosterId;
        var userDraftedPlayers = userRosterId is int urid
            ? pickRecords
                .Where(p => p.RosterId == urid && p.PlayerId is Guid pid && allPlayers.ContainsKey(pid))
                .Select(p => allPlayers[p.PlayerId!.Value])
                .ToList()
            : [];

        var rosterNeeds = BuildRosterNeeds(league, userDraftedPlayers);
        var needByPosition = rosterNeeds.ToDictionary(n => n.PositionLabel, n => n.NeedLevel);

        var undrafted = allPlayers.Values
            .Where(p => !draftedIds.Contains(p.Id))
            .Where(p => p.Position is Position.QB or Position.RB or Position.WR or Position.TE)
            .ToList();

        var replacementLevelByPosition = ComputeReplacementLevels(undrafted, projectionByPlayer, league);
        var isDynasty = league.LeagueType == LeagueType.Dynasty;

        var scored = undrafted
            .Select(p => ScorePlayer(
                p,
                projectionByPlayer.GetValueOrDefault(p.Id),
                replacementLevelByPosition,
                needByPosition,
                isDynasty,
                _injuries.GetCurrentInjury(p.Id)))
            .Where(s => s.Projection is not null)
            .ToList();

        var byRawProjection = scored.OrderByDescending(s => s.Projection!.Value).ToList();
        for (var i = 0; i < byRawProjection.Count; i++)
        {
            byRawProjection[i].BestPlayerAvailableRank = i + 1;
        }

        var byTeamFit = scored.OrderByDescending(s => s.TeamFitScore).ToList();
        for (var i = 0; i < byTeamFit.Count; i++)
        {
            byTeamFit[i].TeamFitRank = i + 1;
        }

        var recommendations = byTeamFit.Take(1 + MaxAlternatives).Select(BuildRecommendation).ToList();
        var isOnTheClock = board.IsUserOnTheClock;

        return new DraftAssistantReport
        {
            Board = board,
            IsOnTheClock = isOnTheClock,
            Recommended = recommendations.FirstOrDefault(),
            Alternatives = recommendations.Skip(1).ToList(),
            RosterNeeds = rosterNeeds,
            StatusMessage = isOnTheClock
                ? "You're on the clock."
                : board.NextRosterId is null
                    ? $"Pick {board.NextPickNumber} is up next (roster unresolved)."
                    : $"Pick {board.NextPickNumber} is on the clock (not your team).",
            UnavailableSignals = KnownUnavailableSignals,
            IsStale = false,
            GeneratedAt = now
        };
    }

    internal static ScoredCandidate ScorePlayer(
        Player player,
        PlayerProjection? projection,
        IReadOnlyDictionary<Position, decimal> replacementLevelByPosition,
        IReadOnlyDictionary<string, PositionalNeedLevel> needByPosition,
        bool isDynasty,
        PlayerInjuryRecord? currentInjury)
    {
        var positionLabel = PlayerPresentation.PositionLabel(player.Position);
        var factors = new List<DraftRecommendationFactor>();

        if (projection is null)
        {
            factors.Add(new DraftRecommendationFactor
            {
                Label = "Projected production",
                Detail = "No current projection available for this player.",
                Direction = FactorDirection.Neutral,
                Available = false
            });

            return new ScoredCandidate
            {
                Player = player,
                Projection = null,
                ProjectionConfidence = null,
                ValueOverReplacement = null,
                TeamFitScore = decimal.MinValue,
                Factors = factors,
                Reasoning = $"{player.FullName}: no current projection on file — cannot be responsibly recommended."
            };
        }

        var projectedPoints = projection.ProjectedFantasyPoints;
        var confidence = projection.Confidence;
        var fitScore = projectedPoints;

        factors.Add(new DraftRecommendationFactor
        {
            Label = "Projected production",
            Detail = $"{projectedPoints:0.0} pts/wk (confidence {confidence}%)",
            Direction = FactorDirection.Positive,
            Available = true
        });

        decimal? valueOverReplacement = null;
        if (replacementLevelByPosition.TryGetValue(player.Position, out var replacementLevel))
        {
            valueOverReplacement = Math.Round(projectedPoints - replacementLevel, 1);
            fitScore += valueOverReplacement.Value * ReplacementValueWeight;
            factors.Add(new DraftRecommendationFactor
            {
                Label = "Positional scarcity",
                Detail = valueOverReplacement >= 0
                    ? $"{valueOverReplacement:0.0} pts above replacement level at {positionLabel}"
                    : $"{Math.Abs(valueOverReplacement.Value):0.0} pts below replacement level at {positionLabel}",
                Direction = valueOverReplacement >= 0 ? FactorDirection.Positive : FactorDirection.Negative,
                Available = true
            });
        }

        var need = needByPosition.GetValueOrDefault(positionLabel, PositionalNeedLevel.Moderate);
        if (needByPosition.ContainsKey(positionLabel))
        {
            var needAdjustment = need switch
            {
                PositionalNeedLevel.Urgent => NeedUrgentBonus,
                PositionalNeedLevel.Satisfied => NeedSatisfiedPenalty,
                _ => 0m
            };
            fitScore += needAdjustment;
            if (needAdjustment != 0m)
            {
                factors.Add(new DraftRecommendationFactor
                {
                    Label = "Roster construction",
                    Detail = need == PositionalNeedLevel.Urgent
                        ? $"Your roster still needs {positionLabel} depth."
                        : $"Your roster already has sufficient {positionLabel} depth.",
                    Direction = need == PositionalNeedLevel.Urgent ? FactorDirection.Positive : FactorDirection.Negative,
                    Available = true
                });
            }
        }

        if (isDynasty)
        {
            if (player.Age is int age)
            {
                var ageAdjustment = age < 27
                    ? (27 - age) * DynastyYoungBonusPerYearUnder27
                    : age > 29
                        ? -(age - 29) * DynastyOldPenaltyPerYearOver29
                        : 0m;
                fitScore += ageAdjustment;
                if (ageAdjustment != 0m)
                {
                    factors.Add(new DraftRecommendationFactor
                    {
                        Label = "Dynasty age curve",
                        Detail = $"Age {age}",
                        Direction = ageAdjustment > 0 ? FactorDirection.Positive : FactorDirection.Negative,
                        Available = true
                    });
                }
            }
            else
            {
                factors.Add(new DraftRecommendationFactor
                {
                    Label = "Dynasty age curve",
                    Detail = "Age unknown.",
                    Direction = FactorDirection.Neutral,
                    Available = false
                });
            }
        }

        var hasLimitingInjury = currentInjury is not null &&
            !string.Equals(currentInjury.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentInjury.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
        if (hasLimitingInjury)
        {
            var isSevere = currentInjury!.Severity is InjurySeverity.Significant or InjurySeverity.Major;
            fitScore += isSevere ? SevereInjuryPenalty : ModerateInjuryPenalty;
            confidence = Math.Max(10, confidence - (isSevere ? 15 : 6));
            factors.Add(new DraftRecommendationFactor
            {
                Label = "Injury risk",
                Detail = $"{currentInjury.Status}" +
                         (string.IsNullOrWhiteSpace(currentInjury.BodyPart) ? "" : $" ({currentInjury.BodyPart})"),
                Direction = FactorDirection.Negative,
                Available = true
            });
        }
        else
        {
            factors.Add(new DraftRecommendationFactor
            {
                Label = "Injury risk",
                Detail = "No verified current injury designation.",
                Direction = FactorDirection.Neutral,
                Available = true
            });
        }

        var reasoning = BuildReasoning(player, positionLabel, projectedPoints, valueOverReplacement, need, needByPosition.ContainsKey(positionLabel));

        return new ScoredCandidate
        {
            Player = player,
            Projection = projectedPoints,
            ProjectionConfidence = confidence,
            ValueOverReplacement = valueOverReplacement,
            TeamFitScore = fitScore,
            Factors = factors,
            Reasoning = reasoning
        };
    }

    private static string BuildReasoning(
        Player player,
        string positionLabel,
        decimal projectedPoints,
        decimal? valueOverReplacement,
        PositionalNeedLevel need,
        bool needKnown)
    {
        var core = $"{player.FullName} projects at {projectedPoints:0.0} pts/wk at {positionLabel}.";

        if (valueOverReplacement is { } vor)
        {
            if (vor >= 3m)
            {
                core += $" That's a meaningful step above the next tier remaining at {positionLabel}.";
            }
            else if (vor <= -3m)
            {
                core += $" Production here is already below replacement level at {positionLabel}.";
            }
        }

        if (needKnown)
        {
            core += need switch
            {
                PositionalNeedLevel.Urgent => $" Your roster still needs {positionLabel} depth.",
                PositionalNeedLevel.Satisfied =>
                    $" Your roster already has sufficient {positionLabel} depth, so this is about value more than need.",
                _ => string.Empty
            };
        }

        return core;
    }

    private static DraftRecommendation BuildRecommendation(ScoredCandidate c) => new()
    {
        PlayerId = c.Player.Id,
        PlayerName = c.Player.FullName,
        PositionLabel = PlayerPresentation.PositionLabel(c.Player.Position),
        Team = c.Player.Team,
        ProjectedPoints = c.Projection,
        ValueOverReplacement = c.ValueOverReplacement,
        BestPlayerAvailableRank = c.BestPlayerAvailableRank,
        TeamFitRank = c.TeamFitRank,
        Confidence = c.ProjectionConfidence ?? 20,
        Reasoning = c.Reasoning,
        Factors = c.Factors
    };

    internal static IReadOnlyDictionary<Position, decimal> ComputeReplacementLevels(
        IReadOnlyList<Player> undraftedPlayers,
        IReadOnlyDictionary<Guid, PlayerProjection> projectionByPlayer,
        League league)
    {
        var result = new Dictionary<Position, decimal>();
        foreach (var group in undraftedPlayers.GroupBy(p => p.Position))
        {
            var ranked = group
                .Select(p => projectionByPlayer.GetValueOrDefault(p.Id)?.ProjectedFantasyPoints)
                .Where(v => v is not null)
                .Select(v => v!.Value)
                .OrderByDescending(v => v)
                .ToList();

            if (ranked.Count == 0)
            {
                continue;
            }

            var starterSlots = CountPositionSlots(league.RosterPositions, group.Key);
            var replacementIndex = Math.Clamp((starterSlots * Math.Max(1, league.NumberOfTeams)) - 1, 0, ranked.Count - 1);
            result[group.Key] = ranked[replacementIndex];
        }

        return result;
    }

    internal static int CountPositionSlots(IReadOnlyList<string> rosterPositions, Position position)
    {
        var label = position.ToString();
        var direct = rosterPositions.Count(p => string.Equals(p, label, StringComparison.OrdinalIgnoreCase));
        var flexShare = position is Position.RB or Position.WR or Position.TE
            ? rosterPositions.Count(p => p.Contains("FLEX", StringComparison.OrdinalIgnoreCase)) * 0.5
            : 0;
        return Math.Max(1, direct + (int)Math.Round(flexShare, MidpointRounding.AwayFromZero));
    }

    internal static IReadOnlyList<PositionalNeed> BuildRosterNeeds(
        League league, IReadOnlyList<Player> userDraftedPlayers)
    {
        var needs = new List<PositionalNeed>();
        foreach (var position in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            var target = CountPositionSlots(league.RosterPositions, position);
            var current = userDraftedPlayers.Count(p => p.Position == position);
            var level = current < target
                ? PositionalNeedLevel.Urgent
                : current == target
                    ? PositionalNeedLevel.Moderate
                    : PositionalNeedLevel.Satisfied;

            needs.Add(new PositionalNeed
            {
                PositionLabel = PlayerPresentation.PositionLabel(position),
                CurrentCount = current,
                TargetStarters = target,
                NeedLevel = level
            });
        }

        return needs;
    }

    internal static IReadOnlyDictionary<int, int> BuildSlotToRosterId(
        SleeperDraftSnapshot draft, SleeperLeagueSnapshot? leagueSnapshot)
    {
        var result = new Dictionary<int, int>();
        if (leagueSnapshot is null)
        {
            return result;
        }

        var ownerToRoster = leagueSnapshot.Rosters
            .Where(r => !string.IsNullOrWhiteSpace(r.OwnerId))
            .ToDictionary(r => r.OwnerId!, r => r.RosterId, StringComparer.Ordinal);

        foreach (var (userId, slot) in draft.DraftOrderByUserId)
        {
            if (ownerToRoster.TryGetValue(userId, out var rosterId))
            {
                result[slot] = rosterId;
            }
        }

        return result;
    }

    private DraftPickRecord MapPick(SleeperDraftPickSnapshot p, IReadOnlyDictionary<int, int> slotToRosterId)
    {
        Guid? playerId = null;
        string? playerName = null;
        string? positionLabel = null;

        if (!string.IsNullOrWhiteSpace(p.SleeperPlayerId))
        {
            var candidateId = SleeperPlayerIds.ToPlaybookId(p.SleeperPlayerId);
            var player = _players.GetPlayer(candidateId);
            if (player is not null)
            {
                playerId = player.Id;
                playerName = player.FullName;
                positionLabel = PlayerPresentation.PositionLabel(player.Position);
            }
        }

        var rosterId = p.RosterId ?? slotToRosterId.GetValueOrDefault(p.DraftSlot);

        return new DraftPickRecord
        {
            PickNumber = p.PickNumber,
            Round = p.Round,
            DraftSlot = p.DraftSlot,
            RosterId = rosterId == 0 ? null : rosterId,
            PlayerId = playerId,
            PlayerName = playerName,
            PositionLabel = positionLabel,
            IsKeeper = p.IsKeeper
        };
    }

    internal static DraftStatus ParseStatus(string status) => status.ToLowerInvariant() switch
    {
        "pre_draft" => DraftStatus.NotStarted,
        "drafting" => DraftStatus.Drafting,
        "paused" => DraftStatus.Paused,
        "complete" => DraftStatus.Complete,
        _ => DraftStatus.Unknown
    };

    private static DraftAssistantReport Empty(DateTimeOffset now, string message, bool isStale = false) => new()
    {
        Board = null,
        IsOnTheClock = false,
        Recommended = null,
        Alternatives = [],
        RosterNeeds = [],
        StatusMessage = message,
        UnavailableSignals = [],
        IsStale = isStale,
        GeneratedAt = now
    };

    internal sealed class ScoredCandidate
    {
        public required Player Player { get; init; }
        public required decimal? Projection { get; init; }
        public required int? ProjectionConfidence { get; init; }
        public required decimal? ValueOverReplacement { get; init; }
        public required decimal TeamFitScore { get; init; }
        public required IReadOnlyList<DraftRecommendationFactor> Factors { get; init; }
        public required string Reasoning { get; init; }
        public int BestPlayerAvailableRank { get; set; }
        public int TeamFitRank { get; set; }
    }
}
