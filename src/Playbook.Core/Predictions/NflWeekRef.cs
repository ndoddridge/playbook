namespace Playbook.Core.Predictions;

/// <summary>
/// Stable NFL week identity used for slate selection and filtering.
/// </summary>
public sealed record NflWeekRef(int Season, NflSeasonPhase Phase, int Week)
{
    public string PhaseLabel => Phase switch
    {
        NflSeasonPhase.Preseason => "Preseason",
        NflSeasonPhase.RegularSeason => "Regular Season",
        NflSeasonPhase.Postseason => "Postseason",
        _ => Phase.ToString()
    };

    public string DisplayLabel => $"{PhaseLabel} · Week {Week}";

    public bool Matches(FootballEvent ev) =>
        ev.Season == Season && ev.Phase == Phase && ev.Week == Week;
}
