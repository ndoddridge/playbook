namespace Playbook.Core.Players;

public static class PlayerPresentation
{
    public static string PositionLabel(Position position) => position.ToString();

    public static string StatusLabel(PlayerStatus status) => status switch
    {
        PlayerStatus.Active => "Active",
        PlayerStatus.Questionable => "Questionable",
        PlayerStatus.Doubtful => "Doubtful",
        PlayerStatus.Out => "Out",
        PlayerStatus.InjuredReserve => "IR",
        PlayerStatus.Suspended => "Suspended",
        PlayerStatus.PracticeSquad => "Practice Squad",
        _ => status.ToString()
    };

    public static string TrendLabel(TrendDirection direction) => direction switch
    {
        TrendDirection.Up => "Rising",
        TrendDirection.Flat => "Stable",
        TrendDirection.Down => "Falling",
        _ => direction.ToString()
    };

    public static string Initials(Player player)
    {
        var first = string.IsNullOrWhiteSpace(player.FirstName) ? "?" : player.FirstName[0].ToString().ToUpperInvariant();
        var last = string.IsNullOrWhiteSpace(player.LastName) ? "?" : player.LastName[0].ToString().ToUpperInvariant();
        return first + last;
    }
}
