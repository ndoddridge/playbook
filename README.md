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
│   ├── Playbook.Web            # Blazor Server UI
│   ├── Playbook.Core           # Domain models & abstractions
│   ├── Playbook.Application    # Use cases & application services
│   └── Playbook.Infrastructure # Persistence & external adapters
├── tests
│   └── Playbook.Tests          # xUnit tests
└── docs
    ├── DESIGN.md
    ├── ARCHITECTURE.md
    └── DEVELOPMENT_RULES.md
```

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
