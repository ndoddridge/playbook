using Playbook.Application.Knowledge;
using Playbook.Core.Knowledge;
using Playbook.Core.Players;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Builds deterministic historical Quick Pick predictions from a cutoff-safe snapshot.
/// Preserves Quick Picks semantics (player × counting market × projected value ranking)
/// without inventing sportsbook lines.
/// </summary>
public sealed class HistoricalQuickPickGenerator
{
    private readonly ISharedKnowledgeModel _sharedKnowledge;
    private readonly IKnowledgeImpactApplicator _knowledgeImpact;
    private readonly KnowledgeImpactExperimentState _knowledgeState;

    public HistoricalQuickPickGenerator(
        ISharedKnowledgeModel sharedKnowledge,
        IKnowledgeImpactApplicator knowledgeImpact,
        KnowledgeImpactExperimentState knowledgeState)
    {
        _sharedKnowledge = sharedKnowledge;
        _knowledgeImpact = knowledgeImpact;
        _knowledgeState = knowledgeState;
    }

    public IReadOnlyList<QuickPickHistoricalPrediction> Generate(
        HistoricalSnapshot snapshot,
        QuickPickMode mode)
    {
        var drafts = new List<(QuickPickHistoricalPrediction Pred, double SortScore)>();

        foreach (var player in snapshot.Players.OrderBy(p => p.PlayerName, StringComparer.Ordinal)
                     .ThenBy(p => p.PlayerId))
        {
            foreach (var market in MarketsFor(player.Position))
            {
                var projected = ResolveProjected(player, market);
                if (projected is null || projected <= 0)
                {
                    continue;
                }

                var confidence = player.ProjectionConfidence;
                var baseRanking = projected.Value;
                var knowledgeAttached = false;
                PredictionContext? knowledgeContext = null;
                var rankingScore = baseRanking;

                if (mode == QuickPickMode.Enhanced)
                {
                    knowledgeContext = _sharedKnowledge.BuildHistoricalPredictionContext(
                        snapshot,
                        player.PlayerId,
                        PredictionType.QuickPick);
                    knowledgeAttached = true;

                    // Bridge into the live ApplyToQuickPickPrediction surface so Enhanced
                    // uses the same frozen knowledge applicator (Allowed groups may be None).
                    var bridge = BuildBridgePrediction(
                        snapshot, player, market, projected.Value, confidence, baseRanking);
                    var adjusted = _knowledgeImpact.ApplyToQuickPickPrediction(
                        bridge, knowledgeContext);
                    // Map OpportunityScore delta back onto ranking score units.
                    var oppDelta = (double)(adjusted.OpportunityScore - bridge.OpportunityScore);
                    rankingScore = baseRanking + oppDelta;
                }

                drafts.Add((new QuickPickHistoricalPrediction
                {
                    Season = snapshot.Season,
                    Week = snapshot.Week,
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName,
                    Position = player.Position,
                    Team = player.Team,
                    PredictionType = QuickPickHistoricalGrading.PredictionTypeLabel,
                    Market = market,
                    ProjectedValue = projected.Value,
                    RankInMarket = 0, // assigned below
                    RankingScore = rankingScore,
                    Confidence = confidence,
                    KnowledgeContext = knowledgeContext,
                    KnowledgeAttached = knowledgeAttached,
                    CutoffTimestamp = snapshot.InformationCutoff,
                    Mode = mode,
                    EvaluatorVersion = FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion
                }, rankingScore));
            }
        }

        // Assign ranks within market: highest RankingScore first; ties broken by name/id.
        var result = new List<QuickPickHistoricalPrediction>();
        foreach (var marketGroup in drafts.GroupBy(d => d.Pred.Market).OrderBy(g => g.Key))
        {
            var ordered = marketGroup
                .OrderByDescending(d => d.SortScore)
                .ThenBy(d => d.Pred.PlayerName, StringComparer.Ordinal)
                .ThenBy(d => d.Pred.PlayerId)
                .ToList();

            for (var i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i].Pred;
                result.Add(new QuickPickHistoricalPrediction
                {
                    Season = p.Season,
                    Week = p.Week,
                    PlayerId = p.PlayerId,
                    PlayerName = p.PlayerName,
                    Position = p.Position,
                    Team = p.Team,
                    PredictionType = p.PredictionType,
                    Market = p.Market,
                    ProjectedValue = p.ProjectedValue,
                    RankInMarket = i + 1,
                    RankingScore = p.RankingScore,
                    Confidence = p.Confidence,
                    KnowledgeContext = p.KnowledgeContext,
                    KnowledgeAttached = p.KnowledgeAttached,
                    CutoffTimestamp = p.CutoffTimestamp,
                    Mode = p.Mode,
                    EvaluatorVersion = p.EvaluatorVersion
                });
            }
        }

        return result
            .OrderBy(p => p.Market)
            .ThenBy(p => p.RankInMarket)
            .ThenBy(p => p.PlayerName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Exposes current knowledge mode for audits (set by runner before Generate).</summary>
    public KnowledgeMode CurrentKnowledgeMode => _knowledgeState.Mode;

    public KnowledgeImpactGroup CurrentActiveGroups => _knowledgeState.ActiveGroups;

    private static IEnumerable<PredictionMarketType> MarketsFor(Position position) =>
        position switch
        {
            Position.QB => [PredictionMarketType.PassingYards],
            Position.RB =>
            [
                PredictionMarketType.RushingYards,
                PredictionMarketType.ReceivingYards,
                PredictionMarketType.Receptions
            ],
            Position.WR or Position.TE =>
            [
                PredictionMarketType.ReceivingYards,
                PredictionMarketType.Receptions
            ],
            _ => []
        };

    private static double? ResolveProjected(HistoricalPlayerState player, PredictionMarketType market) =>
        market switch
        {
            PredictionMarketType.PassingYards => player.ProjectedPassYards,
            PredictionMarketType.RushingYards => player.ProjectedRushYards,
            PredictionMarketType.ReceivingYards => player.ProjectedReceivingYards,
            PredictionMarketType.Receptions => player.ProjectedReceptions,
            PredictionMarketType.PassingTouchdowns => player.ProjectedPassTouchdowns,
            _ => null
        };

    private static Prediction BuildBridgePrediction(
        HistoricalSnapshot snapshot,
        HistoricalPlayerState player,
        PredictionMarketType market,
        double projected,
        int? confidence,
        double rankingScore)
    {
        // OpportunityScore bridge: clamp ranking-derived score into 0–100 for applicator deltas.
        var opp = (decimal)Math.Clamp(rankingScore, 0, 100);
        var eventId =
            $"hist-qp-{snapshot.Season}-w{snapshot.Week}-{player.PlayerId:N}-{market}";
        // Deterministic prediction id from event + market (no Guid.NewGuid).
        var predId = CreateDeterministicGuid(eventId);
        var evt = new FootballEvent
        {
            EventId = eventId,
            Season = snapshot.Season,
            Phase = NflSeasonPhase.RegularSeason,
            Week = snapshot.Week,
            HomeTeam = player.Team ?? "HOME",
            AwayTeam = "AWAY",
            CommenceTime = snapshot.InformationCutoff
        };

        return new Prediction
        {
            Id = predId,
            Event = evt,
            PlayerId = player.PlayerId,
            PlayerName = player.PlayerName,
            TeamName = player.Team,
            Market = market,
            Line = null,
            PlaybookProjection = (decimal)projected,
            Probability = 50,
            Edge = 0m,
            Confidence = confidence ?? 50,
            Direction = PredictionDirection.Over,
            Reasoning = "Historical Quick Pick bridge prediction (no sportsbook line).",
            SupportingIntelligence = [],
            CalculationNotes =
            [
                $"Historical QP generator {FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion}",
                $"Projected={projected:0.##}",
                $"BridgeOpportunityScore={opp:0.##}"
            ],
            Source = "historical-quick-picks",
            LineFreshness = PropLineFreshness.Mock,
            LastUpdated = snapshot.InformationCutoff,
            EngineVersion = FrozenQuickPicksHistoricalEvaluationV1.EvaluatorVersion,
            OpportunityScore = opp
        };
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
