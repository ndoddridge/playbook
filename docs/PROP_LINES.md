# Prop Lines Provider

Quick Picks uses structured sportsbook lines, never fantasy league settings.

## Selected provider: The Odds API

**Why:** Legitimate JSON REST API with NFL moneylines, spreads, totals, and player props; free developer tier; clear API-key auth; no HTML scraping.

- Site: https://the-odds-api.com/
- Docs: https://the-odds-api.com/liveapi/guides/v4/
- Sport key: `americanfootball_nfl`

## Configuration

In `appsettings.json` (or environment / user secrets):

```json
"PropLines": {
  "Provider": "Mock",
  "StaleAfterMinutes": 180,
  "OddsApi": {
    "BaseUrl": "https://api.the-odds-api.com/v4/",
    "ApiKey": "",
    "SportKey": "americanfootball_nfl",
    "Regions": "us",
    "GameMarkets": "h2h,spreads,totals",
    "PlayerPropMarkets": "player_pass_yds,player_rush_yds,player_reception_yds,player_receptions,player_anytime_td,player_pass_tds",
    "FetchPlayerProps": true,
    "TimeoutSeconds": 30
  }
}
```

### Supplying a live key

1. Create a free key at https://the-odds-api.com/
2. Set either:
   - `PropLines:OddsApi:ApiKey` in user secrets / local config, or
   - Environment variable `PropLines__OddsApi__ApiKey`
3. Set `"PropLines:Provider": "Live"`

**Never commit a real API key.**

If Live is configured without a key, or the API fails, Playbook falls back to `MockPropLineProvider` and records the error in the Developer Monitor.

## Freshness

| State | Meaning |
|-------|---------|
| Live | Fetched from The Odds API and within `StaleAfterMinutes` |
| Mock | Synthetic development lines |
| Stale | Provider timestamp older than `StaleAfterMinutes` |
| Unavailable | No usable line for that market |

Stale/unavailable lines are never presented as current opportunities in Top Picks.
