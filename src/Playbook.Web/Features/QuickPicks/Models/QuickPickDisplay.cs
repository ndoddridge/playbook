using Playbook.Core.Predictions;

namespace Playbook.Web.Features.QuickPicks.Models;

public static class QuickPickDisplay
{
    public static string FreshnessLabel(PropLineFreshness freshness) => freshness switch
    {
        PropLineFreshness.Live => "Live line",
        PropLineFreshness.Mock => "Mock line",
        PropLineFreshness.Stale => "Stale line",
        PropLineFreshness.Unavailable => "Unavailable",
        _ => freshness.ToString()
    };

    public static string FreshnessCss(PropLineFreshness freshness) => freshness switch
    {
        PropLineFreshness.Live => "qp-freshness qp-freshness--live",
        PropLineFreshness.Mock => "qp-freshness qp-freshness--mock",
        PropLineFreshness.Stale => "qp-freshness qp-freshness--stale",
        _ => "qp-freshness qp-freshness--unavailable"
    };

    public static string LineText(Prediction prediction)
    {
        if (prediction.Market == PredictionMarketType.AnytimeTouchdown)
        {
            return prediction.DirectionLabel.ToUpperInvariant();
        }

        if (prediction.Line is null)
        {
            return prediction.DirectionLabel.ToUpperInvariant();
        }

        return $"{prediction.DirectionLabel.ToUpperInvariant()} {prediction.Line.Value:0.0}";
    }

    public static string ProjectionText(Prediction prediction) =>
        prediction.PlaybookProjection?.ToString("0.0") ?? "—";

    public static string Timestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("MMM d · h:mm tt");

    /// <summary>"via {Bookmaker} · Updated {time}" — shown on the card face, not just in "Why?".</summary>
    public static string SourceLine(Prediction prediction)
    {
        var book = string.IsNullOrWhiteSpace(prediction.Bookmaker) ? prediction.Source : prediction.Bookmaker;
        return prediction.LineUpdatedAt is DateTimeOffset updated
            ? $"via {book} · {Timestamp(updated)}"
            : $"via {book}";
    }

    /// <summary>Matchup · compact slate · date for the card subtitle.</summary>
    public static string CompactContext(Prediction prediction)
    {
        var week = prediction.Event.Phase switch
        {
            NflSeasonPhase.Preseason => $"Preseason W{prediction.Event.Week}",
            NflSeasonPhase.RegularSeason => $"Regular W{prediction.Event.Week}",
            NflSeasonPhase.Postseason => new NflWeekRef(
                prediction.Event.Season,
                prediction.Event.Phase,
                prediction.Event.Week).WeekLabel,
            _ => prediction.Event.WeekLabel
        };
        var date = prediction.Event.CommenceTime.ToLocalTime().ToString("MMM d");
        return $"{prediction.Event.DisplayName} · {week} · {date}";
    }

    public static string EdgeText(Prediction prediction)
    {
        var sign = prediction.Edge >= 0 ? "+" : "";
        return $"{sign}{prediction.Edge:0.0}";
    }

    /// <summary>One-line human summary — not a restatement of every stat.</summary>
    public static string ShortSummary(Prediction prediction)
    {
        if (prediction.Line is decimal line && prediction.PlaybookProjection is decimal projection)
        {
            var diff = Math.Abs(projection - line);
            var unit = UnitLabel(prediction.Market);
            var relation = projection >= line ? "above" : "below";
            if (string.IsNullOrEmpty(unit))
            {
                return $"Projection is {diff:0.0} {relation} the line.";
            }

            return $"Projection is {diff:0.0} {unit} {relation} the line.";
        }

        return $"{prediction.DirectionLabel} lean at {prediction.Confidence}% confidence.";
    }

    public static string UnitLabel(PredictionMarketType market) => market switch
    {
        PredictionMarketType.PassingYards or
            PredictionMarketType.RushingYards or
            PredictionMarketType.ReceivingYards => "yards",
        PredictionMarketType.Receptions => "receptions",
        PredictionMarketType.PassingTouchdowns => "TDs",
        PredictionMarketType.GameTotal or
            PredictionMarketType.TeamTotal or
            PredictionMarketType.Spread => "points",
        _ => ""
    };

    public static IReadOnlyList<DetailSection> BuildDetailSections(Prediction prediction)
    {
        var sections = new List<DetailSection>();

        var why = new List<string>
        {
            $"Projection: {ProjectionText(prediction)}{UnitSuffix(prediction.Market)}",
            $"Market line: {(prediction.Line?.ToString("0.0") ?? "—")}{UnitSuffix(prediction.Market)}",
            $"Edge: {EdgeText(prediction)}{UnitSuffix(prediction.Market)}",
            $"Estimated probability: {prediction.Probability}%"
        };
        sections.Add(new DetailSection("Why this pick", why));

        var context = new List<string>();
        AddContribution(context, prediction, "Season phase", "Preseason context");
        AddContribution(context, prediction, "Usage / opportunity");
        AddContribution(context, prediction, "Current injury");
        AddContribution(context, prediction, "Historical injury");
        foreach (var signal in prediction.SupportingIntelligence)
        {
            if (signal.Contains("Preseason", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("prior", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("usage", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("injury", StringComparison.OrdinalIgnoreCase) ||
                signal.Contains("Health", StringComparison.OrdinalIgnoreCase))
            {
                AddUnique(context, CleanSignal(signal));
            }
        }

        if (context.Count > 0)
        {
            sections.Add(new DetailSection("Context", context.Take(6).ToList()));
        }

        var intel = new List<string>
        {
            $"Confidence: {prediction.Confidence}%"
        };
        AddContribution(intel, prediction, "Intelligence confidence", "Player intelligence");
        AddContribution(intel, prediction, "Health score");
        AddContribution(intel, prediction, "Unconfirmed injury buzz");
        AddContribution(intel, prediction, "Recent intelligence");
        foreach (var signal in prediction.SupportingIntelligence.Where(s =>
                     s.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase)))
        {
            AddUnique(intel, CleanSignal(signal));
        }

        sections.Add(new DetailSection("Intelligence", intel.Take(6).ToList()));

        var sources = new List<string> { prediction.Source };
        if (!string.IsNullOrWhiteSpace(prediction.Bookmaker))
        {
            sources.Add(prediction.Bookmaker!);
        }

        sources.Add(FreshnessLabel(prediction.LineFreshness));
        if (prediction.LineUpdatedAt is DateTimeOffset updated)
        {
            sources.Add($"Line updated {Timestamp(updated)}");
        }

        sections.Add(new DetailSection("Data sources", sources));
        return sections;
    }

    private static string UnitSuffix(PredictionMarketType market)
    {
        var unit = UnitLabel(market);
        return string.IsNullOrEmpty(unit) ? "" : $" {unit}";
    }

    private static void AddContribution(
        List<string> target,
        Prediction prediction,
        params string[] labels)
    {
        foreach (var label in labels)
        {
            var hit = prediction.SignalContributions.FirstOrDefault(c =>
                c.Available &&
                string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(c.Detail));
            if (hit is null)
            {
                continue;
            }

            var text = hit.IsUnconfirmed
                ? $"{hit.Label}: {hit.Detail} (unconfirmed)"
                : $"{hit.Label}: {hit.Detail}";
            AddUnique(target, text);
        }
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (target.Any(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        target.Add(value);
    }

    private static string CleanSignal(string signal) =>
        signal.Trim().TrimEnd('.');

    public sealed record DetailSection(string Title, IReadOnlyList<string> Items);
}
