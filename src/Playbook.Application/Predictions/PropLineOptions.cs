namespace Playbook.Application.Predictions;

public enum PropLineProviderKind
{
    Mock = 0,
    Live = 1
}

public sealed class PropLineOptions
{
    public const string SectionName = "PropLines";

    /// <summary>Mock (default for local/dev) or Live (The Odds API).</summary>
    public PropLineProviderKind Provider { get; set; } = PropLineProviderKind.Mock;

    public OddsApiOptions OddsApi { get; set; } = new();

    /// <summary>Lines older than this are marked Stale.</summary>
    public int StaleAfterMinutes { get; set; } = 180;

    public int MaxEvents { get; set; } = 8;

    public int MaxPlayerPropsPerEvent { get; set; } = 40;
}

public sealed class OddsApiOptions
{
    /// <summary>Base URL for The Odds API v4.</summary>
    public string BaseUrl { get; set; } = "https://api.the-odds-api.com/v4/";

    /// <summary>
    /// API key from https://the-odds-api.com/ — supply via PropLines:OddsApi:ApiKey
    /// or environment variable PropLines__OddsApi__ApiKey. Never commit a real key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string SportKey { get; set; } = "americanfootball_nfl";

    public string Regions { get; set; } = "us";

    /// <summary>Featured game markets on the bulk odds endpoint.</summary>
    public string GameMarkets { get; set; } = "h2h,spreads,totals";

    /// <summary>Player prop markets requested per event (paid/free-tier dependent).</summary>
    public string PlayerPropMarkets { get; set; } =
        "player_pass_yds,player_rush_yds,player_reception_yds,player_receptions,player_anytime_td,player_pass_tds";

    public int TimeoutSeconds { get; set; } = 30;

    public bool FetchPlayerProps { get; set; } = true;
}
