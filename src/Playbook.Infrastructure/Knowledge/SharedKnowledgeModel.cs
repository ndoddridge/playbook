using Playbook.Application.Knowledge;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Decisions;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Knowledge;
using Playbook.Core.Players;
using Playbook.Core.Replay;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Knowledge;

/// <summary>
/// Assembles shared knowledge from historical snapshots or live sources.
/// Reuses existing PlayerKnowledge composition for Start/Sit compatibility.
/// Never fabricates unavailable team/matchup/context fields.
/// </summary>
public sealed class SharedKnowledgeModel : ISharedKnowledgeModel
{
    private readonly IHistoricalKnowledgeFactory _historicalKnowledge;

    public SharedKnowledgeModel(IHistoricalKnowledgeFactory historicalKnowledge)
    {
        _historicalKnowledge = historicalKnowledge;
    }

    public SharedKnowledgeBundle BuildFromHistorical(
        HistoricalSnapshot snapshot,
        Guid playerId,
        PredictionType predictionType)
    {
        var player = snapshot.Players.FirstOrDefault(p => p.PlayerId == playerId)
            ?? throw new InvalidOperationException($"Player {playerId} not found in historical snapshot.");

        var decisionContext = ReplayContext.FromSnapshot(snapshot).DecisionContext;
        var playerKnowledge = _historicalKnowledge
            .BuildKnowledge(snapshot, decisionContext)
            .First(k => k.PlayerId == playerId);

        return AssembleFromHistoricalPlayer(snapshot, player, playerKnowledge, predictionType);
    }

    public PredictionContext BuildHistoricalPredictionContext(
        HistoricalSnapshot snapshot,
        Guid playerId,
        PredictionType predictionType,
        DecisionContext? decisionContext = null)
    {
        var bundle = BuildFromHistorical(snapshot, playerId, predictionType);
        var player = snapshot.Players.First(p => p.PlayerId == playerId);
        var ctx = decisionContext ?? ReplayContext.FromSnapshot(snapshot).DecisionContext;

        return new PredictionContext
        {
            PredictionType = predictionType,
            Season = snapshot.Season,
            Week = snapshot.Week,
            InformationCutoff = snapshot.InformationCutoff,
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            Position = player.Position,
            Team = player.Team,
            OpponentTeam = null,
            ScoringType = snapshot.ScoringType,
            LeagueId = snapshot.LeagueId,
            Knowledge = bundle,
            ProjectedPoints = player.ProjectedPoints,
            ProjectionConfidence = player.ProjectionConfidence,
            DecisionContext = ctx,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public SharedKnowledgeBundle BuildFromPlayerKnowledge(
        PlayerKnowledge knowledge,
        PredictionType predictionType,
        int season,
        int week,
        string? team = null,
        string? opponentTeam = null)
    {
        var cutoff = knowledge.InformationCutoff;
        var facts = KnowledgeTemporalGuard.FilterFacts(knowledge.Facts, cutoff);
        var signals = KnowledgeTemporalGuard.FilterSignals(knowledge.Signals, cutoff);
        var evidence = MapSignalsToEvidence(signals, cutoff).ToList();
        evidence.AddRange(MarkCommonUnavailableAspects(
            evidence,
            includeTeamMatchupContext: true,
            cutoff));

        var unavailableAspects = evidence
            .Where(e => e.IsUnavailableMarker)
            .Select(e => e.Aspect)
            .Distinct()
            .ToList();

        var filteredKnowledge = ApplyCutoffToPlayerKnowledge(knowledge, facts, signals);

        var bundle = new SharedKnowledgeBundle
        {
            PlayerId = knowledge.PlayerId,
            PlayerName = knowledge.PlayerName,
            Position = ParsePosition(knowledge.PositionLabel),
            Team = team,
            OpponentTeam = opponentTeam,
            Season = season,
            Week = week,
            InformationCutoff = cutoff,
            GeneratedAt = knowledge.GeneratedAt,
            Facts = facts,
            Evidence = evidence,
            UnavailableAspects = unavailableAspects,
            UnavailableSources = knowledge.MissingEvidence,
            OverallStatus = filteredKnowledge.OverallStatus,
            KnowledgeConfidence = filteredKnowledge.KnowledgeConfidence,
            DecisionPlayerKnowledge = filteredKnowledge
        };

        KnowledgeTemporalGuard.AssertNoFutureLeak(bundle);
        _ = predictionType;
        return bundle;
    }

    public PredictionContext BuildStartSitPredictionContext(
        PlayerKnowledge knowledge,
        DecisionContext decisionContext,
        string? team = null,
        string? opponentTeam = null)
    {
        var bundle = BuildFromPlayerKnowledge(
            knowledge,
            PredictionType.StartSit,
            decisionContext.Season,
            decisionContext.Week,
            team,
            opponentTeam);

        return new PredictionContext
        {
            PredictionType = PredictionType.StartSit,
            Season = decisionContext.Season,
            Week = decisionContext.Week,
            InformationCutoff = decisionContext.InformationCutoff ?? knowledge.InformationCutoff,
            PlayerId = knowledge.PlayerId,
            PlayerName = knowledge.PlayerName,
            Position = ParsePosition(knowledge.PositionLabel),
            Team = team,
            OpponentTeam = opponentTeam,
            ScoringType = decisionContext.ScoringType,
            LeagueId = decisionContext.LeagueId,
            Knowledge = bundle,
            ProjectedPoints = knowledge.ProjectedPoints,
            ProjectionConfidence = knowledge.ProjectionConfidence,
            DecisionContext = decisionContext,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    public PredictionContext BuildQuickPickPredictionContext(
        QuickPickEvaluationContext evaluation,
        DateTimeOffset? informationCutoff = null)
    {
        var cutoff = informationCutoff ?? evaluation.Line.Event.CommenceTime;
        var facts = new List<KnowledgeFact>();
        var evidence = new List<KnowledgeEvidence>();
        var unavailableSources = new List<string>();

        AddQuickPickProjection(evaluation, cutoff, facts, evidence);
        AddQuickPickProduction(evaluation, cutoff, facts, evidence, unavailableSources);
        AddQuickPickInjury(evaluation.InjuryProfile, cutoff, facts, evidence, unavailableSources);
        AddQuickPickIntelligence(evaluation, cutoff, facts, evidence, unavailableSources);

        evidence.AddRange(MarkCommonUnavailableAspects(
            evidence,
            includeTeamMatchupContext: true,
            cutoff));

        // Explicitly keep unknown as unknown — never invent matchup/weather/rest.
        var unavailableAspects = evidence
            .Where(e => e.IsUnavailableMarker)
            .Select(e => e.Aspect)
            .Distinct()
            .ToList();

        var overall = DeriveOverallStatus(evidence, unavailableAspects.Count);
        var confidence = DeriveKnowledgeConfidence(evidence, unavailableAspects.Count);

        var bundle = new SharedKnowledgeBundle
        {
            PlayerId = evaluation.Line.PlayerId,
            PlayerName = evaluation.Line.PlayerName,
            Position = null,
            Team = evaluation.Line.TeamName,
            OpponentTeam = null,
            Season = evaluation.Line.Event.Season,
            Week = evaluation.Line.Event.Week,
            InformationCutoff = cutoff,
            GeneratedAt = DateTimeOffset.UtcNow,
            Facts = KnowledgeTemporalGuard.FilterFacts(facts, cutoff),
            Evidence = KnowledgeTemporalGuard.FilterEvidence(evidence, cutoff),
            UnavailableAspects = unavailableAspects,
            UnavailableSources = unavailableSources.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            OverallStatus = overall,
            KnowledgeConfidence = confidence,
            DecisionPlayerKnowledge = null
        };

        KnowledgeTemporalGuard.AssertNoFutureLeak(bundle);

        return new PredictionContext
        {
            PredictionType = PredictionType.QuickPick,
            Season = bundle.Season,
            Week = bundle.Week,
            InformationCutoff = cutoff,
            PlayerId = bundle.PlayerId,
            PlayerName = bundle.PlayerName,
            Position = null,
            Team = bundle.Team,
            OpponentTeam = null,
            ScoringType = null,
            LeagueId = null,
            Knowledge = bundle,
            ProjectedPoints = evaluation.PlaybookProjection,
            ProjectionConfidence = evaluation.ProjectionConfidence,
            MarketLine = evaluation.Line.Line,
            MarketLabel = evaluation.Line.Market.ToString(),
            DecisionContext = null,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private SharedKnowledgeBundle AssembleFromHistoricalPlayer(
        HistoricalSnapshot snapshot,
        HistoricalPlayerState player,
        PlayerKnowledge playerKnowledge,
        PredictionType predictionType)
    {
        var cutoff = snapshot.InformationCutoff;
        var facts = KnowledgeTemporalGuard.FilterFacts(playerKnowledge.Facts, cutoff).ToList();
        var signals = KnowledgeTemporalGuard.FilterSignals(playerKnowledge.Signals, cutoff);
        var evidence = MapSignalsToEvidence(signals, cutoff).ToList();

        // Enrich with structured player aspects from snapshot fields (no fabrication).
        AddHistoricalEnrichment(player, cutoff, facts, evidence);

        evidence.AddRange(MarkCommonUnavailableAspects(
            evidence,
            includeTeamMatchupContext: true,
            cutoff));

        // Snapshot-level unavailable sources.
        foreach (var source in snapshot.UnavailableSources)
        {
            if (!facts.Any(f => f.Key == "source.unavailable" && f.Statement.Contains(source, StringComparison.OrdinalIgnoreCase)))
            {
                facts.Add(new KnowledgeFact
                {
                    Key = "source.unavailable",
                    Statement = $"Unavailable source at cutoff: {source}.",
                    Source = "HistoricalSnapshot",
                    ObservedAt = cutoff,
                    Status = EvidenceStatus.Unknown
                });
            }
        }

        var unavailableAspects = evidence
            .Where(e => e.IsUnavailableMarker)
            .Select(e => e.Aspect)
            .Distinct()
            .ToList();

        var filteredPk = ApplyCutoffToPlayerKnowledge(playerKnowledge, facts, signals);

        var bundle = new SharedKnowledgeBundle
        {
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            Position = player.Position,
            Team = player.Team,
            OpponentTeam = null,
            Season = snapshot.Season,
            Week = snapshot.Week,
            InformationCutoff = cutoff,
            GeneratedAt = cutoff,
            Facts = facts,
            Evidence = KnowledgeTemporalGuard.FilterEvidence(evidence, cutoff),
            UnavailableAspects = unavailableAspects,
            UnavailableSources = snapshot.UnavailableSources
                .Concat(player.UnavailableSignals)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            OverallStatus = filteredPk.OverallStatus,
            KnowledgeConfidence = filteredPk.KnowledgeConfidence,
            DecisionPlayerKnowledge = filteredPk
        };

        KnowledgeTemporalGuard.AssertNoFutureLeak(bundle, cutoff);
        _ = predictionType;
        return bundle;
    }

    private static void AddHistoricalEnrichment(
        HistoricalPlayerState player,
        DateTimeOffset cutoff,
        List<KnowledgeFact> facts,
        List<KnowledgeEvidence> evidence)
    {
        // Injury after cutoff must not appear — HistoricalSnapshotBuilder already filters,
        // but we double-check timestamps here.
        if (player.InjuryObservedAt is DateTimeOffset injuryAt && injuryAt > cutoff)
        {
            // Strip any injury evidence that may have slipped through.
            evidence.RemoveAll(e => e.Aspect is KnowledgeAspect.InjuryStatus or KnowledgeAspect.Health
                && e.Direction == SignalDirection.Negative);
            facts.RemoveAll(f => f.Key.StartsWith("health.injury", StringComparison.OrdinalIgnoreCase));
        }

        if (player.RecentNewsObservedAt is DateTimeOffset newsAt && newsAt > cutoff)
        {
            evidence.RemoveAll(e => e.Aspect == KnowledgeAspect.News);
            facts.RemoveAll(f => f.Key.StartsWith("news.", StringComparison.OrdinalIgnoreCase));
        }

        if (player.DataSufficiency is DataSufficiency sufficiency)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "projection.data_sufficiency",
                Statement = $"Projection data sufficiency: {sufficiency}.",
                Source = "HistoricalFeatureReconstructor",
                ObservedAt = cutoff,
                Status = EvidenceStatus.Known
            });
        }
    }

    private static IEnumerable<KnowledgeEvidence> MapSignalsToEvidence(
        IEnumerable<KnowledgeSignal> signals,
        DateTimeOffset? cutoff) =>
        signals.Select(s => new KnowledgeEvidence
        {
            Scope = KnowledgeScope.Player,
            Aspect = MapAspect(s.Type),
            Statement = s.Explanation,
            Direction = s.Direction,
            Strength = s.Strength,
            Status = s.Status,
            Confidence = s.Confidence,
            Reliability = MapReliability(s.Confidence, s.Status),
            Source = s.Source,
            ObservedAt = s.ObservedAt,
            InformationCutoff = cutoff,
            Value = s.Value,
            IsUnavailableMarker = s.Status == EvidenceStatus.Unknown &&
                                  s.Direction == SignalDirection.Uncertainty &&
                                  s.Type is SignalType.Projection or SignalType.Coverage,
            Category = s.Category
        });

    private static KnowledgeAspect MapAspect(SignalType type) => type switch
    {
        SignalType.Projection => KnowledgeAspect.Projection,
        SignalType.Floor => KnowledgeAspect.Projection,
        SignalType.Ceiling => KnowledgeAspect.Projection,
        SignalType.RecentProduction => KnowledgeAspect.RecentProduction,
        SignalType.Opportunity => KnowledgeAspect.Opportunity,
        SignalType.Usage => KnowledgeAspect.Usage,
        SignalType.Health => KnowledgeAspect.Health,
        SignalType.Role => KnowledgeAspect.Role,
        SignalType.News => KnowledgeAspect.News,
        SignalType.MatchupContext => KnowledgeAspect.PositionalMatchup,
        SignalType.Volatility => KnowledgeAspect.Volatility,
        SignalType.Coverage => KnowledgeAspect.Coverage,
        SignalType.Outlook => KnowledgeAspect.Outlook,
        _ => KnowledgeAspect.Coverage
    };

    private static EvidenceReliability MapReliability(int confidence, EvidenceStatus status)
    {
        if (status is EvidenceStatus.Unknown or EvidenceStatus.Conflicting)
        {
            return EvidenceReliability.Unknown;
        }

        return confidence >= 70 ? EvidenceReliability.High
            : confidence >= 45 ? EvidenceReliability.Moderate
            : EvidenceReliability.Low;
    }

    private static IEnumerable<KnowledgeEvidence> MarkCommonUnavailableAspects(
        IReadOnlyList<KnowledgeEvidence> existing,
        bool includeTeamMatchupContext,
        DateTimeOffset? cutoff)
    {
        var present = existing
            .Where(e => !e.IsUnavailableMarker && e.Status != EvidenceStatus.Unknown)
            .Select(e => e.Aspect)
            .ToHashSet();

        var candidates = new List<(KnowledgeScope Scope, KnowledgeAspect Aspect, string Label)>
        {
            (KnowledgeScope.Player, KnowledgeAspect.SnapShare, "Snap share"),
            (KnowledgeScope.Player, KnowledgeAspect.TargetShare, "Target share"),
            (KnowledgeScope.Player, KnowledgeAspect.CarryShare, "Carry share"),
            (KnowledgeScope.Player, KnowledgeAspect.DepthChart, "Depth chart"),
            (KnowledgeScope.Player, KnowledgeAspect.Trend, "Trend"),
            (KnowledgeScope.Context, KnowledgeAspect.Weather, "Weather"),
            (KnowledgeScope.Context, KnowledgeAspect.Rest, "Rest"),
            (KnowledgeScope.Context, KnowledgeAspect.HomeAway, "Home/away"),
            (KnowledgeScope.Context, KnowledgeAspect.GameScript, "Game script"),
            (KnowledgeScope.Context, KnowledgeAspect.TeammateAvailability, "Teammate availability"),
            (KnowledgeScope.Context, KnowledgeAspect.RoleChange, "Role change")
        };

        if (includeTeamMatchupContext)
        {
            candidates.AddRange(
            [
                (KnowledgeScope.Team, KnowledgeAspect.OffensiveEnvironment, "Offensive environment"),
                (KnowledgeScope.Team, KnowledgeAspect.DefensiveEnvironment, "Defensive environment"),
                (KnowledgeScope.Team, KnowledgeAspect.Pace, "Pace"),
                (KnowledgeScope.Team, KnowledgeAspect.ScoringEnvironment, "Scoring environment"),
                (KnowledgeScope.Team, KnowledgeAspect.OpponentStrength, "Opponent strength"),
                (KnowledgeScope.Team, KnowledgeAspect.RecentForm, "Team recent form"),
                (KnowledgeScope.Matchup, KnowledgeAspect.OpponentTendencies, "Opponent tendencies"),
                (KnowledgeScope.Matchup, KnowledgeAspect.PositionalMatchup, "Positional matchup"),
                (KnowledgeScope.Matchup, KnowledgeAspect.GameEnvironment, "Game environment")
            ]);
        }

        foreach (var (scope, aspect, label) in candidates)
        {
            if (present.Contains(aspect))
            {
                continue;
            }

            yield return Unavailable(scope, aspect, label, cutoff);
        }
    }

    private static KnowledgeEvidence Unavailable(
        KnowledgeScope scope,
        KnowledgeAspect aspect,
        string label,
        DateTimeOffset? cutoff) =>
        new()
        {
            Scope = scope,
            Aspect = aspect,
            Statement = $"No reliable {label.ToLowerInvariant()} information available at cutoff.",
            Direction = SignalDirection.Uncertainty,
            Strength = SignalStrength.Weak,
            Status = EvidenceStatus.Unknown,
            Confidence = 0,
            Reliability = EvidenceReliability.Unknown,
            Source = "SharedKnowledgeModel",
            ObservedAt = cutoff,
            InformationCutoff = cutoff,
            IsUnavailableMarker = true,
            Category = scope.ToString()
        };

    private static void AddQuickPickProjection(
        QuickPickEvaluationContext evaluation,
        DateTimeOffset? cutoff,
        List<KnowledgeFact> facts,
        List<KnowledgeEvidence> evidence)
    {
        if (evaluation.PlaybookProjection is decimal pts)
        {
            var line = evaluation.Line.Line;
            facts.Add(new KnowledgeFact
            {
                Key = "projection.points",
                Statement = $"Playbook projects {pts:0.0} for {evaluation.Line.Market}.",
                Source = "PropStatProjector",
                ObservedAt = cutoff,
                Status = EvidenceStatus.Known
            });
            var direction = line is null
                ? SignalDirection.Neutral
                : pts >= line.Value
                    ? SignalDirection.Positive
                    : SignalDirection.Negative;
            var strength = line is null
                ? SignalStrength.Moderate
                : Math.Abs(pts - line.Value) >= 8
                    ? SignalStrength.Strong
                    : SignalStrength.Moderate;
            evidence.Add(new KnowledgeEvidence
            {
                Scope = KnowledgeScope.Player,
                Aspect = KnowledgeAspect.Projection,
                Statement = line is null
                    ? $"Projection {pts:0.0} (no market line)."
                    : $"Projection {pts:0.0} vs line {line.Value:0.0}.",
                Direction = direction,
                Strength = strength,
                Status = evaluation.ProjectionConfidence >= 55
                    ? EvidenceStatus.Known
                    : EvidenceStatus.LowConfidence,
                Confidence = Math.Clamp(evaluation.ProjectionConfidence, 0, 100),
                Reliability = MapReliability(evaluation.ProjectionConfidence, EvidenceStatus.Known),
                Source = "PropStatProjector",
                ObservedAt = cutoff,
                InformationCutoff = cutoff,
                Value = (double)pts,
                Category = "Projection"
            });
        }
        else
        {
            facts.Add(new KnowledgeFact
            {
                Key = "projection",
                Statement = "No counting-stat projection available.",
                Source = "PropStatProjector",
                ObservedAt = cutoff,
                Status = EvidenceStatus.Unknown
            });
            evidence.Add(Unavailable(KnowledgeScope.Player, KnowledgeAspect.Projection, "Projection", cutoff));
        }
    }

    private static void AddQuickPickProduction(
        QuickPickEvaluationContext evaluation,
        DateTimeOffset? cutoff,
        List<KnowledgeFact> facts,
        List<KnowledgeEvidence> evidence,
        List<string> unavailable)
    {
        if (evaluation.Production is null && evaluation.StatisticalContext is null)
        {
            unavailable.Add("Recent production");
            evidence.Add(Unavailable(
                KnowledgeScope.Player,
                KnowledgeAspect.RecentProduction,
                "Recent production",
                cutoff));
            return;
        }

        if (evaluation.StatisticalContext is PlayerStatisticalContext stats)
        {
            var asOf = stats.AsOf;
            if (!KnowledgeTemporalGuard.IsKnownAtCutoff(asOf, cutoff))
            {
                unavailable.Add("Recent production (after cutoff)");
                evidence.Add(Unavailable(
                    KnowledgeScope.Player,
                    KnowledgeAspect.RecentProduction,
                    "Recent production",
                    cutoff));
            }
            else if (stats.RecentProduction?.PerGame is { } perGame)
            {
                var yards = (perGame.RushYards ?? 0) + (perGame.ReceivingYards ?? 0) + (perGame.PassYards ?? 0);
                facts.Add(new KnowledgeFact
                {
                    Key = "production.recent_window",
                    Statement =
                        $"Recent production window '{stats.RecentProduction.Label}' " +
                        $"({stats.RecentProduction.Games ?? 0} games).",
                    Source = "PlayerStatisticalContext",
                    ObservedAt = asOf,
                    Status = EvidenceStatus.Known
                });
                evidence.Add(new KnowledgeEvidence
                {
                    Scope = KnowledgeScope.Player,
                    Aspect = KnowledgeAspect.RecentProduction,
                    Statement = $"Recent per-game counting production available ({yards:0.0} combined yards/game).",
                    Direction = SignalDirection.Neutral,
                    Strength = SignalStrength.Moderate,
                    Status = EvidenceStatus.Known,
                    Confidence = 60,
                    Reliability = EvidenceReliability.Moderate,
                    Source = "PlayerStatisticalContext",
                    ObservedAt = asOf,
                    InformationCutoff = cutoff,
                    Value = (double)yards,
                    Category = "Production"
                });
            }

            if (stats.Usage is StatisticalUsageSignals usage)
            {
                if (usage.TargetsPerGame is decimal targets)
                {
                    evidence.Add(new KnowledgeEvidence
                    {
                        Scope = KnowledgeScope.Player,
                        Aspect = KnowledgeAspect.Usage,
                        Statement = $"Targets/game {targets:0.0}.",
                        Direction = targets >= 7 ? SignalDirection.Positive
                            : targets <= 3 ? SignalDirection.Negative
                            : SignalDirection.Neutral,
                        Strength = SignalStrength.Moderate,
                        Status = EvidenceStatus.Known,
                        Confidence = 60,
                        Reliability = EvidenceReliability.Moderate,
                        Source = "PlayerStatisticalContext",
                        ObservedAt = asOf,
                        InformationCutoff = cutoff,
                        Value = (double)targets,
                        Category = "Usage"
                    });
                }

                if (usage.CarriesPerGame is decimal carries)
                {
                    evidence.Add(new KnowledgeEvidence
                    {
                        Scope = KnowledgeScope.Player,
                        Aspect = KnowledgeAspect.Usage,
                        Statement = $"Carries/game {carries:0.0}.",
                        Direction = carries >= 12 ? SignalDirection.Positive
                            : carries <= 4 ? SignalDirection.Negative
                            : SignalDirection.Neutral,
                        Strength = SignalStrength.Moderate,
                        Status = EvidenceStatus.Known,
                        Confidence = 60,
                        Reliability = EvidenceReliability.Moderate,
                        Source = "PlayerStatisticalContext",
                        ObservedAt = asOf,
                        InformationCutoff = cutoff,
                        Value = (double)carries,
                        Category = "Usage"
                    });
                }
            }

            if (stats.Trend is not StatisticalTrendSignal.Unknown)
            {
                evidence.Add(new KnowledgeEvidence
                {
                    Scope = KnowledgeScope.Player,
                    Aspect = KnowledgeAspect.Trend,
                    Statement = $"Statistical trend: {stats.Trend}.",
                    Direction = stats.Trend == StatisticalTrendSignal.Increasing
                        ? SignalDirection.Positive
                        : stats.Trend == StatisticalTrendSignal.Decreasing
                            ? SignalDirection.Negative
                            : SignalDirection.Neutral,
                    Strength = SignalStrength.Weak,
                    Status = EvidenceStatus.Known,
                    Confidence = 50,
                    Reliability = EvidenceReliability.Low,
                    Source = "PlayerStatisticalContext",
                    ObservedAt = asOf,
                    InformationCutoff = cutoff,
                    Category = "Trend"
                });
            }
        }

        if (evaluation.UsingPriorRegularSeasonProduction)
        {
            facts.Add(new KnowledgeFact
            {
                Key = "production.prior_regular_season",
                Statement = "Using prior regular-season production baseline for this slate phase.",
                Source = "PropStatProjector",
                ObservedAt = cutoff,
                Status = EvidenceStatus.Known
            });
        }
    }

    private static void AddQuickPickInjury(
        PlayerInjuryProfile? injury,
        DateTimeOffset? cutoff,
        List<KnowledgeFact> facts,
        List<KnowledgeEvidence> evidence,
        List<string> unavailable)
    {
        if (injury?.CurrentInjury is null)
        {
            if (injury is null)
            {
                unavailable.Add("Injury");
                evidence.Add(Unavailable(KnowledgeScope.Player, KnowledgeAspect.InjuryStatus, "Injury status", cutoff));
                return;
            }

            facts.Add(new KnowledgeFact
            {
                Key = "health.label",
                Statement = "No current injury designation in profile.",
                Source = "PlayerInjuryService",
                ObservedAt = injury.LastUpdated ?? cutoff,
                Status = EvidenceStatus.Known
            });
            evidence.Add(new KnowledgeEvidence
            {
                Scope = KnowledgeScope.Player,
                Aspect = KnowledgeAspect.Health,
                Statement = "Listed without a current injury designation.",
                Direction = SignalDirection.Positive,
                Strength = SignalStrength.Moderate,
                Status = EvidenceStatus.Known,
                Confidence = 70,
                Reliability = EvidenceReliability.Moderate,
                Source = "PlayerInjuryService",
                ObservedAt = injury.LastUpdated ?? cutoff,
                InformationCutoff = cutoff,
                Category = "Health"
            });
            return;
        }

        var current = injury.CurrentInjury;
        var observed = current.LastUpdated == default ? current.Date : current.LastUpdated;
        if (!KnowledgeTemporalGuard.IsKnownAtCutoff(observed, cutoff) &&
            !KnowledgeTemporalGuard.IsKnownAtCutoff(current.Date, cutoff))
        {
            unavailable.Add("Injury (after cutoff)");
            evidence.Add(Unavailable(KnowledgeScope.Player, KnowledgeAspect.InjuryStatus, "Injury status", cutoff));
            return;
        }

        var knownAt = KnowledgeTemporalGuard.IsKnownAtCutoff(observed, cutoff) ? observed : current.Date;
        facts.Add(new KnowledgeFact
        {
            Key = "health.injury.current",
            Statement = $"Listed as {current.Status}" +
                        (string.IsNullOrWhiteSpace(current.BodyPart) ? "." : $" ({current.BodyPart})."),
            Source = current.Source ?? "PlayerInjuryService",
            ObservedAt = knownAt,
            Status = EvidenceStatus.Known
        });
        evidence.Add(new KnowledgeEvidence
        {
            Scope = KnowledgeScope.Player,
            Aspect = KnowledgeAspect.InjuryStatus,
            Statement = $"Injury designation: {current.Status}.",
            Direction = SignalDirection.Negative,
            Strength = current.Status.Contains("Out", StringComparison.OrdinalIgnoreCase)
                ? SignalStrength.Strong
                : SignalStrength.Moderate,
            Status = EvidenceStatus.Known,
            Confidence = 80,
            Reliability = EvidenceReliability.High,
            Source = current.Source ?? "PlayerInjuryService",
            ObservedAt = knownAt,
            InformationCutoff = cutoff,
            Category = "Health"
        });
    }

    private static void AddQuickPickIntelligence(
        QuickPickEvaluationContext evaluation,
        DateTimeOffset? cutoff,
        List<KnowledgeFact> facts,
        List<KnowledgeEvidence> evidence,
        List<string> unavailable)
    {
        var recent = evaluation.RecentFacts
            .Where(f => KnowledgeTemporalGuard.IsKnownAtCutoff(f.Created, cutoff))
            .Take(3)
            .ToList();

        if (recent.Count == 0 && evaluation.Intelligence is null)
        {
            unavailable.Add("Intelligence facts");
            return;
        }

        foreach (var fact in recent)
        {
            var statement = string.IsNullOrWhiteSpace(fact.Description)
                ? fact.Title
                : $"{fact.Title}: {fact.Description}";
            facts.Add(new KnowledgeFact
            {
                Key = $"intel.{fact.Id}",
                Statement = statement,
                Source = fact.Source.ToString(),
                ObservedAt = fact.Created,
                Status = EvidenceStatus.Known
            });
            evidence.Add(new KnowledgeEvidence
            {
                Scope = KnowledgeScope.Player,
                Aspect = KnowledgeAspect.News,
                Statement = statement,
                Direction = SignalDirection.Neutral,
                Strength = SignalStrength.Weak,
                Status = EvidenceStatus.Known,
                Confidence = Math.Clamp(fact.Confidence, 0, 100),
                Reliability = EvidenceReliability.Low,
                Source = fact.Source.ToString(),
                ObservedAt = fact.Created,
                InformationCutoff = cutoff,
                Category = "Intelligence"
            });
        }
    }

    private static PlayerKnowledge ApplyCutoffToPlayerKnowledge(
        PlayerKnowledge source,
        IReadOnlyList<KnowledgeFact> facts,
        IReadOnlyList<KnowledgeSignal> signals) =>
        new()
        {
            PlayerId = source.PlayerId,
            PlayerName = source.PlayerName,
            PositionLabel = source.PositionLabel,
            Facts = facts,
            Signals = signals,
            OverallStatus = source.OverallStatus,
            KnowledgeConfidence = source.KnowledgeConfidence,
            MissingEvidence = source.MissingEvidence,
            GeneratedAt = source.GeneratedAt,
            InformationCutoff = source.InformationCutoff,
            ProjectedPoints = source.ProjectedPoints,
            Floor = source.Floor,
            Ceiling = source.Ceiling,
            ProjectionConfidence = source.ProjectionConfidence,
            OpportunityScore = source.OpportunityScore,
            UsageScore = source.UsageScore,
            HealthLabel = source.HealthLabel
        };

    private static EvidenceStatus DeriveOverallStatus(
        IReadOnlyList<KnowledgeEvidence> evidence,
        int unavailableCount)
    {
        if (evidence.Any(e => e.Status == EvidenceStatus.Conflicting))
        {
            return EvidenceStatus.Conflicting;
        }

        var known = evidence.Count(e => !e.IsUnavailableMarker && e.Status == EvidenceStatus.Known);
        if (known == 0)
        {
            return EvidenceStatus.Unknown;
        }

        return unavailableCount >= 6 ? EvidenceStatus.LowConfidence : EvidenceStatus.Known;
    }

    private static int DeriveKnowledgeConfidence(
        IReadOnlyList<KnowledgeEvidence> evidence,
        int unavailableCount)
    {
        var known = evidence.Where(e => !e.IsUnavailableMarker && e.Status == EvidenceStatus.Known).ToList();
        if (known.Count == 0)
        {
            return 20;
        }

        var avg = (int)Math.Round(known.Average(e => e.Confidence));
        return Math.Clamp(avg - unavailableCount * 2, 15, 90);
    }

    private static Position? ParsePosition(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        return Enum.TryParse<Position>(label, ignoreCase: true, out var pos) ? pos : null;
    }
}
