namespace Playbook.Application.Predictions;

public enum PropLineProviderKind
{
    /// <summary>Deterministic local lines for development.</summary>
    Mock = 0,

    /// <summary>
    /// Primary: The Odds API. Falls back to Mock when the API key is missing
    /// or the live request fails.
    /// </summary>
    Live = 1
}

public sealed class PropLineOptions
{
    public const string SectionName = "PropLines";

    /// <summary>
    /// Live (primary — The Odds API) or Mock (dev-only).
    /// Live without credentials / on failure automatically falls back to Mock.
    /// </summary>
    public PropLineProviderKind Provider { get; set; } = PropLineProviderKind.Live;

    public OddsApiOptions OddsApi { get; set; } = new();

    /// <summary>Lines older than this are marked Stale (never shown as Live).</summary>
    public int StaleAfterMinutes { get; set; } = 180;

    public int MaxEvents { get; set; } = 8;

    public int MaxPlayerPropsPerEvent { get; set; } = 40;

    /// <summary>
    /// When Live returns zero usable lines (e.g. offseason), fall back to Mock
    /// so the board remains usable for development.
    /// </summary>
    public bool FallbackToMockWhenEmpty { get; set; } = true;
}

public sealed class OddsApiOptions
{
    /// <summary>Base URL for The Odds API v4.</summary>
    public string BaseUrl { get; set; } = "https://api.the-odds-api.com/v4/";

    /// <summary>
    /// API key from https://the-odds-api.com/
    /// Supply via:
    /// - config key <c>PropLines:OddsApi:ApiKey</c>, or
    /// - environment variable <c>PropLines__OddsApi__ApiKey</c>
    /// Never commit a real key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public string SportKey { get; set; } = "americanfootball_nfl";

    public string Regions { get; set; } = "us";

    /// <summary>Featured game markets on the bulk odds endpoint.</summary>
    public string GameMarkets { get; set; } = "h2h,spreads,totals";

    /// <summary>Player prop markets requested per event (tier-dependent).</summary>
    public string PlayerPropMarkets { get; set; } =
        "player_pass_yds,player_rush_yds,player_reception_yds,player_receptions,player_anytime_td,player_pass_tds";

    public int TimeoutSeconds { get; set; } = 30;

    public bool FetchPlayerProps { get; set; } = true;

    /// <summary>Optional preferred bookmaker keys (e.g. draftkings,fanduel). Empty = first available.</summary>
    public string PreferredBookmakers { get; set; } = "draftkings,fanduel,betmgm,williamhill_us";
}
