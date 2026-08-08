# Architecture

## Overview

Playbook uses a clean, layered architecture so domain logic, application workflows, infrastructure adapters, and the UI stay independently testable and easy to evolve.

```
Playbook.Web
        │
        ├──► Playbook.Application
        │            │
        │            └──► Playbook.Core
        │
        └──► Playbook.Infrastructure
                     │
                     ├──► Playbook.Application
                     └──► Playbook.Core
```

## Layers

### Playbook.Core

The innermost layer. Contains domain models, value objects, enums, and abstractions that define what the system *is*.

- No dependencies on Application, Infrastructure, or Web
- No framework concerns
- Engine contracts and shared domain types belong here

### Playbook.Application

Orchestrates use cases. Coordinates domain behavior without knowing how data is stored or how the UI renders.

- Depends only on Core
- Registers application services through dependency injection
- Houses application ports (interfaces) that Infrastructure implements

### Playbook.Infrastructure

Implements technical details: persistence, external APIs, file systems, and other adapters.

- Depends on Application and Core
- Entity Framework Core and PostgreSQL will live here when introduced
- Database wiring is intentionally stubbed in the foundation

### Playbook.Web

Blazor Server presentation layer.

- Depends on Application and Infrastructure
- Composes the DI container at startup
- Remains thin: UI concerns only; no business logic

### Playbook.Tests

xUnit test project referencing Core, Application, and Infrastructure so each layer can be verified in isolation or together.

## Dependency Rules

1. Dependencies point inward toward Core.
2. Web may compose Infrastructure, but Core and Application never reference Web.
3. Prefer interfaces defined in Core/Application; implement them in Infrastructure.
4. Register all services through dependency injection — avoid static state and service locators.

## Future Engine Structure

The product vision is a pipeline of single-responsibility engines. Each engine should be independently testable and produce explainable outputs.

```
Data Engine
    ↓
Intelligence Engine
    ↓
Projection Engine
    ↓
Prediction Engine
    ↓
Decision Engine
    ↓
Recommendation Service
    ↓
UI (Blazor)
```

| Engine | Responsibility |
| --- | --- |
| **Data Engine** | Ingest and normalize NFL, injury, weather, tracking, and market inputs |
| **Intelligence Engine** | Convert football information into structured `IntelligenceFact` / `PlayerIntelligence` — no fantasy scoring |
| **Projection Engine** | Produce expected points, floor, median, ceiling, confidence, and volatility from intelligence + league context (no fantasy decisions) |
| **Prediction Engine** | Estimate outcomes (win probability, game script, volume distributions) |
| **Decision Engine** | Turn projections/predictions into actionable choices (start/sit, waiver, trade, draft) |
| **Recommendation Service** | Aggregate, rank, and expose `Recommendation` objects to the UI |

Engines communicate through clear contracts. Recommendations always carry action, confidence, impact, and reasoning so every suggestion remains explainable.

### Intelligence Engine

`Playbook.Core.Intelligence.Models` holds football-only insight models:

- `IntelligenceFact` — one inferred insight (usage, matchup, weather, coaching, etc.) with confidence, importance, source, evidence, and optional player/team/game links
- `PlayerIntelligence` — aggregated profile for one player (facts + trend/risk/opportunity summaries)

Contracts:

- `IIntelligenceService` (`Playbook.Application.Intelligence.Interfaces`)
- `MockIntelligenceService` (`Playbook.Infrastructure.Intelligence.Services`)

The Intelligence Engine knows nothing about fantasy points, rankings, recommendations, or league settings. Its job ends at structured football intelligence. Projection and Decision engines consume it later.

UI surfaces today:

- Dashboard **Football Intelligence** — `GetTopFacts`
- Player Overlay **Intelligence** tab — `GetPlayerIntelligence`

Swap `MockIntelligenceService` for a Data-Engine-backed implementation without changing consumers.

## Design Goals

- Extremely clean architecture
- Modular, maintainable design
- Fast iteration with a never-break-the-build discipline
- Every feature testable from day one
- Every recommendation eventually explainable

## League State

The selected fantasy league is global application context. Every future recommendation engine should read from this state so outputs stay league-aware (scoring, roster rules, matchups).

### Contracts

- `Playbook.Core.Leagues.League` — domain model with platform, type, scoring, week, season, and activity flags
- `ILeagueService` — catalog + selection API (`GetAllLeagues`, `GetCurrentLeague`, `SelectLeague`)
- `ILeagueState` — process-lifetime selected league plus a `Changed` notification for UI (and later engines)

### Mock Service

`MockLeagueService` (Infrastructure) seeds three in-memory leagues: Friends League, Dynasty League, and Work League. No database or external APIs are involved.

### Dependency Injection

Registered as singletons so selection survives navigation for the lifetime of the running app:

- `ILeagueService` → `MockLeagueService`
- `ILeagueState` → `LeagueStateService`

UI components (`LeagueSwitcher`, Dashboard) inject `ILeagueState` and subscribe to `Changed`. Avoid static state.

### Future replacement with real APIs

Swap `MockLeagueService` for an API/EF-backed implementation of `ILeagueService` without changing UI or engine consumers. Persist the last-selected league id (cookie/local storage/user profile) when accounts exist.

## Recommendation Model

`Playbook.Core.Recommendations.Recommendation` is the central object every engine produces and the UI consumes.

Core fields: Id, Title, Summary, ActionType, Priority, Confidence, Impact, Category, Status, Reasoning, SupportingSignals, Evidence, FutureNotes, LastUpdated, SourceEngine, IsExpanded, and optional Metadata.

Enums: `RecommendationType`, `RecommendationPriority`, `RecommendationStatus`, `RecommendationCategory`, `EngineType`.

### Recommendation Pipeline

```
Engines (Projection / Draft / Waiver / Trade / Knowledge / Quick Picks / Decision)
        ↓  emit Recommendation objects
IRecommendationService  (aggregation / ranking)
        ↓
UI (DecisionCard)  — display only, never invents recommendations
```

Today, `MockRecommendationService` is the single source of recommendations. The Dashboard calls `GetTopRecommendations()` and passes each item to `DecisionCard`.

### Future Engine Flow

Each engine returns `Recommendation` instances tagged with `SourceEngine`. A future aggregator implements `IRecommendationService`, merges engine output, ranks by priority/confidence/league context, and feeds the same Decision Card UI without visual rewrites.

## Player Engine

The Player Engine is the football domain model — not an ingestion pipeline. Everything in Playbook eventually revolves around `Player` and `PlayerProfile`.

### Contracts

- `Playbook.Core.Players.Player` — identity (name, position, team, status, physicals, bye)
- Supplemental structures: `SeasonStats`, `CareerStats`, `CollegeStats`, `InjuryRecord`, `PlayerTrend`
- `PlayerProfile` — aggregated view engines should request instead of assembling pieces
- `IPlayerService` — `GetAllPlayers`, `GetPlayer`, `GetPlayerProfile`, `SearchPlayers`
- `IPlayerDataProvider` — raw catalog source (`Mock` or `Live`); UI never calls this directly
- `IPlayerDataSyncStatus` — developer telemetry for the active provider

### Provider architecture (first live integration)

```
appsettings PlayerData:Provider = Mock | Live
        │
        ▼
   PlayerService  ──uses──►  IPlayerDataProvider (primary)
        │                         │
        │                         ├── MockPlayerDataProvider
        │                         └── LivePlayerDataProvider (Sleeper)
        │
        └── on live failure ──► MockPlayerDataProvider (automatic fallback)
```

- **Selected live API:** [Sleeper](https://docs.sleeper.com/) `GET /players/nfl` (filtered by fantasy position). Free public reads; `PlayerData:Sleeper:ApiKey` is reserved for future auth.
- Flip sources with configuration only — no UI or `IPlayerService` consumer changes.
- `PlayerService` records configured vs active provider, last sync, player count, response time, and last error for the Developer Monitor.

### Mock enrichment

When the active catalog is mock (configured or fallback), `PlayerService` still attaches rich mock profiles. Live catalogs map identity/status fields only until projection/stats providers exist.

### Future Data Engine / provider additions

Add new providers the same way: define an application interface (e.g. `INewsDataProvider`), implement Mock + Live in Infrastructure, bind a config section, register both, and have the consuming service fall back to mock on failure. Candidates:

| Provider | Purpose |
| --- | --- |
| **News** | Headlines and injury blurbs feeding Intelligence |
| **Odds** | Market lines for projection/decision confidence |
| **Weather** | Game-environment signals |
| **Schedules** | Matchups and bye weeks |
| **Injuries** | Structured injury status beyond player roster flags |

The Data Engine will eventually orchestrate these providers and refresh `Player` / `PlayerProfile` without changing Player Explorer or overlay UI.

## News Provider

The News Provider retrieves and normalizes football news. It is **not** the Intelligence Engine — it only supplies structured `NewsArticle` objects.

### Contracts

- `NewsArticle` — Id, Title, Summary, Published, Source, Url, RelatedPlayerIds, RelatedTeamIds, Category, Priority
- `INewsProvider` — UI-facing API (`GetLatest`, `GetForPlayer`, `RefreshAsync`)
- `INewsSource` — Mock/Live adapters
- `INewsSyncStatus` — developer telemetry

### Provider flow

```
appsettings News:Provider = Mock | Live
        │
        ▼
   INewsProvider (NewsProvider facade)
        │
        ├── MockNewsProvider
        └── LiveNewsProvider (ESPN)
        │
        └── on live failure ──► MockNewsProvider
```

### Normalization

Live ESPN articles are mapped into `NewsArticle`. Athlete names from ESPN categories are resolved to Playbook player Guids by matching against the loaded player catalog when the API does not provide Playbook ids.

### Future Intelligence integration

The Intelligence Engine consumes `INewsProvider` (normalized `NewsArticle` values) plus the player catalog. Do not parse ESPN (or any wire format) inside Intelligence.

## Intelligence Engine (V1)

Playbook's first reasoning layer. It transforms raw news into actionable **football** intelligence — not fantasy recommendations.

### Pipeline

```
NewsArticle (INewsProvider) + Player catalog
        │
        ▼
 IntelligenceAnalyzer (deterministic rules)
        │
        ▼
 IntelligenceFact (+ RelatedNewsArticleIds)
        │
        ▼
 IIntelligenceService → Dashboard / Player Overlay
```

### Analyzer / rule engine

`IntelligenceAnalyzer` applies ordered, explainable keyword heuristics (injury, usage, transactions, suspensions, practice, coaching, weather, etc.). Each match emits an `IntelligenceFact` with:

- Category, importance, confidence
- Supporting evidence: rule id, matched phrase, reason
- Related news article id(s) and optional player/team links

Given the same articles and players, outputs are identical (deterministic Guids).

### Explainability

UI surfaces reasons and source article links. Downstream engines should treat facts as evidence packages, not opaque scores.

### Future ML integration

Replace or augment `IIntelligenceAnalyzer` with an ML model that still emits `IntelligenceFact` with article references and human-readable evidence. Keep `IIntelligenceService` and UI unchanged.

### Aggregation pipeline

```
IntelligenceFact[]
        │
        ▼
 IntelligenceAggregator
   (group by player → dedupe → weighted scores)
        │
        ▼
 PlayerIntelligenceProfile  ← canonical engine input
```

Weighted scoring starts at a configurable baseline (default 50) and applies rule deltas from `Intelligence:Scoring` (e.g. limited practice −25 health, full practice +15 health, starter language +20 opportunity). Importance and confidence scale each delta.

### Future engine inputs

Projection, Prediction, and Decision engines should take `PlayerIntelligenceProfile` as their football-intelligence input. Raw facts remain available for explainability UI only.

### Background refresh

`DataRefreshBackgroundService` refreshes players, then news, then intelligence, then projections — each step logged separately.

## Player Statistics Layer

Normalized historical + current-season statistics feed Projection and the Career/Stats overlay.

### Provider pattern

Mirrors players/news:

- `IPlayerStatsProvider` — `MockPlayerStatsProvider` | `LivePlayerStatsProvider` (Sleeper)
- `IPlayerStatsService` — facade with config switch, mock fallback, telemetry
- `PlayerStatsCacheStore` — JSON file cache (`data/player-stats-cache.json`) for initial sync / reuse / refresh

Live endpoint: `GET /stats/nfl/regular/{season}` on `api.sleeper.app/v1`, joined to Playbook player ids via the same deterministic Sleeper id hash used by the player catalog. NFL state (`/state/nfl`) selects current vs previous seasons.

### Data model

`PlayerSeasonStats` includes PlayerId, Season, SeasonType, Period (`CompletedSeason` | `CurrentSeason` | `College`), games/starts, passing/rushing/receiving counting stats, and fantasy points (Standard / Half-PPR / PPR). Missing values stay null — never fabricated.

College statistics are first-class for players with fewer than 3 NFL seasons. Sleeper does not provide college box scores. A dedicated `ICollegeStatsProvider` supplies college rows (Mock seeds or Live ESPN college-football athlete stats), merged by `PlayerStatsService`. Games/targets may be null when the source omits them — never fabricated.

### Projection Engine (V1)

Estimates **numerical expected outcomes** only. It must not encode start/sit, waiver, draft, or trade decisions. Downstream engines consume `PlayerProjection`.

### Projection inputs

| Input | Role |
| --- | --- |
| `IPlayerStatsService` → `PlayerProductionSnapshot` | Preferred recent / multi-season NFL production |
| Curated / attribute fallbacks | Used only when stats service has no record |
| `PlayerIntelligenceProfile` | Opportunity / usage / health / risk / trend modifiers |
| `Player` + position | Routes which production components matter |
| League scoring | Standard / Half-PPR / PPR fantasy math from components |

### Projection pipeline

```
Player + IPlayerProductionProvider
        │
        ▼
 PlayerProductionSnapshot  (curated → profile → attribute fallback)
        │
        +── PlayerIntelligenceProfile
        +── League scoring context
        ▼
 ProjectionEngine (production baseline + intelligence volume/downside)
        │
        ▼
 PlayerProjection
   (points / floor / median / ceiling / confidence / volatility)
        │
        ▼
 IProjectionService → Overlay / Explorer / Monitor
        │
        ▼
 Future: Decision · Quick Picks · Draft · Waiver · Trade
```

### Position-specific baselines

Weekly fantasy points are computed from season production ÷ games:

| Position | Components |
| --- | --- |
| QB | Passing volume/efficiency (yds, TD, INT) + rushing contribution |
| RB | Carries / rush yds / rush TD + targets / receptions / receiving yds / TD |
| WR | Targets / receptions / receiving yds / TD (+ rare rush) |
| TE | Targets / receptions / receiving yds / TD |
| K / DST | Specialist weekly prior |

Fantasy math (`FantasyScoring`): pass yds/25, pass TD×4, INT×−2, rush/rec yds/10, rush/rec TD×6, receptions × 0 / 0.5 / 1.0 by scoring type.

### Intelligence adjustments

Centralized in `Projection:Rules`:

| Signal | Effect |
| --- | --- |
| High Opportunity | Increases expected volume (× factor) |
| Low Opportunity | Decreases expected volume |
| Health concern | Reduces projection; widens downside (floor) |
| Strong usage / trend up | Raises median and ceiling |
| Negative usage / trend down | Decreases projection |
| Elevated Risk | Trims projection |
| High intel confidence | Reduces volatility |
| Low intel confidence | Increases volatility |

### Fallback behavior

`IPlayerProductionProvider` resolution order:

1. **CuratedSeason** — player-specific catalog keyed by normalized name (works for mock + live ids)
2. **ProfileSeason** — `SeasonStats` from `PlayerProfile` when populated by a future live stats path
3. **AttributeFallback** — position shell scaled by YearsPro, Age, and Status (explicitly labeled in reasoning)

Live Sleeper currently supplies identity only (no box scores). Unknown live players therefore use attribute fallback until a stats provider implements `IPlayerProductionProvider`.

### Validation tests

`ProjectionEngineTests` covers: same-position differentiation, opportunity↑ ⇒ projection↑, health concern downside, stronger production ⇒ higher baseline, PPR ≠ Half-PPR for receivers, Floor &lt; Median &lt; Ceiling, confidence 0–100, league scoring refresh.

### Contracts

- `PlayerProjection`, `ProjectionLeagueContext`, `PlayerProductionSnapshot` — `Playbook.Core.Projections.Models`
- `IProjectionEngine`, `IProjectionService`, `IPlayerProductionProvider` — `Playbook.Application.Projections.Interfaces`
- `ProjectionEngine`, `ProjectionService`, `PlayerProductionProvider` — `Playbook.Infrastructure.Projections.Services`
- UI feature folder — `Playbook.Web.Features.Projections`

### UI surfaces

- Player Overlay **Projection** tab (player-specific reasoning)
- Player Explorer sortable **Projected Points**
- Developer Monitor: Players Projected, Unique Projection Values, Average Projection, Average Confidence, Projection Runtime

## Player Overlay

`PlayerOverlay` is the single reusable surface for player details. It opens above the current page (no navigation), hosted from `MainLayout`, so Dashboard, Player Explorer, and future features share one experience.

### PlayerContext

`PlayerContext` wraps a `Player` with league-aware fantasy fields: scoring type, weekly projection, ROS rank, positional rank, VORP, recommendation summary, and confidence. The overlay consumes `PlayerContext`, not raw `Player`.

### League-aware player rendering

`IPlayerOverlayState` keeps the selected player id. On league switch (`ILeagueState.Changed`), it refreshes context via `IPlayerContextService` so fantasy values update while the player stays selected. The app top bar stays above the overlay (z-index) so the league switcher remains usable without closing the player.

### Contracts

- `IPlayerContextService` / `MockPlayerContextService` — builds context (UI never calculates fantasy values)
- `IPlayerOverlayState` / `PlayerOverlayState` — open/close/refresh

### Future API replacement strategy

Replace `MockPlayerContextService` with projection/value engines that still return `PlayerContext`. Keep `PlayerOverlay` and `IPlayerOverlayState` unchanged. Wire more surfaces through `RelatedPlayerId` / overlay open calls.
