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

## Configuration path (trace)

```
Environment / user-secrets / appsettings
        ↓  (ASP.NET Core configuration)
PropLines:OddsApi:ApiKey  (+ aliases ODDS_API_KEY / THE_ODDS_API_KEY)
        ↓
IOptions<PropLineOptions>  (PostConfigure applies aliases)
        ↓
QuickPicksService.LoadLines()
        ↓  Provider=Live AND key present?
LivePropLineProvider (TheOddsAPI)  →  PropLine (Freshness=Live)
        ↓  else
MockPropLineProvider               →  PropLine (Freshness=Mock) + Fallback status
```

`appsettings.json` sets `"PropLines:Provider": "Live"` by default with an **empty** `ApiKey`.  
An empty key is intentional — never commit a real key. Without a runtime key, Live **correctly** falls back to Mock (this is why the board may show `MOCK LINE` / `MockBook`).

## Required credentials

| Name | Kind | Required for live |
|------|------|-------------------|
| `PropLines__OddsApi__ApiKey` | Environment variable (preferred) | **Yes** |
| `PropLines:OddsApi:ApiKey` | Config / user secrets | Yes (same value) |
| `ODDS_API_KEY` | Alias env var | Optional alias |
| `THE_ODDS_API_KEY` | Alias env var | Optional alias |
| `PropLines__Provider` | Env override `Live` / `Mock` | No (default Live) |

**Never commit a real API key.**

## How to supply the key

### Local machine (recommended)

```bash
# From src/Playbook.Web
dotnet user-secrets set "PropLines:OddsApi:ApiKey" "<your-key>"
```

Or export before `dotnet run`:

```bash
export PropLines__OddsApi__ApiKey="<your-key>"
# optional aliases also work:
# export ODDS_API_KEY="<your-key>"
dotnet run --project src/Playbook.Web
```

### Cursor Cloud Agent / remote VM

Add a secret named exactly:

```text
PropLines__OddsApi__ApiKey
```

Then **restart** the Playbook process so configuration is reloaded.  
This cloud run currently has **no** Odds API key in the process environment, which is why Quick Picks shows Mock.

### Verify without exposing the key

Open the Dashboard Developer Monitor:

| Field | Expected when live works |
|-------|---------------------------|
| Prop Provider | `TheOddsAPI` |
| Provider Status | `Live` |
| Api Key Configured | `Yes` |
| Last Error | `None` |

If **Api Key Configured = No**, the key never reached the process — fix configuration, don’t change the prediction engine.

## Fallback behavior

| Situation | Result |
|-----------|--------|
| `Provider=Mock` | Uses `MockPropLineProvider` |
| `Provider=Live` + missing API key | Falls back to Mock; monitor shows Fallback + Api Key Configured=No |
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
Live Odds sports:
  americanfootball_nfl_preseason + americanfootball_nfl
        ↓
INflCalendarService
  (cluster kickoffs → NflSlate; preseason ≤ 3 weeks)
        ↓
Select next incomplete slate (or user-selected)
        ↓
Intelligence + Injury + PropStatProjector (phase-aware)
        ↓
QuickPicksEngine v0.3 → board + navigator + filters
```

Sport keys: `PropLines:OddsApi:SportKey` (regular/post) and `PreseasonSportKey` (preseason).
Slates are built only from real events — never invented week numbers.
