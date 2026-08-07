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
News Engine
    ↓
Intelligence Engine
    ↓
Projection Engine
    ↓
Decision Engine
    ↓
UI (Blazor)
```

| Engine | Responsibility |
| --- | --- |
| **Data Engine** | Ingest and normalize NFL, fantasy, and market data |
| **News Engine** | Monitor, deduplicate, and prioritize fantasy-relevant news |
| **Intelligence Engine** | Maintain live player value and risk scores |
| **Projection Engine** | Produce expected points, floor, ceiling, and confidence |
| **Decision Engine** | Turn scores and context into lineup, waiver, trade, and draft recommendations |

Engines communicate through clear contracts. Recommendations always carry action, confidence, impact, and reasoning so every suggestion remains explainable.

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

### Mock Service

`MockPlayerService` seeds ~20 players across QB/RB/WR/TE/K/DST with mock profiles. UI never constructs players.

### Future Data Engine integration

The Data Engine will populate and refresh player records (stats, injuries, depth charts). Swap `MockPlayerService` for a Data-Engine-backed `IPlayerService` without changing Player Explorer or downstream recommendation engines that depend on `PlayerProfile`.

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
