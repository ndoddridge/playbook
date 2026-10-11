# Playbook

A professional football intelligence platform focused on maximizing fantasy football decision making through explainable recommendations.

This is **not** a fantasy football website. It is a personal football intelligence platform that prioritizes concise, actionable recommendations over raw data — while remaining fully explainable.

## Vision

When you open Playbook, you should immediately understand the highest-impact decisions you can make. The system will continuously ingest football information, convert it into recommendations, explain every recommendation clearly, and adapt as news breaks or rosters change.

See [docs/DESIGN.md](docs/DESIGN.md) for the full product design.

## Solution Structure

```
playbook.sln
├── src
│   ├── Playbook.UI             # Razor components, pages and styles (shared by both hosts)
│   ├── Playbook.Web            # Blazor Server host (Fly.io)
│   ├── Playbook.Client         # Blazor WebAssembly host (noahdbuilds.com/playbook)
│   ├── Playbook.Core           # Domain models & abstractions
│   ├── Playbook.Application    # Use cases & application services
│   ├── Playbook.Infrastructure # Persistence & external adapters
│   └── Playbook.Research       # Offline research CLI workbench
├── tests
│   └── Playbook.Tests          # xUnit tests
├── research-runs/              # Local experiment/simulation artifacts (gitignored)
├── scripts/research            # Convenience wrapper for the research CLI
└── docs
    ├── DESIGN.md
    ├── ARCHITECTURE.md
    ├── RESEARCH_WORKBENCH.md
    └── DEVELOPMENT_RULES.md
```

## Research workbench (manual experiments)

For offline historical evaluation / experiments / future multi-season simulation:

```bash
dotnet run --project src/Playbook.Research -- --help
# or
./scripts/research --help
```

See [docs/RESEARCH_WORKBENCH.md](docs/RESEARCH_WORKBENCH.md). Production remains `KnowledgeMode.Passthrough`.

## Tech Stack

| Area | Choice |
| --- | --- |
| Backend | ASP.NET Core (.NET 9) |
| UI | Blazor Server (Interactive Server render mode) |
| Player data | Mock catalog **or** live Sleeper NFL API (config switch) |
| News | Mock wire **or** live ESPN NFL news (config switch) |
| Data access | Entity Framework Core + PostgreSQL *(planned; not wired yet)* |
| Testing | xUnit |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Setup

```bash
git clone <repository-url>
cd playbook
dotnet restore
dotnet build
```

## Player data source (Mock vs Live)

Playbook can load players from mock data or the live Sleeper API. Switch with configuration only — no code or UI changes.

In `src/Playbook.Web/appsettings.json`:

```json
"PlayerData": {
  "Provider": "Live",
  "Sleeper": {
    "BaseUrl": "https://api.sleeper.app/v1/",
    "ApiKey": "",
    "TimeoutSeconds": 30
  }
}
```

| `Provider` | Behavior |
| --- | --- |
| `Mock` | In-memory catalog (~20 players) |
| `Live` | Sleeper NFL players (teams, positions, status). On failure, **automatically falls back to Mock** |

`ApiKey` is reserved for future authenticated providers; public Sleeper reads do not require it.

## News source (Mock vs Live)

Football news is normalized into `NewsArticle` objects behind `INewsProvider`.

```json
"News": {
  "Provider": "Live",
  "Espn": {
    "BaseUrl": "https://site.api.espn.com/apis/site/v2/",
    "ApiKey": "",
    "Limit": 40,
    "TimeoutSeconds": 30
  }
}
```

| `Provider` | Behavior |
| --- | --- |
| `Mock` | In-memory headlines with related player names |
| `Live` | ESPN NFL news. On failure, **automatically falls back to Mock** |

Background refresh (`BackgroundRefresh`) periodically reloads players, news, intelligence analysis, and projections (each step logged separately).

## Player injuries

Injury designations are loaded behind `IPlayerInjuryService` (Mock or Live), exposed as `PlayerInjuryProfile`.

```json
"Injuries": {
  "Provider": "Live",
  "CacheFileName": "player-injuries-cache.json",
  "CacheTtlMinutes": 180
}
```

- **Current:** ESPN NFL injuries feed + Sleeper enrichment (`Injuries:Provider=Live`)
- **Historical NFL:** nflverse official injury-report CSVs via `HistoricalProvider=Nflverse` (GSIS join; no API key)
- **College injuries:** not available from configured free sources (`ICollegeInjuryProvider` extension point)
- Player identity crosswalk: PlaybookId ↔ Sleeper / ESPN / GSIS (`IPlayerIdentityDirectory`)
- Source confidence: Verified / Reported / Unconfirmed / Unknown (never silently merged)
- See `docs/INJURY_DATA_SOURCES.md` for the full audit

## Player statistics

Normalized football statistics power Career, Projection, and the Intelligence statistical interface.

```json
"PlayerStats": {
  "Provider": "Live",
  "HistoricalProvider": "Nflverse",
  "HistoricalSeasonCount": 5,
  "GameLogSeasonCount": 3,
  "CacheFileName": "player-stats-cache.json",
  "GameLogCacheFileName": "player-game-logs-cache.json",
  "CacheTtlMinutes": 360
}
```

- Historical NFL: nflverse weekly `player_stats_{season}.csv.gz` (per-season disk cache, GSIS + name/team identity)
- Current / gap-fill NFL: Sleeper bulk season stats (`/stats/nfl/regular/{season}`)
- College: `ICollegeStatsProvider` via ESPN CFB athlete stats (kept separate from NFL samples)
- Fantasy points calculated from canonical counting stats + league scoring (`LeagueFantasyScoring` — PPR / Half / Standard)
- Game logs retained for trends; null ≠ zero
- Developer Monitor: providers, NFL players/seasons, game logs, identity matches, sync errors

## Projection Engine

Projection Engine V1 produces numerical expected outcomes (`PlayerProjection`). It does **not** make start/sit, waiver, draft, or trade decisions.

**Inputs**

1. Player-specific production (`IPlayerProductionProvider`) — prefers `IPlayerStatsService` recent/multi-season NFL stats, then curated catalog, then attribute fallback
2. `PlayerIntelligenceProfile` — opportunity / usage / health / risk / trend adjustments
3. League scoring (Standard / Half-PPR / PPR) — receptions change fantasy math

Baselines are computed from passing/rushing/receiving components (position-specific), not a flat position constant. Intelligence then scales volume and downside. Rules live in `Projection:Rules`.

UI surfaces:

- Player Overlay **Projection** tab — points, floor/median/ceiling, confidence, volatility, player-specific reasoning
- Player Explorer — sortable **Projected Points** column
- Developer Monitor — Players Projected, Unique Projection Values, Average Projection, Average Confidence, Projection Runtime

## Run the web app

```bash
dotnet run --project src/Playbook.Web
```

Then open the URL shown in the console (typically `https://localhost:7xxx`). Use the Dashboard **Developer Monitor** to confirm provider, sync time, player count, and any fallback errors.

## Production deployment

### In the browser, at noahdbuilds.com/playbook

`Playbook.Client` runs the same UI and engines in the browser as WebAssembly, with no server. noahdbuilds.com serves the published files at `/playbook/` and provides a small relay Function that forwards the app's GET requests to Sleeper, ESPN and nflverse, which do not all accept calls from a browser page on another site (`RelayHandler` decides which requests go through it). The data directory the stores and caches write to lives in memory and is saved to IndexedDB (`BrowserDataStore`), so connected leagues and caches survive a reload.

A browser cannot block on a network call, so the data services load asynchronously when running there (`OperatingSystem.IsBrowser()` in their `EnsureLoaded` paths), and `Boot` loads players, stats, injuries, news and the NFL calendar before the first render. MD5 ids use the managed `Playbook.Core.Hashing.Md5`, since the browser runtime has no MD5. Settings are the Web project's `appsettings.json` with `src/Playbook.Client/browser-settings.json` on top.

The browser build is compiled ahead of time to WebAssembly, which needs the .NET WebAssembly build tools once: `dotnet workload install wasm-tools` (on an SDK newer than 9, `wasm-tools-net9`).

To deploy a new version, build it into the site from a clean checkout of this repository:

```bash
cd ../noahdbuilds
PLAYBOOK_LOCAL_PATH=../playbook npm run build:playbook
```

then commit and merge there (see that repository's README). To try it locally first, run `npx wrangler pages dev public` in the noahdbuilds checkout after building: that serves the app and its relay at http://localhost:8788/playbook/.

### Blazor Server, on Fly.io

The Blazor Server app runs on Fly.io (persistent process + WebSocket support, required for Blazor Server's SignalR circuit) at **https://playbook-genie.fly.dev**. `main` is the production branch — every push triggers `.github/workflows/fly-deploy.yml`, which builds the `Dockerfile` (Core/Application/Infrastructure/Web only — the Research CLI is excluded from the deployed image) and deploys via `flyctl deploy --remote-only`.

## Run tests

```bash
dotnet test
```

## Current Status

**Developer monitoring**, **live players/news**, **Intelligence Analyzer V1**, **Player Intelligence Profiles**, and **Projection Engine V1** (numerical expected outcomes for downstream Decision / Quick Picks / Draft / Waiver / Trade engines).

## Documentation

- [Design](docs/DESIGN.md) — product vision, pages, and engines
- [Architecture](docs/ARCHITECTURE.md) — layered structure, providers, and future engine pipeline
- [Development Rules](docs/DEVELOPMENT_RULES.md) — engineering standards for this repo
- [Changelog](docs/CHANGELOG.md) — notable project changes
