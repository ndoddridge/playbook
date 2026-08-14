namespace Playbook.Core.Predictions;

/// <summary>
/// Turns a market/direction/line into an explicit, actionable bet instruction. "Cover" / "Not
/// Cover" and bare Over/Under on a spread or total force the reader to work out which team and
/// which sign — this always names the team (or OVER/UNDER + line) directly instead. Player-prop
/// markets (yards/receptions/anytime TD) are already unambiguous (a single numeric stat line) and
/// pass through unchanged.
/// </summary>
public static class BetLabelFormatter
{
    /// <summary>Card-facing label, e.g. "BET: Chiefs -3.5" / "BET OVER 36.5".</summary>
    public static string FormatBadge(
        PredictionMarketType market,
        PredictionDirection direction,
        decimal? line,
        string? subjectTeamName,
        string homeTeam,
        string awayTeam)
    {
        var side = FormatSide(market, direction, line, subjectTeamName, homeTeam, awayTeam);
        return market switch
        {
            PredictionMarketType.Spread => $"BET: {side}",
            PredictionMarketType.GameTotal or PredictionMarketType.TeamTotal => $"BET {side}",
            _ => side
        };
    }

    /// <summary>Sentence-facing phrase (no "BET" prefix), e.g. "Chiefs -3.5" / "OVER 36.5".</summary>
    public static string FormatSide(
        PredictionMarketType market,
        PredictionDirection direction,
        decimal? line,
        string? subjectTeamName,
        string homeTeam,
        string awayTeam)
    {
        if (market == PredictionMarketType.Spread && line is decimal spreadLine)
        {
            var favoredTeam = string.IsNullOrWhiteSpace(subjectTeamName) ? homeTeam : subjectTeamName;
            return direction == PredictionDirection.Cover
                ? $"{favoredTeam} {spreadLine:+0.0;-0.0;0.0}"
                : $"{OtherTeam(favoredTeam, homeTeam, awayTeam)} {-spreadLine:+0.0;-0.0;0.0}";
        }

        if (market is PredictionMarketType.GameTotal or PredictionMarketType.TeamTotal && line is decimal totalLine)
        {
            var prefix = market == PredictionMarketType.TeamTotal && !string.IsNullOrWhiteSpace(subjectTeamName)
                ? $"{subjectTeamName} "
                : "";
            return direction == PredictionDirection.Over
                ? $"{prefix}OVER {totalLine:0.0}"
                : $"{prefix}UNDER {totalLine:0.0}";
        }

        var word = DirectionWord(direction);
        return line is decimal l ? $"{word} {l:0.0}" : word;
    }

    private static string OtherTeam(string subject, string home, string away) =>
        string.Equals(subject, home, StringComparison.OrdinalIgnoreCase) ? away : home;

    private static string DirectionWord(PredictionDirection direction) => direction switch
    {
        PredictionDirection.Over => "OVER",
        PredictionDirection.Under => "UNDER",
        PredictionDirection.Yes => "YES",
        PredictionDirection.No => "NO",
        PredictionDirection.Home => "HOME",
        PredictionDirection.Away => "AWAY",
        PredictionDirection.Cover => "COVER",
        PredictionDirection.NotCover => "NOT COVER",
        _ => direction.ToString().ToUpperInvariant()
    };
}
