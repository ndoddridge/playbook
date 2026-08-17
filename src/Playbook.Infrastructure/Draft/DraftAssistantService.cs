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
    private readonly IByeWeekProvider _byeWeeks;
    private readonly ILogger<DraftAssistantService> _logger;

    public DraftAssistantService(
        ILeagueState leagueState,
        ISleeperLeagueClient sleeper,
        IPlayerService players,
        IProjectionService projections,
        IPlayerInjuryService injuries,
        IByeWeekProvider byeWeeks,
        ILogger<DraftAssistantService> logger)
    {
        _leagueState = leagueState;
        _sleeper = sleeper;
        _players = players;
        _projections = projections;
        _injuries = injuries;
        _byeWeeks = byeWeeks;
        _logger = logger;
    }

    /// <summary>
    /// Selected dynasty posture, held per league for the life of the session. Redraft leagues
    /// ignore it entirely — every redraft pick is a win-now pick.
    /// </summary>
    private readonly Dictionary<Guid, DynastyStrategy> _strategyByLeague = [];

    public DynastyStrategy GetStrategy(Guid leagueId) =>
        _strategyByLeague.TryGetValue(leagueId, out var s) ? s : DynastyStrategy.Hybrid;

    public void SetStrategy(Guid leagueId, DynastyStrategy strategy) =>
        _strategyByLeague[leagueId] = strategy;

    /// <summary>Draft id the user explicitly asked to follow, if any.</summary>
    private string? _attachedDraftId;

    public string? AttachedDraftId => _attachedDraftId;

    public bool AttachDraft(string draftUrlOrId)
    {
        if (!SleeperDraftLink.TryParse(draftUrlOrId, out var draftId))
        {
            return false;
        }

        _attachedDraftId = draftId;
        return true;
    }

    public void DetachDraft() => _attachedDraftId = null;

    public async Task<DraftAssistantReport> GetReportAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var league = _leagueState.CurrentLeague;
        var team = _leagueState.CurrentUserTeam;
        var attachedDraftId = _attachedDraftId;

        // A directly-attached draft does not require a connected league — that is the whole
        // point of it. Only the league-resolution path needs one.
        if (attachedDraftId is null &&
            (league is null || league.DataSource != LeagueDataSource.Sleeper ||
             string.IsNullOrWhiteSpace(league.ExternalId)))
        {
            return Empty(
                now,
                "Connect a live Sleeper league, or paste a Sleeper draft link, to use the Draft Assistant.");
        }

        string resolvedDraftId;
        if (attachedDraftId is not null)
        {
            resolvedDraftId = attachedDraftId;
        }
        else
        {
            SleeperDraftSummary? draftSummary;
            try
            {
                var drafts = await _sleeper.GetDraftsForLeagueAsync(league!.ExternalId!, cancellationToken)
                    .ConfigureAwait(false);
                draftSummary = drafts.OrderByDescending(d => d.StartTime ?? 0).FirstOrDefault();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "Draft Assistant: failed to list drafts for league {LeagueId}", league!.ExternalId);
                return Empty(now, "Could not reach Sleeper to look up this league's draft.", isStale: true);
            }

            if (draftSummary is null)
            {
                return Empty(now, "No draft found for this league yet.");
            }

            resolvedDraftId = draftSummary.DraftId;
        }

        SleeperDraftSnapshot? draft;
        IReadOnlyList<SleeperDraftPickSnapshot> picks;
        SleeperLeagueSnapshot? leagueSnapshot;
        try
        {
            var draftTask = _sleeper.GetDraftAsync(resolvedDraftId, cancellationToken);
            var picksTask = _sleeper.GetDraftPicksAsync(resolvedDraftId, cancellationToken);
            // The league snapshot is only used to cross-reference roster ownership. An attached
            // draft carries its own slot_to_roster_id, so it is skipped when no league applies.
            var leagueSnapshotTask = league is not null && !string.IsNullOrWhiteSpace(league.ExternalId)
                ? _sleeper.GetLeagueSnapshotAsync(league.ExternalId, cancellationToken)
                : Task.FromResult<SleeperLeagueSnapshot?>(null);
            await Task.WhenAll(draftTask, picksTask, leagueSnapshotTask).ConfigureAwait(false);
            draft = draftTask.Result;
            picks = picksTask.Result;
            leagueSnapshot = leagueSnapshotTask.Result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Draft Assistant: failed to fetch live draft {DraftId}", resolvedDraftId);
            return Empty(now, "Could not reach Sleeper for live draft data.", isStale: true);
        }

        if (draft is null)
        {
            return Empty(now, "Draft data unavailable from Sleeper.", isStale: true);
        }

        // An attached draft may belong to a league the user has not connected (which is exactly
        // what happens with a Sleeper mock). The draft object is self-describing — teams, rounds,
        // roster slots, scoring and league type all live on it — so an effective League is built
        // from the draft itself rather than requiring a connection. When the attached draft DOES
        // belong to the connected league, the real league wins so the user's own roster is known.
        var draftBelongsToConnectedLeague =
            league is not null &&
            !string.IsNullOrWhiteSpace(draft.LeagueId) &&
            string.Equals(league.ExternalId, draft.LeagueId, StringComparison.Ordinal);

        var effectiveLeague = draftBelongsToConnectedLeague || attachedDraftId is null
            ? league
            : BuildLeagueFromDraft(draft);

        if (effectiveLeague is null)
        {
            return Empty(now, "Could not determine league settings for this draft.", isStale: true);
        }

        // The user's roster in an attached draft is resolved from the draft's own order, using the
        // Sleeper user id taken from a connected league's roster ownership. Never guessed.
        var effectiveTeamRosterId = draftBelongsToConnectedLeague || attachedDraftId is null
            ? team?.RosterId
            : ResolveAttachedUserRosterId(draft, leagueSnapshot, league);

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
            LeagueId = effectiveLeague.Id,
            Season = draft.Season,
            Status = status,
            Type = draft.Type,
            TotalRounds = draft.Rounds,
            TeamCount = draft.Teams,
            Picks = pickRecords,
            NextPickNumber = nextPickNumber,
            NextRosterId = nextRosterId == 0 ? null : nextRosterId,
            UserRosterId = effectiveTeamRosterId,
            RetrievedAt = now
        };

        // Computed once, right after the board exists, so every return path below — including
        // the early-exit states — reports the league's real type/strategy/phase instead of
        // silently defaulting to Redraft/Hybrid/Early. A dynasty league whose draft hasn't
        // started, is paused, or is already complete must still show as dynasty.
        var isDynasty = effectiveLeague.LeagueType == LeagueType.Dynasty;
        var strategy = isDynasty ? GetStrategy(effectiveLeague.Id) : DynastyStrategy.Hybrid;
        var draftPhase = DraftPhasePolicy.ClassifyFromPick(
            board.NextPickNumber, board.TeamCount, board.TotalRounds);

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
                GeneratedAt = now,
                IsDynasty = isDynasty,
                Strategy = strategy,
                LeagueId = effectiveLeague.Id,
                Phase = draftPhase
            };
        }

        // NOTE: a non-Drafting status is deliberately NOT a reason to withhold recommendations.
        // Sleeper reports mock drafts as "paused" even while picks are actively being made — this
        // was observed live on a real mock (pick count advancing while status stayed "paused"),
        // and short-circuiting here made the Draft Assistant useless for exactly the mock-draft
        // case it is most needed for. A pre-draft board is likewise worth recommending against,
        // so the user can prepare before pick 1.01.
        //
        // Only a COMPLETE draft has nothing left to recommend, and that is handled above.

        var allPlayers = _players.GetAllPlayers().ToDictionary(p => p.Id);
        var draftedIds = pickRecords
            .Where(p => p.PlayerId is not null)
            .Select(p => p.PlayerId!.Value)
            .ToHashSet();
        var projectionByPlayer = _projections.GetAllProjections().ToDictionary(p => p.PlayerId);

        var userRosterId = effectiveTeamRosterId;
        var userDraftedPlayers = userRosterId is int urid
            ? pickRecords
                .Where(p => p.RosterId == urid && p.PlayerId is Guid pid && allPlayers.ContainsKey(pid))
                .Select(p => allPlayers[p.PlayerId!.Value])
                .ToList()
            : [];

        var rosterNeeds = BuildRosterNeeds(effectiveLeague, userDraftedPlayers);
        var needByPosition = rosterNeeds.ToDictionary(n => n.PositionLabel, n => n.NeedLevel);

        var undrafted = allPlayers.Values
            .Where(p => !draftedIds.Contains(p.Id))
            .Where(p => p.Position is Position.QB or Position.RB or Position.WR or Position.TE)
            .ToList();

        var replacementLevelByPosition =
            ComputeReplacementLevels(undrafted, projectionByPlayer, effectiveLeague);
        // isDynasty / strategy / draftPhase already computed above (shared with the early-return
        // paths).

        // Real published schedule -> bye weeks. Empty map when unavailable; the factor then
        // reports itself as unavailable rather than silently scoring zero.
        var byeWeeks = _byeWeeks.GetByeWeeks(effectiveLeague.Season);
        var byeCountsByPositionWeek = BuildByeCounts(userDraftedPlayers, byeWeeks);

        var rosterByPosition = rosterNeeds.ToDictionary(n => n.PositionLabel, n => n);

        var scored = undrafted
            .Select(p => ScorePlayer(
                p,
                projectionByPlayer.GetValueOrDefault(p.Id),
                replacementLevelByPosition,
                needByPosition,
                isDynasty,
                _injuries.GetCurrentInjury(p.Id),
                strategy,
                byeWeeks,
                byeCountsByPositionWeek,
                draftPhase,
                rosterByPosition))
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
        // Nobody is on the clock before the draft begins, even though slot 1 technically resolves.
        var isOnTheClock = status != DraftStatus.NotStarted && board.IsUserOnTheClock;

        return new DraftAssistantReport
        {
            Board = board,
            IsOnTheClock = isOnTheClock,
            Recommended = recommendations.FirstOrDefault(),
            Alternatives = recommendations.Skip(1).ToList(),
            RosterNeeds = rosterNeeds,
            StatusMessage = status switch
            {
                // Keep the real Sleeper state visible rather than implying an active clock.
                DraftStatus.NotStarted =>
                    "Draft has not started yet — showing who to target first.",
                DraftStatus.Paused when isOnTheClock =>
                    "Draft is paused — you're up next.",
                DraftStatus.Paused =>
                    $"Draft is paused — pick {board.NextPickNumber} is next.",
                _ => isOnTheClock
                    ? "You're on the clock."
                    : board.NextRosterId is null
                        ? $"Pick {board.NextPickNumber} is up next (roster unresolved)."
                        : $"Pick {board.NextPickNumber} is on the clock (not your team)."
            },
            UnavailableSignals = BuildUnavailableSignals(byeWeeks),
            IsStale = false,
            GeneratedAt = now,
            IsDynasty = isDynasty,
            Strategy = strategy,
            LeagueId = effectiveLeague.Id,
            Phase = draftPhase,
            DecisionSummary = BuildDecisionSummary(
                recommendations.FirstOrDefault(), byRawProjection, draftPhase, isDynasty, strategy)
        };
    }

    internal static ScoredCandidate ScorePlayer(
        Player player,
        PlayerProjection? projection,
        IReadOnlyDictionary<Position, decimal> replacementLevelByPosition,
        IReadOnlyDictionary<string, PositionalNeedLevel> needByPosition,
        bool isDynasty,
        PlayerInjuryRecord? currentInjury,
        // Defaulted so focused unit tests can exercise one factor at a time. Production always
        // passes these explicitly from GetReportAsync.
        DynastyStrategy strategy = DynastyStrategy.Hybrid,
        ByeWeekMap? byeWeeks = null,
        IReadOnlyDictionary<(string Position, int Week), int>? byeCounts = null,
        DraftPhase phase = DraftPhase.Middle,
        IReadOnlyDictionary<string, PositionalNeed>? rosterByPosition = null)
    {
        var positionLabel = PlayerPresentation.PositionLabel(player.Position);
        var factors = new List<DraftRecommendationFactor>();
        byeWeeks ??= ByeWeekMap.Empty;
        byeCounts ??= new Dictionary<(string, int), int>();

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
            var needWeight = isDynasty ? DynastyStrategyPolicy.NeedWeightMultiplier(strategy) : 1m;
            var needAdjustment = need switch
            {
                PositionalNeedLevel.Urgent => NeedUrgentBonus * needWeight,
                PositionalNeedLevel.Satisfied => NeedSatisfiedPenalty * needWeight,
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

        // Finer-grained roster shape than the three-level need signal above can express:
        // specifically, the difference between useful depth and capital tied up at one position.
        // Bounded to +/-2.5 so it can break a tie but never outrank a clearly better player.
        if (rosterByPosition is not null && rosterByPosition.TryGetValue(positionLabel, out var slot))
        {
            var tier = RosterConstructionPolicy.Classify(slot.CurrentCount, slot.TargetStarters);
            var phaseWeight = DraftPhasePolicy.RosterConstructionWeight(phase);
            var constructionAdjustment = RosterConstructionPolicy.Adjustment(tier, phaseWeight);
            fitScore += constructionAdjustment;

            if (constructionAdjustment != 0m)
            {
                factors.Add(new DraftRecommendationFactor
                {
                    Label = "Roster shape",
                    Detail = $"{RosterConstructionPolicy.Describe(tier, positionLabel)} "
                             + DraftPhasePolicy.Describe(phase),
                    Direction = constructionAdjustment > 0
                        ? FactorDirection.Positive
                        : FactorDirection.Negative,
                    Available = true
                });
            }
        }

        if (isDynasty)
        {
            if (player.Age is int age)
            {
                // Phase layers on top of strategy: it scales the curve the strategy chose, so a
                // Competitor late is still far less youth-driven than a Rebuild late.
                var ageAdjustment = DynastyStrategyPolicy.AgeAdjustment(strategy, age)
                                    * DraftPhasePolicy.UpsideWeight(phase);
                ageAdjustment = Math.Clamp(
                    ageAdjustment,
                    -DynastyStrategyPolicy.MaxAgeAdjustment,
                    DynastyStrategyPolicy.MaxAgeAdjustment);
                fitScore += ageAdjustment;
                if (ageAdjustment != 0m)
                {
                    factors.Add(new DraftRecommendationFactor
                    {
                        Label = $"Dynasty age curve ({DynastyStrategyPolicy.DisplayName(strategy)})",
                        Detail = ageAdjustment > 0
                            ? $"Age {age} — upside is weighted more here ({DraftPhasePolicy.DisplayName(phase).ToLowerInvariant()})."
                            : $"Age {age} — ageing profile is discounted under this strategy.",
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

        // Bye-week concentration. Real published schedule only; when the schedule is unavailable
        // the factor reports itself unavailable rather than scoring a silent zero.
        if (!byeWeeks.IsAvailable)
        {
            factors.Add(new DraftRecommendationFactor
            {
                Label = "Bye week",
                Detail = "Schedule not loaded — bye-week fit not considered.",
                Direction = FactorDirection.Neutral,
                Available = false
            });
        }
        else if (byeWeeks.ByeWeekFor(player.Team) is int bye)
        {
            var alreadyOnThatBye = byeCounts.GetValueOrDefault((positionLabel, bye), 0);
            var byePenalty = ByeWeekCollisionPolicy.Penalty(alreadyOnThatBye + 1);
            fitScore += byePenalty;

            factors.Add(new DraftRecommendationFactor
            {
                Label = "Bye week",
                Detail = byePenalty < 0
                    ? $"Week {bye} — you already have {alreadyOnThatBye} {positionLabel} on this bye."
                    : $"Week {bye} — no {positionLabel} bye collision.",
                Direction = byePenalty < 0 ? FactorDirection.Negative : FactorDirection.Positive,
                Available = true
            });
        }
        else
        {
            factors.Add(new DraftRecommendationFactor
            {
                Label = "Bye week",
                Detail = $"No bye on file for {player.Team ?? "this team"}.",
                Direction = FactorDirection.Neutral,
                Available = false
            });
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

    /// <summary>
    /// How many already-drafted players of each position sit on each bye week. Built from the
    /// user's real drafted roster; empty when the schedule is unavailable.
    /// </summary>
    /// <summary>
    /// Plain-language explanation of the top pick, written for a manager mid-draft rather than
    /// for a developer. Names the deciding consideration and, when Best Available and Team Fit
    /// disagree, says so explicitly instead of quietly preferring one.
    /// </summary>
    private static string BuildDecisionSummary(
        DraftRecommendation? top,
        IReadOnlyList<ScoredCandidate> byRawProjection,
        DraftPhase phase,
        bool isDynasty,
        DynastyStrategy strategy)
    {
        if (top is null)
        {
            return "";
        }

        var parts = new List<string>();
        var bestAvailable = byRawProjection.FirstOrDefault();

        if (bestAvailable is not null &&
            !string.Equals(bestAvailable.Player.FullName, top.PlayerName, StringComparison.Ordinal))
        {
            parts.Add(
                $"{bestAvailable.Player.FullName} is the highest raw projection, but {top.PlayerName} "
                + "fits this roster better right now");
        }
        else
        {
            parts.Add($"{top.PlayerName} is both the best available and the best fit");
        }

        parts.Add(DraftPhasePolicy.Describe(phase).TrimEnd('.').ToLowerInvariant());

        if (isDynasty && strategy != DynastyStrategy.Hybrid)
        {
            parts.Add(strategy == DynastyStrategy.Rebuild
                ? "and your rebuild setting adds weight to youth and upside"
                : "and your win-now setting favours immediate production");
        }

        return string.Join(" — ", parts.Take(2)) +
               (parts.Count > 2 ? " " + parts[2] : "") + ".";
    }

    /// <summary>
    /// Honest inventory of what the recommendation could NOT consider. Bye weeks move in and out
    /// of this list depending on whether the real schedule loaded.
    /// </summary>
    private static IReadOnlyList<string> BuildUnavailableSignals(ByeWeekMap byeWeeks)
    {
        var signals = KnownUnavailableSignals.ToList();
        if (!byeWeeks.IsAvailable)
        {
            signals.Add("Bye weeks (schedule not loaded)");
        }

        return signals;
    }

    internal static IReadOnlyDictionary<(string Position, int Week), int> BuildByeCounts(
        IReadOnlyList<Player> draftedPlayers,
        ByeWeekMap byeWeeks)
    {
        var counts = new Dictionary<(string, int), int>();
        if (!byeWeeks.IsAvailable)
        {
            return counts;
        }

        foreach (var player in draftedPlayers)
        {
            if (byeWeeks.ByeWeekFor(player.Team) is not int bye)
            {
                continue;
            }

            var key = (PlayerPresentation.PositionLabel(player.Position), bye);
            counts[key] = counts.GetValueOrDefault(key, 0) + 1;
        }

        return counts;
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

    /// <summary>
    /// Build an effective League purely from a Sleeper draft object. Every value is read from the
    /// draft — nothing is assumed. Used when following a draft that belongs to a league the user
    /// has not connected (a Sleeper mock being the common case).
    ///
    /// The id is derived deterministically from the draft id so the same draft always maps to the
    /// same league id across polls, which keeps the dynasty-strategy selection stable.
    /// </summary>
    internal static League BuildLeagueFromDraft(SleeperDraftSnapshot draft)
    {
        var leagueType = SleeperScoringMapper.MapLeagueType(
            int.TryParse(draft.LeagueTypeRaw, out var rawType) ? rawType : 0);

        var scoring = SleeperScoringMapper.MapScoringTypeFromName(draft.ScoringType);

        return new League
        {
            Id = SleeperPlayerIds.ToPlaybookId($"draft:{draft.DraftId}"),
            Name = string.IsNullOrWhiteSpace(draft.Name) ? "Sleeper draft" : draft.Name!,
            Platform = LeaguePlatform.Sleeper,
            LeagueType = leagueType,
            ScoringType = scoring,
            NumberOfTeams = draft.Teams,
            CurrentWeek = 1,
            Season = int.TryParse(draft.Season, out var season) ? season : DateTime.UtcNow.Year,
            IsActive = true,
            DataSource = LeagueDataSource.Sleeper,
            ExternalId = draft.LeagueId,
            RosterPositions = draft.RosterPositions
        };
    }

    /// <summary>
    /// Which roster in the attached draft belongs to the user. Resolved from the draft's own
    /// draft_order using the Sleeper user id that owns the user's roster in a connected league.
    /// Returns null when that cannot be established — the board then shows the draft without
    /// claiming a team, rather than guessing one.
    /// </summary>
    internal static int? ResolveAttachedUserRosterId(
        SleeperDraftSnapshot draft,
        SleeperLeagueSnapshot? connectedLeagueSnapshot,
        League? connectedLeague)
    {
        if (connectedLeagueSnapshot is null || connectedLeague?.SelectedRosterId is not int selectedRoster)
        {
            return null;
        }

        var sleeperUserId = connectedLeagueSnapshot.Rosters
            .FirstOrDefault(r => r.RosterId == selectedRoster)?.OwnerId;

        if (string.IsNullOrWhiteSpace(sleeperUserId) ||
            !draft.DraftOrderByUserId.TryGetValue(sleeperUserId, out var slot))
        {
            return null;
        }

        return draft.SlotToRosterId.TryGetValue(slot, out var rosterId) ? rosterId : null;
    }

    internal static IReadOnlyDictionary<int, int> BuildSlotToRosterId(
        SleeperDraftSnapshot draft, SleeperLeagueSnapshot? leagueSnapshot)
    {
        // Sleeper publishes slot_to_roster_id on the draft itself. Prefer it: it is the
        // authoritative mapping and, critically, it is present for a draft attached directly by
        // id where no league roster list exists. The league cross-reference below remains as a
        // fallback for drafts that omit it.
        if (draft.SlotToRosterId.Count > 0)
        {
            return new Dictionary<int, int>(draft.SlotToRosterId);
        }

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
