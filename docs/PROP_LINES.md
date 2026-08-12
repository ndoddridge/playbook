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

## Bookmaker priority (Caesars primary)

`PropLines:OddsApi:PreferredBookmakers` is an **ordered** priority list (position = priority, not
just set membership). Default: `williamhill_us,draftkings,fanduel,betmgm` — `williamhill_us` is
The Odds API's bookmaker key for **Caesars Sportsbook**, the primary source. For each market, the
highest-priority book that has posted a line wins; other listed books only supplement markets
Caesars doesn't have (`LivePropLineProvider.SelectPreferredPerIdentity`). The card face and the
"Why?" panel both show which book a line actually came from and when it was last updated.

## Configuration path (trace)

```
Environment / user-secrets / appsettings
        ↓  (ASP.NET Core configuration)
PropLines:OddsApi:ApiKey  (+ aliases ODDS_API_KEY / THE_ODDS_API_KEY)
        ↓
IOptions<PropLineOptions>  (PostConfigure applies aliases)
        ↓
QuickPicksService.LoadLines()
        ↓  Provider=Live AND key present AND live call succeeds with results?
LivePropLineProvider (TheOddsAPI)  →  PropLine (Freshness=Live)
        ↓  else, only if PropLines:AllowMockFallback=true
MockPropLineProvider               →  PropLine (Freshness=Mock) + Fallback status
        ↓  else (AllowMockFallback=false)
Empty board, ProviderStatus=Error  →  "LIVE UNAVAILABLE" (honest, never mock-as-real)
```

`appsettings.json` sets `"PropLines:Provider": "Live"` by default with an **empty** `ApiKey`.
An empty key is intentional — never commit a real key. `PropLines:AllowMockFallback` (default
`true`, set `false` in `Playbook.Web/appsettings.json`) decides what happens next without a key:
locally/in tests it falls back to Mock so the board stays usable for development; in the deployed
product it does **not** — the board reports "LIVE UNAVAILABLE" instead of quietly showing mock
lines. Explicitly setting `Provider=Mock` is a deliberate dev choice and is unaffected either way.

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

### Production (Fly.io)

Set it as a Fly **secret**, never in `appsettings.json` or fly.toml (secrets are encrypted and
excluded from `flyctl secrets list` output; they never touch the repo):

```bash
flyctl secrets set PropLines__OddsApi__ApiKey="<your-key>" -a playbook-genie
```

Setting a Fly secret automatically restarts the machine with it applied — no separate redeploy
needed. Until this is set, production correctly shows **LIVE UNAVAILABLE** (not Mock — see
`AllowMockFallback` above) rather than presenting fabricated lines as real.

### Cursor Cloud Agent / remote VM

Add a secret named exactly:

```text
PropLines__OddsApi__ApiKey
```

Then **restart** the Playbook process so configuration is reloaded.

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

| Situation | `AllowMockFallback=true` (local/tests) | `AllowMockFallback=false` (production) |
|-----------|------------------------------------------|--------------------------------------------|
| `Provider=Mock` | Uses `MockPropLineProvider` | Uses `MockPropLineProvider` (deliberate choice, not a fallback) |
| `Provider=Live` + missing API key | Falls back to Mock; monitor shows Fallback + Api Key Configured=No | Empty board; ProviderStatus=Error; "LIVE UNAVAILABLE" |
| `Provider=Live` + HTTP/API failure | Falls back to Mock | Empty board; ProviderStatus=Error; "LIVE UNAVAILABLE" |
| `Provider=Live` + empty markets (e.g. offseason) | Falls back to Mock when `FallbackToMockWhenEmpty` is true | Empty board; "LIVE UNAVAILABLE" |

The app does **not** crash when credentials are missing, in either mode.

## Freshness

| State | Meaning |
|-------|---------|
| Live | Fetched from The Odds API and within `StaleAfterMinutes` |
| Mock | Synthetic development lines |
| Stale | Provider timestamp older than `StaleAfterMinutes` |
| Unavailable | No usable line for that market |

Stale and Unavailable lines never produce a Quick Pick at all (`QuickPicksEngine.Evaluate` excludes
them outright, not just from Top Picks) — a pick only ever comes from a Live or Mock line.

## Participation gate

Player-prop picks are also excluded when the app has a real, structured signal that the player
won't play: roster status `Suspended` / `InjuredReserve` / `PracticeSquad`, or a current injury
designation of `Out` / `IR` (`QuickPicksEngine.IsRealisticallyExpectedToParticipate`). This reuses
existing roster/injury data only — Playbook has no depth-chart or snap-share feed, so a healthy
starter simply being rested by a coach (common in the preseason) cannot be detected and is
intentionally not filtered; `Doubtful`/`Questionable` designations remain a soft confidence
derate, not an exclusion, since real uncertainty remains.

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
