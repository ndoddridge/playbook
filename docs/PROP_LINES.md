# Prop Lines Provider

Quick Picks uses structured sportsbook lines via `IPropLineProvider`. It never reads fantasy league settings, owned teams, or scoring.

## Selected provider: The Odds API

**Why:** Legitimate JSON REST API with NFL moneylines, spreads, totals, and player props; free developer tier; clear API-key auth; no HTML scraping.

- Site: https://the-odds-api.com/
- Docs: https://the-odds-api.com/liveapi/guides/v4/
- Sport key: `americanfootball_nfl`

### Markets Playbook consumes

| Market | Odds API key(s) | Playbook type |
|--------|-----------------|---------------|
| Moneyline | `h2h` | Winner |
| Spread | `spreads` | Spread |
| Game total | `totals` | GameTotal |
| Team total | `team_totals` (when present) | TeamTotal |
| Passing yards | `player_pass_yds` | PassingYards |
| Rushing yards | `player_rush_yds` | RushingYards |
| Receiving yards | `player_reception_yds` | ReceivingYards |
| Receptions | `player_receptions` | Receptions |
| Anytime TD | `player_anytime_td` | AnytimeTouchdown |
| Passing TDs | `player_pass_tds` | PassingTouchdowns |

Each normalized `PropLine` includes book/source, line, American odds when available, and `UpdatedAt` for freshness.

## Configuration

In `appsettings.json` (or environment / user secrets):

```json
"PropLines": {
  "Provider": "Live",
  "StaleAfterMinutes": 180,
  "FallbackToMockWhenEmpty": true,
  "OddsApi": {
    "BaseUrl": "https://api.the-odds-api.com/v4/",
    "ApiKey": "",
    "SportKey": "americanfootball_nfl",
    "Regions": "us",
    "GameMarkets": "h2h,spreads,totals",
    "PlayerPropMarkets": "player_pass_yds,player_rush_yds,player_reception_yds,player_receptions,player_anytime_td,player_pass_tds",
    "PreferredBookmakers": "draftkings,fanduel,betmgm,williamhill_us",
    "FetchPlayerProps": true,
    "TimeoutSeconds": 30
  }
}
```

### Required environment variables (live)

| Variable | Purpose |
|----------|---------|
| `PropLines__OddsApi__ApiKey` | The Odds API key (required for live lines) |
| `PropLines__Provider` | Optional override: `Live` (default) or `Mock` |

Config-key equivalents:

- `PropLines:OddsApi:ApiKey`
- `PropLines:Provider`

### Supplying a live key

1. Create a free key at https://the-odds-api.com/
2. Set `PropLines__OddsApi__ApiKey` (or user secrets / local config)
3. Keep `"PropLines:Provider": "Live"` (default)

**Never commit a real API key.**

### Fallback behavior

| Situation | Result |
|-----------|--------|
| `Provider=Mock` | Uses `MockPropLineProvider` |
| `Provider=Live` + missing API key | Falls back to Mock; monitor shows Fallback + Last Error |
| `Provider=Live` + HTTP/API failure | Falls back to Mock |
| `Provider=Live` + empty markets (e.g. offseason) | Falls back to Mock when `FallbackToMockWhenEmpty` is true |

The app does **not** crash when credentials are missing.

## Freshness

| State | Meaning |
|-------|---------|
| Live | Fetched from The Odds API and within `StaleAfterMinutes` |
| Mock | Synthetic development lines |
| Stale | Provider timestamp older than `StaleAfterMinutes` |
| Unavailable | No usable line for that market |

Stale/unavailable lines are never presented as Live. Top Picks only includes Live or Mock freshness.

## Pipeline

```
Live/Mock prop lines (IPropLineProvider)
        ↓
Intelligence + PropStatProjector
        ↓
QuickPicksEngine
        ↓
Edge / probability / confidence → Quick Picks UI
```
