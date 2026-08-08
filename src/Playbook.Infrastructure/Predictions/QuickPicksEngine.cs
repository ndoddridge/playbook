using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Predictions;
using Playbook.Core.Projections.Models;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Quick Picks Engine v0.1 — deterministic, explainable edge vs market lines.
///
/// V1 formula (documented):
/// 1. rawDiff = projection − line
/// 2. scale = typical market unit (e.g. 22 receiving yards)
/// 3. quality = (confidence/100) × (1 − volatility/200) × healthFactor
/// 4. signedEdge = rawDiff × quality
/// 5. direction = Over/Yes when signedEdge ≥ 0, else Under/No
/// 6. probability = 50 + clamp(|signedEdge| / scale × 28, 0, 35)
/// 7. pickConfidence = confidence × √quality (− stale penalty)
///
/// Small diffs or low quality are dampened so projection &gt; line alone is not enough.
/// </summary>
public sealed class QuickPicksEngine : IQuickPicksEngine
{
    public const string CurrentVersion = "0.1";

    public string Version => CurrentVersion;

    public Prediction? Evaluate(
        PropLine line,
        decimal? playbookProjection,
        int projectionConfidence,
        int volatility,
        PlayerIntelligenceProfile? intelligence,
        PlayerStatisticalContext? statisticalContext,
        string? injuryNote)
    {
        if (line.Freshness == PropLineFreshness.Unavailable)
        {
            return null;
        }

        var isGameMarket = line.Market is PredictionMarketType.GameTotal
            or PredictionMarketType.TeamTotal
            or PredictionMarketType.Winner
            or PredictionMarketType.Spread;

        if (playbookProjection is null && !isGameMarket)
        {
            return null;
        }

        var calc = new List<string>();
        var supporting = new List<string>();

        var lineValue = ResolveComparableLine(line);
        if (lineValue is null && line.Market != PredictionMarketType.Winner)
        {
            return null;
        }

        var projection = playbookProjection ?? lineValue ?? 0.5m;
        var comparableLine = lineValue ?? 0.5m;

        calc.Add($"Market line: {FormatValue(line.Market, comparableLine)}");
        calc.Add($"Playbook projection: {FormatValue(line.Market, projection)}");
        calc.Add($"Projection confidence: {projectionConfidence}% · Volatility: {volatility}");

        var rawDiff = projection - comparableLine;
        calc.Add($"Raw difference: {rawDiff:+0.0;-0.0;0.0}");

        var healthFactor = 1m;
        if (intelligence is not null)
        {
            healthFactor = intelligence.HealthScore switch
            {
                < 25 => 0.55m,
                < 40 => 0.72m,
                < 55 => 0.88m,
                > 75 => 1.05m,
                _ => 1.0m
            };
            supporting.Add(
                $"Health score {intelligence.HealthScore}, usage {intelligence.UsageScore}, opportunity {intelligence.OpportunityScore}");
            calc.Add($"Health factor: {healthFactor:0.00}");
        }
        else
        {
            supporting.Add("No player intelligence profile — neutral health/usage assumptions.");
            calc.Add("Health factor: 1.00 (no intelligence profile)");
        }

        if (!string.IsNullOrWhiteSpace(injuryNote))
        {
            supporting.Add(injuryNote);
            healthFactor *= 0.8m;
            calc.Add("Injury concern applied (×0.80 quality).");
        }

        if (statisticalContext?.Usage is { } usage)
        {
            supporting.Add(
                $"Recent usage — targets/g {usage.TargetsPerGame?.ToString("0.0") ?? "—"}, " +
                $"carries/g {usage.CarriesPerGame?.ToString("0.0") ?? "—"}, workload {usage.WorkloadTrend ?? "—"}");
        }

        var quality = (projectionConfidence / 100m) * (1m - volatility / 200m) * healthFactor;
        quality = Math.Clamp(quality, 0.05m, 1.15m);
        calc.Add($"Quality weight: {quality:0.00}");

        var scale = MarketScale(line.Market);
        var signedEdge = Math.Round(rawDiff * quality, 1, MidpointRounding.AwayFromZero);

        if (Math.Abs(rawDiff) < scale * 0.08m || quality < 0.25m)
        {
            signedEdge = Math.Round(signedEdge * 0.35m, 1, MidpointRounding.AwayFromZero);
            calc.Add("Small difference or low quality — edge dampened ×0.35.");
        }

        calc.Add($"Adjusted signed edge: {signedEdge:+0.0;-0.0;0.0}");

        var direction = ResolveDirection(line.Market, signedEdge);
        var absEdge = Math.Abs(signedEdge);
        var probability = (int)Math.Round(
            50m + Math.Clamp(absEdge / scale * 28m, 0m, 35m),
            MidpointRounding.AwayFromZero);
        probability = Math.Clamp(probability, 15, 88);

        var pickConfidence = (int)Math.Round(
            projectionConfidence * (decimal)Math.Sqrt((double)Math.Clamp(quality, 0.05m, 1m)));
        pickConfidence = Math.Clamp(pickConfidence, 12, 92);

        if (line.Freshness == PropLineFreshness.Stale)
        {
            pickConfidence = Math.Max(12, pickConfidence - 18);
            probability = Math.Max(15, probability - 6);
            supporting.Add("Market line is stale — confidence reduced.");
            calc.Add("Stale-line penalty: −18 confidence, −6 probability.");
        }

        if (line.Freshness == PropLineFreshness.Mock)
        {
            supporting.Add("Using mock market line (development).");
        }

        var reasoning = BuildHumanReasoning(
            line, projection, comparableLine, direction, absEdge, probability, intelligence, injuryNote);

        return new Prediction
        {
            Id = CreateId(line),
            Event = line.Event,
            PlayerId = line.PlayerId,
            PlayerName = line.PlayerName,
            TeamName = line.TeamName,
            Market = line.Market,
            Line = line.Line,
            PlaybookProjection = playbookProjection,
            Probability = probability,
            Edge = absEdge,
            Confidence = pickConfidence,
            Direction = direction,
            Reasoning = reasoning,
            SupportingIntelligence = supporting,
            CalculationNotes = calc,
            Source = line.Source,
            LineFreshness = line.Freshness,
            LastUpdated = DateTimeOffset.UtcNow,
            Bookmaker = line.Bookmaker
        };
    }

    private static decimal? ResolveComparableLine(PropLine line)
    {
        if (line.Market == PredictionMarketType.AnytimeTouchdown)
        {
            return line.Line ?? 0.5m;
        }

        if (line.Market == PredictionMarketType.Winner)
        {
            return 0.5m;
        }

        return line.Line;
    }

    private static PredictionDirection ResolveDirection(PredictionMarketType market, decimal signedEdge) =>
        market switch
        {
            PredictionMarketType.AnytimeTouchdown =>
                signedEdge >= 0 ? PredictionDirection.Yes : PredictionDirection.No,
            PredictionMarketType.Winner =>
                signedEdge >= 0 ? PredictionDirection.Home : PredictionDirection.Away,
            PredictionMarketType.Spread =>
                signedEdge >= 0 ? PredictionDirection.Cover : PredictionDirection.NotCover,
            _ => signedEdge >= 0 ? PredictionDirection.Over : PredictionDirection.Under
        };

    private static decimal MarketScale(PredictionMarketType market) => market switch
    {
        PredictionMarketType.PassingYards => 35m,
        PredictionMarketType.RushingYards => 20m,
        PredictionMarketType.ReceivingYards => 22m,
        PredictionMarketType.Receptions => 2.5m,
        PredictionMarketType.PassingTouchdowns => 0.75m,
        PredictionMarketType.AnytimeTouchdown => 0.35m,
        PredictionMarketType.GameTotal => 6m,
        PredictionMarketType.TeamTotal => 4m,
        PredictionMarketType.Spread => 3m,
        PredictionMarketType.Winner => 0.15m,
        _ => 10m
    };

    private static string FormatValue(PredictionMarketType market, decimal? value)
    {
        if (value is null)
        {
            return "—";
        }

        return market == PredictionMarketType.AnytimeTouchdown
            ? value.Value.ToString("0.00")
            : value.Value.ToString("0.0");
    }

    private static string MarketUnit(PredictionMarketType market) => market switch
    {
        PredictionMarketType.PassingYards or PredictionMarketType.RushingYards
            or PredictionMarketType.ReceivingYards => " yards",
        PredictionMarketType.Receptions => " receptions",
        PredictionMarketType.PassingTouchdowns => " TDs",
        PredictionMarketType.GameTotal or PredictionMarketType.TeamTotal => " points",
        PredictionMarketType.Spread => " points",
        _ => ""
    };

    private static string BuildHumanReasoning(
        PropLine line,
        decimal projection,
        decimal lineValue,
        PredictionDirection direction,
        decimal edge,
        int probability,
        PlayerIntelligenceProfile? intelligence,
        string? injuryNote)
    {
        var subject = line.PlayerName ?? line.TeamName ?? line.Event.DisplayName;
        var market = line.MarketLabel.ToLowerInvariant();
        var side = direction switch
        {
            PredictionDirection.Over => "Over",
            PredictionDirection.Under => "Under",
            PredictionDirection.Yes => "Yes",
            PredictionDirection.No => "No",
            PredictionDirection.Cover => "cover",
            PredictionDirection.NotCover => "not cover",
            PredictionDirection.Home => "home",
            PredictionDirection.Away => "away",
            _ => direction.ToString()
        };

        var delta = Math.Abs(projection - lineValue);
        var relation = projection >= lineValue ? "above" : "below";
        var unit = MarketUnit(line.Market);
        var core =
            $"Playbook projects {FormatValue(line.Market, projection)} {market} for {subject}, " +
            $"about {delta:0.0}{unit} {relation} the current line ({FormatValue(line.Market, lineValue)}). " +
            $"That favors {side} at roughly {probability}% estimated probability.";

        if (intelligence is not null)
        {
            if (intelligence.UsageScore >= 60)
            {
                core += " Recent usage looks constructive.";
            }
            else if (intelligence.UsageScore <= 40)
            {
                core += " Recent usage is softer than usual.";
            }

            if (intelligence.HealthScore >= 65)
            {
                core += " No meaningful health concerns right now.";
            }
            else if (intelligence.HealthScore <= 40)
            {
                core += " Health/availability risk is tempering the edge.";
            }
        }

        if (!string.IsNullOrWhiteSpace(injuryNote))
        {
            core += $" Injury note: {injuryNote}.";
        }

        if (line.Freshness == PropLineFreshness.Stale)
        {
            core += " The posted line looks stale, so treat this cautiously.";
        }

        return core;
    }

    private static Guid CreateId(PropLine line)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"playbook:prediction:{line.Id}:{line.Market}"));
        return new Guid(bytes);
    }
}
