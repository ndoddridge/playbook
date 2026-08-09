using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Predictions;

namespace Playbook.Infrastructure.Predictions;

/// <summary>
/// Quick Picks Engine v0.3 — week-scoped, intelligence-driven prop recommendations.
///
/// Combines projection-vs-line with health, usage/opportunity, historical injury
/// (relevance + age decay), unconfirmed buzz (labeled, soft), and intel confidence.
/// Preseason treats regular-season production as a prior only. Missing signals reduce
/// confidence — they are never fabricated. Weights: <see cref="QuickPicksScoringOptions"/>.
/// </summary>
public sealed class QuickPicksEngine : IQuickPicksEngine
{
    public const string CurrentVersion = "0.3";

    private readonly QuickPicksScoringOptions _options;

    public QuickPicksEngine(IOptions<QuickPicksScoringOptions> options)
    {
        _options = options.Value;
    }

    public string Version => CurrentVersion;

    public Prediction? Evaluate(QuickPickEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var line = context.Line;

        if (line.Freshness == PropLineFreshness.Unavailable)
        {
            return null;
        }

        var isGameMarket = line.Market is PredictionMarketType.GameTotal
            or PredictionMarketType.TeamTotal
            or PredictionMarketType.Winner
            or PredictionMarketType.Spread;

        if (context.PlaybookProjection is null && !isGameMarket)
        {
            return null;
        }

        var calc = new List<string>();
        var supporting = new List<string>();
        var contributions = new List<PredictionSignalContribution>();

        var lineValue = ResolveComparableLine(line);
        if (lineValue is null && line.Market != PredictionMarketType.Winner)
        {
            return null;
        }

        var projection = context.PlaybookProjection ?? lineValue ?? 0.5m;
        var comparableLine = lineValue ?? 0.5m;
        var scale = MarketScale(line.Market);
        var rawDiff = projection - comparableLine;

        calc.Add($"Engine v{CurrentVersion}");
        calc.Add($"Slate: {line.Event.ContextLabel}");
        calc.Add($"Market line: {FormatValue(line.Market, comparableLine)}");
        calc.Add($"Playbook projection: {FormatValue(line.Market, projection)}");
        calc.Add($"Projection confidence: {context.ProjectionConfidence}% · Volatility: {context.Volatility}");
        calc.Add($"Raw difference: {rawDiff:+0.0;-0.0;0.0}");

        var quality = 1m;
        var confidenceDelta = 0;
        var edgeBias = 0m;

        // --- Projection confidence & volatility ---
        var projQ = Math.Clamp(context.ProjectionConfidence / 100m, 0.05m, 1.2m);
        var volQ = Math.Clamp(1m - context.Volatility / 200m, 0.35m, 1.1m);
        quality *= projQ * volQ;
        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "projection-vs-line",
            Label = "Projection vs line",
            Available = context.PlaybookProjection is not null,
            Weight = 1m,
            QualityMultiplier = projQ * volQ,
            Detail = context.PlaybookProjection is null
                ? "No counting-stat projection for this market."
                : $"Diff {rawDiff:+0.0;-0.0;0.0} · conf {context.ProjectionConfidence}% · vol {context.Volatility}"
        });

        // --- Preseason prior production (explicit — not treated as current form) ---
        ApplyPreseasonPrior(context, ref quality, ref confidenceDelta, contributions, supporting, calc);

        // --- Player intelligence confidence ---
        ApplyIntelligenceConfidence(
            context, isGameMarket, ref quality, ref confidenceDelta, contributions, supporting, calc);

        // --- Health score (profile) ---
        ApplyHealthScore(context, ref quality, contributions, supporting, calc);

        // --- Current verified injury (strong) ---
        ApplyCurrentInjury(
            context, scale, rawDiff, ref quality, ref confidenceDelta, ref edgeBias,
            contributions, supporting, calc);

        // --- Historical injury (weak, relevance + age) ---
        ApplyHistoricalInjury(context, ref quality, ref confidenceDelta, contributions, supporting, calc);

        // --- Unconfirmed buzz (soft, labeled) ---
        ApplyUnconfirmed(context, ref quality, ref confidenceDelta, contributions, supporting, calc);

        // --- Usage / opportunity ---
        ApplyUsageOpportunity(
            context, scale, rawDiff, ref quality, ref confidenceDelta, ref edgeBias,
            contributions, supporting, calc);

        // --- Recent news/facts (capped — cannot dominate) ---
        ApplyNewsFacts(context, ref quality, ref confidenceDelta, contributions, supporting, calc);

        quality = Math.Clamp(quality, 0.05m, 1.20m);
        calc.Add($"Composite quality: {quality:0.00}");
        calc.Add($"Edge bias: {edgeBias:+0.0;-0.0;0.0}");

        var signedEdge = Math.Round((rawDiff + edgeBias) * quality, 1, MidpointRounding.AwayFromZero);
        if (Math.Abs(rawDiff) < scale * _options.SmallDiffScaleFraction || quality < _options.LowQualityThreshold)
        {
            signedEdge = Math.Round(signedEdge * _options.LowQualityDampener, 1, MidpointRounding.AwayFromZero);
            calc.Add($"Small difference or low quality — edge dampened ×{_options.LowQualityDampener:0.00}.");
        }

        calc.Add($"Adjusted signed edge: {signedEdge:+0.0;-0.0;0.0}");

        var direction = ResolveDirection(line.Market, signedEdge);
        var absEdge = Math.Abs(signedEdge);
        var probability = (int)Math.Round(
            50m + Math.Clamp(absEdge / scale * _options.ProbabilityEdgeScale, 0m, 35m),
            MidpointRounding.AwayFromZero);
        probability = Math.Clamp(probability, 15, 88);

        // Blend projection confidence with signal quality; available intelligence can lift floor.
        var intelLift = context.Intelligence is null ? 0 : Math.Max(0, context.Intelligence.OverallConfidence - 55) / 4;
        var pickConfidence = (int)Math.Round(
            context.ProjectionConfidence * (0.55m + 0.45m * (decimal)Math.Sqrt((double)Math.Clamp(quality, 0.05m, 1m)))
            + confidenceDelta
            + intelLift);
        pickConfidence = Math.Clamp(pickConfidence, 12, 92);

        if (line.Freshness == PropLineFreshness.Stale)
        {
            pickConfidence = Math.Max(12, pickConfidence - _options.StaleConfidencePenalty);
            probability = Math.Max(15, probability - _options.StaleProbabilityPenalty);
            supporting.Add("Market line is stale — confidence reduced.");
            calc.Add(
                $"Stale-line penalty: −{_options.StaleConfidencePenalty} confidence, −{_options.StaleProbabilityPenalty} probability.");
        }

        if (line.Freshness == PropLineFreshness.Mock)
        {
            supporting.Add("Using mock market line (development).");
        }

        var reasoning = BuildHumanReasoning(
            line, projection, comparableLine, direction, probability, context, contributions);

        var opportunity = Math.Round(
            absEdge * (pickConfidence / 100m) * (0.75m + (probability / 100m) * 0.5m),
            2,
            MidpointRounding.AwayFromZero);

        return new Prediction
        {
            Id = CreateId(line),
            Event = line.Event,
            PlayerId = line.PlayerId,
            PlayerName = line.PlayerName,
            TeamName = line.TeamName,
            Market = line.Market,
            Line = line.Line,
            PlaybookProjection = context.PlaybookProjection,
            Probability = probability,
            Edge = absEdge,
            Confidence = pickConfidence,
            Direction = direction,
            Reasoning = reasoning,
            SupportingIntelligence = supporting,
            SignalContributions = contributions,
            CalculationNotes = calc,
            Source = line.Source,
            LineFreshness = line.Freshness,
            LastUpdated = DateTimeOffset.UtcNow,
            LineUpdatedAt = line.UpdatedAt,
            Bookmaker = line.Bookmaker,
            EngineVersion = CurrentVersion,
            OpportunityScore = opportunity
        };
    }

    private void ApplyPreseasonPrior(
        QuickPickEvaluationContext context,
        ref decimal quality,
        ref int confidenceDelta,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        if (context.SeasonPhase != NflSeasonPhase.Preseason)
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "season-phase",
                Label = "Season phase",
                Available = true,
                Weight = 1m,
                Detail = context.SeasonPhase == NflSeasonPhase.Postseason
                    ? "Postseason slate"
                    : "Regular-season slate"
            });
            return;
        }

        var qMult = context.UsingPriorRegularSeasonProduction
            ? _options.PreseasonPriorProductionQualityFactor
            : 0.92m;
        quality *= qMult;
        confidenceDelta -= _options.PreseasonConfidencePenalty;
        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "season-phase",
            Label = "Preseason context",
            Available = true,
            Weight = 1m,
            QualityMultiplier = qMult,
            ConfidenceDelta = -_options.PreseasonConfidencePenalty,
            Detail = context.UsingPriorRegularSeasonProduction
                ? "Regular-season production used as a prior only — not treated as current preseason form."
                : "Preseason slate — limited sample; confidence tempered."
        });
        supporting.Add(context.UsingPriorRegularSeasonProduction
            ? "Prior regular-season production only (preseason form not equated)."
            : "Preseason context — conviction tempered.");
        calc.Add($"Preseason phase: quality ×{qMult:0.00}, conf −{_options.PreseasonConfidencePenalty}");
    }

    private void ApplyIntelligenceConfidence(
        QuickPickEvaluationContext context,
        bool isGameMarket,
        ref decimal quality,
        ref int confidenceDelta,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var intel = context.Intelligence;
        if (intel is null)
        {
            if (!isGameMarket && context.Line.PlayerId is not null)
            {
                var q = _options.MissingIntelligenceQualityFactor;
                quality *= q;
                confidenceDelta -= _options.MissingIntelligenceConfidencePenalty;
                contributions.Add(new PredictionSignalContribution
                {
                    SignalId = "intelligence-confidence",
                    Label = "Player intelligence",
                    Available = false,
                    Weight = _options.IntelligenceConfidenceWeight,
                    QualityMultiplier = q,
                    ConfidenceDelta = -_options.MissingIntelligenceConfidencePenalty,
                    Detail = "No intelligence profile — confidence reduced (no fabricated signals)."
                });
                supporting.Add("Player intelligence unavailable — confidence reduced.");
                calc.Add($"Missing intelligence: quality ×{q:0.00}, conf −{_options.MissingIntelligenceConfidencePenalty}");
            }
            else
            {
                contributions.Add(new PredictionSignalContribution
                {
                    SignalId = "intelligence-confidence",
                    Label = "Player intelligence",
                    Available = false,
                    Weight = _options.IntelligenceConfidenceWeight,
                    Detail = "Not applicable / unavailable for this market."
                });
            }

            return;
        }

        var confNorm = Math.Clamp(intel.OverallConfidence / 100m, 0m, 1m);
        var qMult = 1m + _options.IntelligenceConfidenceWeight * (confNorm - 0.5m) * 2m * 0.12m;
        qMult = Math.Clamp(qMult, 0.80m, 1.12m);
        quality *= qMult;
        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "intelligence-confidence",
            Label = "Intelligence confidence",
            Available = true,
            Weight = _options.IntelligenceConfidenceWeight,
            QualityMultiplier = qMult,
            Detail = $"Overall confidence {intel.OverallConfidence}%"
        });
        calc.Add($"Intelligence confidence factor: {qMult:0.00}");
    }

    private void ApplyHealthScore(
        QuickPickEvaluationContext context,
        ref decimal quality,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var intel = context.Intelligence;
        if (intel is null)
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "health-score",
                Label = "Health score",
                Available = false,
                Weight = _options.HealthScoreWeight,
                Detail = "Health score unavailable."
            });
            return;
        }

        var baseFactor = intel.HealthScore switch
        {
            < 25 => 0.55m,
            < 40 => 0.72m,
            < 55 => 0.88m,
            > 75 => 1.05m,
            _ => 1.0m
        };
        var qMult = 1m + (baseFactor - 1m) * _options.HealthScoreWeight;
        quality *= qMult;
        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "health-score",
            Label = "Health score",
            Available = true,
            Weight = _options.HealthScoreWeight,
            QualityMultiplier = qMult,
            Detail = $"Health {intel.HealthScore}"
        });
        supporting.Add($"Health score {intel.HealthScore}.");
        calc.Add($"Health factor: {qMult:0.00}");
    }

    private void ApplyCurrentInjury(
        QuickPickEvaluationContext context,
        decimal scale,
        decimal rawDiff,
        ref decimal quality,
        ref int confidenceDelta,
        ref decimal edgeBias,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var current = context.InjuryProfile?.CurrentInjury;
        if (current is null ||
            string.Equals(current.Status, "Active", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current.Status, "Healthy", StringComparison.OrdinalIgnoreCase))
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "current-injury",
                Label = "Current injury",
                Available = current is not null,
                Weight = _options.CurrentInjuryWeight,
                Detail = current is null
                    ? (context.Line.PlayerId is null
                        ? "No player-linked injury context."
                        : "No verified current injury designation.")
                    : $"Status {current.Status} (not a limiting designation)."
            });
            if (current is null && context.Line.PlayerId is not null)
            {
                supporting.Add("No verified current injury designation.");
            }

            return;
        }

        var rule = InjuryIntelligenceMapping.ResolveRuleId(current);
        var healthMult = InjuryIntelligenceMapping.ProjectionHealthMultiplier(current) ?? 0.92m;
        // Map health multiplier into quality strongly when current injury weight is high.
        var qMult = 1m - (1m - healthMult) * Math.Clamp(_options.CurrentInjuryWeight, 0m, 1.25m);
        qMult = Math.Clamp(qMult, 0.12m, 1.0m);
        quality *= qMult;

        var bias = 0m;
        if (rawDiff > 0 && rule is "injury-out" or "injury-ir" or "injury-doubtful")
        {
            bias = -scale * _options.SevereInjuryOverBiasScale * _options.CurrentInjuryWeight;
            edgeBias += bias;
        }
        else if (rawDiff > 0 && rule is "injury-questionable" or "injury-limited")
        {
            bias = -scale * 0.25m * _options.CurrentInjuryWeight;
            edgeBias += bias;
        }

        if (rule is "injury-out" or "injury-ir")
        {
            confidenceDelta -= 12;
        }
        else if (rule is "injury-doubtful" or "injury-questionable")
        {
            confidenceDelta -= 6;
        }

        var label =
            $"{current.Status}" +
            (string.IsNullOrWhiteSpace(current.BodyPart) ? "" : $" ({current.BodyPart})");
        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "current-injury",
            Label = "Current injury",
            Available = true,
            Weight = _options.CurrentInjuryWeight,
            QualityMultiplier = qMult,
            ConfidenceDelta = rule is "injury-out" or "injury-ir" ? -12 :
                rule is "injury-doubtful" or "injury-questionable" ? -6 : 0,
            EdgeBias = bias,
            Detail = label
        });
        supporting.Add($"Current injury: {label}.");
        calc.Add($"Current injury ({rule ?? "n/a"}): quality ×{qMult:0.00}, edge bias {bias:+0.0;-0.0;0.0}");
    }

    private void ApplyHistoricalInjury(
        QuickPickEvaluationContext context,
        ref decimal quality,
        ref int confidenceDelta,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var profile = context.InjuryProfile;
        if (profile is null)
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "historical-injury",
                Label = "Historical injury",
                Available = false,
                Weight = _options.HistoricalInjuryWeight,
                Detail = "Injury profile unavailable."
            });
            return;
        }

        if (profile.HistoricalDataStatus is HistoricalDataStatus.Unavailable
            or HistoricalDataStatus.NotSupportedByProvider)
        {
            confidenceDelta -= 2;
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "historical-injury",
                Label = "Historical injury",
                Available = false,
                Weight = _options.HistoricalInjuryWeight,
                ConfidenceDelta = -2,
                Detail = "Historical injury data unavailable — slight confidence reduction."
            });
            supporting.Add("Historical injury data unavailable.");
            return;
        }

        var relevant = profile.RecentHistory
            .Where(e => e.Band is InjuryRelevanceBand.High or InjuryRelevanceBand.Moderate)
            .OrderByDescending(e => e.RelevanceScore)
            .Take(2)
            .ToList();

        if (relevant.Count == 0)
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "historical-injury",
                Label = "Historical injury",
                Available = true,
                Weight = _options.HistoricalInjuryWeight,
                Detail = "No high/moderate relevance historical injuries in recent window."
            });
            return;
        }

        var drag = 0m;
        foreach (var entry in relevant)
        {
            var years = Math.Max(0, (DateTimeOffset.UtcNow - entry.Record.Date).TotalDays / 365.25);
            var ageWeight = Math.Clamp(
                1m - (decimal)years * _options.HistoricalInjuryDecayPerYear,
                0.12m,
                1m);
            var bandStrength = entry.Band == InjuryRelevanceBand.High ? 0.10m : 0.05m;
            drag += bandStrength * ageWeight * _options.HistoricalInjuryWeight;

            var when = entry.Record.Date.ToString("MMM yyyy");
            var part = string.IsNullOrWhiteSpace(entry.Record.BodyPart) ? "injury" : entry.Record.BodyPart!;
            supporting.Add($"Relevant history ({entry.Band}, aged): {part} — {when}.");
        }

        drag = Math.Clamp(drag, 0m, 0.18m);
        var qMult = 1m - drag;
        quality *= qMult;
        confidenceDelta -= drag > 0.08m ? 4 : 2;
        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "historical-injury",
            Label = "Historical injury",
            Available = true,
            Weight = _options.HistoricalInjuryWeight,
            QualityMultiplier = qMult,
            ConfidenceDelta = drag > 0.08m ? -4 : -2,
            Detail = $"{relevant.Count} relevant historical event(s); age-weighted."
        });
        calc.Add($"Historical injury drag: quality ×{qMult:0.00}");
    }

    private void ApplyUnconfirmed(
        QuickPickEvaluationContext context,
        ref decimal quality,
        ref int confidenceDelta,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var signals = context.InjuryProfile?.UnconfirmedSignals ?? [];
        if (signals.Count == 0)
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "unconfirmed-injury-buzz",
                Label = "Unconfirmed injury buzz",
                Available = false,
                Weight = _options.UnconfirmedSignalWeight,
                IsUnconfirmed = true,
                Detail = "No unconfirmed injury buzz."
            });
            return;
        }

        // Cap influence — a single unconfirmed item must not dominate.
        var top = signals.OrderByDescending(s => s.Confidence).Take(2).ToList();
        var avgConf = top.Average(s => s.Confidence) / 100.0;
        var drag = Math.Min(
            _options.UnconfirmedMaxQualityDrag,
            (decimal)avgConf * _options.UnconfirmedSignalWeight * 0.35m);
        var qMult = 1m - drag;
        quality *= qMult;
        confidenceDelta -= _options.UnconfirmedConfidencePenalty;

        foreach (var signal in top)
        {
            supporting.Add($"Unconfirmed: {signal.Headline}");
        }

        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "unconfirmed-injury-buzz",
            Label = "Unconfirmed injury buzz",
            Available = true,
            Weight = _options.UnconfirmedSignalWeight,
            QualityMultiplier = qMult,
            ConfidenceDelta = -_options.UnconfirmedConfidencePenalty,
            IsUnconfirmed = true,
            Detail = $"{top.Count} unconfirmed report(s); soft confidence drag only."
        });
        calc.Add($"Unconfirmed buzz: quality ×{qMult:0.00} (labeled unconfirmed, not treated as fact)");
    }

    private void ApplyUsageOpportunity(
        QuickPickEvaluationContext context,
        decimal scale,
        decimal rawDiff,
        ref decimal quality,
        ref int confidenceDelta,
        ref decimal edgeBias,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var intel = context.Intelligence;
        var usage = context.StatisticalContext?.Usage;
        var hasUsage = intel is not null || usage is not null;

        if (!hasUsage)
        {
            if (context.Line.PlayerId is not null)
            {
                confidenceDelta -= 3;
            }

            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "usage-opportunity",
                Label = "Usage / opportunity",
                Available = false,
                Weight = _options.UsageSignalWeight,
                ConfidenceDelta = context.Line.PlayerId is not null ? -3 : 0,
                Detail = "Usage/opportunity signals unavailable."
            });
            if (context.Line.PlayerId is not null)
            {
                supporting.Add("Usage/opportunity data unavailable.");
            }

            return;
        }

        var usageScore = intel?.UsageScore ?? 50;
        var oppScore = intel?.OpportunityScore ?? 50;
        var usageTilt = (usageScore - 50) / 50m;
        var oppTilt = (oppScore - 50) / 50m;

        var qMult = 1m
                    + usageTilt * 0.06m * _options.UsageSignalWeight
                    + oppTilt * 0.05m * _options.OpportunitySignalWeight;
        qMult = Math.Clamp(qMult, 0.85m, 1.12m);
        quality *= qMult;

        // Usage/opportunity must move the edge, not only the quality weight.
        var bias = scale * 0.16m * (usageTilt * _options.UsageSignalWeight * 0.65m
                                    + oppTilt * _options.OpportunitySignalWeight * 0.55m);
        if (context.SeasonPhase == NflSeasonPhase.Preseason)
        {
            // Current opportunity intel is especially valuable before regular-season samples exist.
            bias *= 1.25m;
        }

        edgeBias += bias;

        var workload = usage?.WorkloadTrend;
        var detail =
            $"Usage {usageScore}, opportunity {oppScore}" +
            (workload is null ? "" : $", workload {workload}");
        if (usage?.TargetsPerGame is decimal tgt)
        {
            detail += $", targets/g {tgt:0.0}";
        }

        if (usage?.CarriesPerGame is decimal car)
        {
            detail += $", carries/g {car:0.0}";
        }

        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "usage-opportunity",
            Label = "Usage / opportunity",
            Available = true,
            Weight = (_options.UsageSignalWeight + _options.OpportunitySignalWeight) / 2m,
            QualityMultiplier = qMult,
            EdgeBias = bias,
            Detail = detail
        });
        supporting.Add(detail + ".");
        calc.Add($"Usage/opportunity: quality ×{qMult:0.00}, edge bias {bias:+0.0;-0.0;0.0}");
    }

    private void ApplyNewsFacts(
        QuickPickEvaluationContext context,
        ref decimal quality,
        ref int confidenceDelta,
        List<PredictionSignalContribution> contributions,
        List<string> supporting,
        List<string> calc)
    {
        var facts = context.RecentFacts
            .OrderByDescending(f => f.Importance)
            .ThenByDescending(f => f.Confidence)
            .Take(3)
            .ToList();

        if (facts.Count == 0)
        {
            contributions.Add(new PredictionSignalContribution
            {
                SignalId = "recent-intelligence",
                Label = "Recent intelligence",
                Available = false,
                Weight = _options.NewsSignalWeight,
                Detail = "No recent intelligence facts for this player."
            });
            return;
        }

        // Cap: one fact cannot dominate. Unconfirmed facts stay labeled.
        var primary = facts[0];
        var isUnconfirmed = primary.Tags.Any(t =>
                                t.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase))
                            || primary.SupportingEvidence.Any(e =>
                                e.Contains("Unconfirmed", StringComparison.OrdinalIgnoreCase));

        var dragOrBoost = Math.Clamp(
            (primary.Confidence - 50) / 50m * 0.04m * _options.NewsSignalWeight,
            -_options.MaxSingleNewsQualityDrag,
            _options.MaxSingleNewsQualityDrag);

        if (isUnconfirmed)
        {
            // Unconfirmed news: confidence softener only, never a strong directional claim.
            dragOrBoost = -Math.Abs(dragOrBoost);
            confidenceDelta -= 3;
        }

        var qMult = 1m + dragOrBoost;
        quality *= qMult;

        var label = isUnconfirmed
            ? $"Unconfirmed: {Trim(primary.Title, 90)}"
            : Trim(primary.Title, 90);
        supporting.Add(isUnconfirmed ? label : $"Intel: {label}");

        contributions.Add(new PredictionSignalContribution
        {
            SignalId = "recent-intelligence",
            Label = "Recent intelligence",
            Available = true,
            Weight = _options.NewsSignalWeight,
            QualityMultiplier = qMult,
            ConfidenceDelta = isUnconfirmed ? -3 : 0,
            IsUnconfirmed = isUnconfirmed,
            Detail = label
        });
        calc.Add($"Recent intelligence (capped): quality ×{qMult:0.00}" +
                 (isUnconfirmed ? " [unconfirmed]" : ""));
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

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
        int probability,
        QuickPickEvaluationContext context,
        List<PredictionSignalContribution> contributions)
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
            $"about {delta:0.0}{unit} {relation} the line ({FormatValue(line.Market, lineValue)}) — " +
            $"favoring {side} (~{probability}%).";

        if (context.SeasonPhase == NflSeasonPhase.Preseason)
        {
            core += context.UsingPriorRegularSeasonProduction
                ? " Preseason slate: regular-season production is a prior only."
                : " Preseason slate: limited sample tempered confidence.";
        }

        var current = contributions.FirstOrDefault(c => c.SignalId == "current-injury" && c.Available);
        if (current?.Detail is { } injuryDetail &&
            !injuryDetail.Contains("not a limiting", StringComparison.OrdinalIgnoreCase) &&
            !injuryDetail.Contains("No verified", StringComparison.OrdinalIgnoreCase))
        {
            core += $" Current designation: {injuryDetail} is weighing on the edge.";
        }
        else if (context.Intelligence is { HealthScore: >= 65 })
        {
            core += " No verified current injury concern.";
        }
        else if (context.Intelligence is { HealthScore: <= 40 })
        {
            core += " Health risk is tempering conviction.";
        }

        var usage = contributions.FirstOrDefault(c => c.SignalId == "usage-opportunity" && c.Available);
        if (usage is not null && context.Intelligence is { } intel)
        {
            if (intel.UsageScore >= 60)
            {
                core += " Usage/opportunity signals support the lean.";
            }
            else if (intel.UsageScore <= 40)
            {
                core += " Soft usage/opportunity is pulling against oversized overs.";
            }
        }

        var unconfirmed = contributions.FirstOrDefault(c => c.IsUnconfirmed && c.Available);
        if (unconfirmed is not null)
        {
            core += " Unconfirmed injury buzz is noted cautiously and is not treated as fact.";
        }

        if (!contributions.Any(c => c.SignalId == "intelligence-confidence" && c.Available) &&
            line.PlayerId is not null)
        {
            core += " Limited player intelligence reduced confidence.";
        }

        if (line.Freshness == PropLineFreshness.Stale)
        {
            core += " The posted line looks stale.";
        }

        return core;
    }

    private static Guid CreateId(PropLine line)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"playbook:prediction:{line.Id}:{line.Market}:v3"));
        return new Guid(bytes);
    }
}
