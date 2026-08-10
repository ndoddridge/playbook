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
            return prediction.DirectionLabel;
        }

        if (prediction.Line is null)
        {
            return "—";
        }

        return $"{prediction.DirectionLabel} {prediction.Line.Value:0.0}";
    }

    public static string ProjectionText(Prediction prediction) =>
        prediction.PlaybookProjection?.ToString("0.0") ?? "—";

    public static string Timestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("MMM d · h:mm tt");
}
