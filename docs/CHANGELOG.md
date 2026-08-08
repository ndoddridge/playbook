# Changelog

All notable project changes are recorded here.

## [Unreleased] — Injury Data + Modal Top Bar Fix

### Added

- Dedicated `IPlayerInjuryProvider` / `IPlayerInjuryService` with Mock + Live (ESPN injuries + Sleeper practice/status enrichment)
- Normalized `PlayerInjuryRecord` with current + historical preservation via JSON cache
- Injuries tab: current status/injury/practice/game status, recent + historical history, source/last updated
- Intelligence consumes structured injury facts (`InjuryReport` source) via existing rule ids
- Projection Engine applies conservative availability multipliers for Out / IR / Doubtful / Questionable / Limited
- Developer Monitor injury sync metrics; background refresh includes injuries (isolated failure)

### Fixed

- Player Detail modal on mobile now starts below `--pb-topbar-height` so content is not hidden under the top bar

### Limitations

- ESPN feed is a current snapshot; history accumulates across syncs rather than providing lifelong medical records
- Practice designations are sparse on Sleeper outside active report weeks

## [Unreleased] — College Statistics + Player Modal Polish

### Added

- Dedicated `ICollegeStatsProvider` with `MockCollegeStatsProvider` + `LiveCollegeStatsProvider` (ESPN college-football athlete stats)
- College JSON cache + Developer Monitor: College Provider / Players / Seasons / Last Sync / Error
- College tab renders real season box scores (school, seasons, passing/rushing/receiving) when available
- Career season selector promotes College for players with fewer than 3 NFL seasons

### Fixed

- Player detail modal responsive layout (viewport-fit sheet, tab strip scroll, season select width, no horizontal page overflow)
- Removed misleading “college detail not supplied” empty copy when college data can be loaded

### Limitations

- ESPN college tables often omit games played and targets; those fields stay null (never fabricated)
- College sync covers young skill players with resolvable ESPN roster ids (capped per sync)

## [Unreleased] — Historical Player Statistics Layer

### Added

- `PlayerSeasonStats` normalized model (passing / rushing / receiving / fantasy; Completed / Current / College periods)
- `IPlayerStatsProvider` with `MockPlayerStatsProvider` + `LivePlayerStatsProvider` (Sleeper season stats)
- `IPlayerStatsService` / `PlayerStatsService` with JSON file cache, refresh, and mock fallback
- Player Overlay Career/Stats season switcher (NFL completed, current season, college)
- Developer Monitor: Stats Provider, Players With Stats, Seasons Loaded, Current/Historical records, sync runtime/error
- Projection production path prefers stats-service seasons before curated/attribute fallbacks

### Architecture notes

- NFL stats reuse Sleeper; college stats use a dedicated ESPN-backed provider
- Local cache under app `data/` directory; TTL configurable via `PlayerStats:CacheTtlMinutes` / `CollegeStats:CacheTtlMinutes`

## [Unreleased] — Player-Specific Projection Fix

### Fixed

- Projection baselines are now player-specific production (curated season box scores), not flat position constants
- Elite QBs/RBs/WRs/TEs (Mahomes, Allen, Daniels, Barkley, Bijan, Chase, Kelce, …) produce differentiated projections and reasoning
- Intelligence adjusts volume/downside/ceiling on top of production; league scoring recalculates fantasy points from components

### Added

- `PlayerProductionSnapshot` + `IPlayerProductionProvider` (curated catalog → profile stats → attribute fallback)
- `FantasyScoring` helper (Standard / Half-PPR / PPR from box-score components)
- Differentiation validation tests (same-position variance, opportunity, health, production, scoring, floor/median/ceiling)
- Developer Monitor: Unique Projection Values, Average Projection (plus existing Players Projected / Runtime / Confidence)

### Limitations

- Live Sleeper provider still does not supply season stats; unknown players use attribute fallback until a live stats provider is wired behind `IPlayerProductionProvider`

## [Unreleased] — Projection Engine V1

### Added

- `PlayerProjection` — expected fantasy points, floor, median, ceiling, confidence, volatility, reasoning, supporting intelligence
- `IProjectionEngine` / `ProjectionEngine` — deterministic weighted rules over `PlayerIntelligenceProfile` + player + league context
- `IProjectionService` / `ProjectionService` — cached projections with league-aware refresh
- Configurable rules via `Projection:Rules`
- Player Overlay **Projection** tab
- Player Explorer sortable **Projected Points** column
- Developer Monitor projection telemetry
- Background refresh re-runs projections after intelligence

### Architecture notes

- Projection estimates outcomes only — never start/sit, waiver, draft, or trade advice
- Future Decision / Quick Picks / Draft Assistant / Waiver Assistant / Trade Analyzer must consume `PlayerProjection`
- Rules are centralized and explainable; same production + intelligence + league ⇒ same projection

## [Unreleased] — Intelligence Aggregation (Player Profiles)

### Added

- `PlayerIntelligenceProfile` — canonical per-player intelligence (health, opportunity, usage, risk, momentum, trend, supporting facts)
- `IIntelligenceAggregator` / `IntelligenceAggregator` — groups facts, dedupes, applies weighted scoring
- Configurable scoring rules via `Intelligence:Scoring` (centralized deltas for limited practice, full practice, starter language, signings, etc.)
- Dashboard **Top Player Intelligence Changes** (⬆/⬇ player + headline + confidence)
- Player Overlay Intelligence tab shows full profile scores + category-grouped supporting facts
- Developer Monitor: Profiles Generated, Facts Aggregated, Average Facts Per Player, Aggregation Runtime

### Architecture notes

- Future Projection / Prediction / Decision engines must consume `PlayerIntelligenceProfile`, not raw `IntelligenceFact`s
- Aggregation is deterministic given the same fact set and scoring config

## [Unreleased] — Intelligence Engine V1

### Added

- Rule-based `IntelligenceAnalyzer` turning `NewsArticle` + player catalog into deterministic `IntelligenceFact`s
- Live `IntelligenceService` replacing mock-as-default for the app pipeline
- Categories expanded: Depth Chart, Practice, Transaction, Suspension, Contract, Game Environment, Team Chemistry, General
- `RelatedNewsArticleIds` on facts for explainability / source links
- Dashboard **Top Intelligence** (replaces Latest Football News)
- Player Overlay Intelligence tab: summary, recent facts, confidence/importance, supporting articles + links
- Developer Monitor: Articles Processed, Facts Generated, Analyzer Runtime, Last Analysis Time
- Background refresh now re-runs intelligence after news updates

### Architecture notes

- Deterministic: same news + players ⇒ same fact ids/outputs
- Explainable: every fact cites rule id, matched phrase, and source article
- No ML/LLMs — heuristics only; future ML can replace the analyzer behind `IIntelligenceAnalyzer`

## [Unreleased] — Live News Provider

### Added

- Normalized `NewsArticle` domain model (title, summary, published, source, url, related players/teams, category, priority)
- `INewsProvider` application facade with `MockNewsProvider` + `LiveNewsProvider` (ESPN NFL news API)
- Configuration switch `News:Provider` = `Mock` | `Live` with automatic mock fallback
- Dashboard **Latest Football News** card (headline, published time, source, priority, summary)
- Player Overlay **Recent News** section (name-mapped related articles when the API lacks Playbook ids)
- `INewsSyncStatus` Developer Monitor fields: Current News Provider, Articles Loaded, Last News Sync, response time
- `DataRefreshBackgroundService` periodically refreshes player data and news (logged separately)

### Architecture notes

- UI consumes only `INewsProvider` — never Mock/Live concretions
- Live source: ESPN public site API; auth slot reserved in `News:Espn:ApiKey`
- Athlete names from ESPN are mapped onto Playbook `Player` ids via catalog name matching
- Not the Intelligence Engine — news is normalized input Intelligence will consume later

## [Unreleased] — Live Player Data Provider

### Added

- `IPlayerDataProvider` abstraction with `MockPlayerDataProvider` and `LivePlayerDataProvider` (Sleeper NFL API)
- Configuration switch `PlayerData:Provider` = `Mock` | `Live` (no code changes to flip sources)
- `PlayerService` loads from the configured provider and **automatically falls back to mock** on live failure
- `IPlayerDataSyncStatus` telemetry: configured/active provider, last sync, player count, response time, last error
- Developer Monitor fields for provider status
- Auth isolation via `PlayerData:Sleeper:ApiKey` (unused for public Sleeper reads; ready for future keys)

### Architecture notes

- UI still consumes only `IPlayerService` — Player Explorer is unchanged
- Sleeper selected as the first live provider: free public API with players, teams, positions, and status
- Future News / Odds / Weather / Schedules / Injuries providers should follow the same provider + config + fallback pattern

## [Unreleased] — Developer Monitoring Dashboard

### Added

- Automatic dashboard refresh every 30 seconds (recommendations, intelligence, status timestamps)
- **Development Status** card: Build Status, Background Service Status, Last Update Time, Mock Data Status, Current League, Application Version, Current Time
- Heartbeat indicator (`🟢 Running`) that visibly ticks on each refresh
- **Engine Status** section with mock states for Player, League, Recommendation, Intelligence, and Data engines (`Ready` / `In Development` / `Offline`)
- **Developer Mode** badge in the top bar
- Shared `AppInfo` version (`0.1.0-dev`) shown in the sidebar footer as `Playbook v0.1.0-dev`
- Mobile-friendly stacking for monitor and dashboard cards

### Architecture notes

- Monitoring values are developer UX only — mock/static status strings, not wired to real build or background services yet
- Refresh timer lives on the Dashboard page; heartbeat flash is CSS-driven so phone checks show the app is alive
- `AppInfo` is the single version string for footer and Development Status

### Future extension points

- Replace mock engine/build/background statuses with health checks from real engines and hosted services
- Persist last-refresh telemetry and expose a shared `IHealthStatus` application port
- Optionally surface heartbeat in the top bar for all pages, not only Dashboard

## [Unreleased] — Intelligence Engine Foundation

### Added

- `IntelligenceFact` and `PlayerIntelligence` domain models (football-only)
- Enums: `IntelligenceCategory`, `IntelligenceImportance`, `IntelligenceSource`
- `IIntelligenceService` / `MockIntelligenceService` (~75 mock facts)
- Feature layout: Core Models, Application Interfaces, Infrastructure Services, Web UI helpers
- Player Overlay **Intelligence** tab (facts grouped by category)
- Dashboard **Football Intelligence** panel (highest-priority facts)

### Architecture notes

- Intelligence Engine produces structured football insights — not predictions, recommendations, or fantasy values
- Downstream engines should consume `PlayerIntelligence` rather than assembling facts themselves
- Reuses shared `TrendDirection` from the Player domain (no duplicate enum)

## [Unreleased] — Player Overlay

### Added

- `PlayerContext` league-aware player view model
- `IPlayerContextService` / `MockPlayerContextService`
- `IPlayerOverlayState` / `PlayerOverlayState` with league-change refresh
- Reusable `PlayerOverlay` component (Overview / Fantasy / Career / College / Injuries)
- Player Explorer opens overlay without navigation
- Decision Card titles open overlay via `Recommendation.RelatedPlayerId`

### Architecture notes

- Overlay is the single player detail surface across features
- Fantasy values come only from `IPlayerContextService`
- League switching updates contextual fantasy data while keeping the same player selected

## [Unreleased] — Player Engine Foundation

### Added

- Player domain model and enums (`Position`, `PlayerStatus`, `InjuryStatus`, `TrendDirection`)
- Supplemental structures: `SeasonStats`, `CareerStats`, `CollegeStats`, `InjuryRecord`, `PlayerTrend`, `PlayerProfile`
- `IPlayerService` and `MockPlayerService` (~20 mock players across QB/RB/WR/TE/K/DST)
- Player Explorer with search, scrollable list, headshot placeholder, and profile panel
- Docs for Player Engine / PlayerProfile / future Data Engine integration

### Architecture notes

- UI never creates `Player` objects — everything comes from `IPlayerService`
- Engines should request `PlayerProfile` as the aggregated unit
- Future Data Engine replaces only the service implementation

## [Unreleased] — Recommendation Domain Model

### Added

- Central `Recommendation` domain model with extensible metadata support
- Enums: `RecommendationType`, `RecommendationPriority`, `RecommendationStatus`, `RecommendationCategory`, `EngineType`
- `IRecommendationService` application contract
- `MockRecommendationService` as the single source of mock recommendations
- `RecommendationPresentation` helpers for UI labels/CSS hooks
- Web feature boundary `Features/Recommendations`

### Changed

- `DecisionCard` now accepts a `Recommendation` (display-only; no football knowledge)
- Dashboard loads Top Decisions from `IRecommendationService` — hardcoded recommendation arrays removed
- Retired the interim `Decision` model in favor of `Recommendation`

### Recommendation Model

Every future engine should emit `Recommendation` objects. The UI never creates them; it only renders what services/engines provide.

### Recommendation Pipeline

Engines → `IRecommendationService` → Decision Card. Mock service stands in until real engines exist.

## [Unreleased] — Decision Card System

### Added

- Generic `Decision` model and enums (`DecisionActionType`, `DecisionPriority`, `DecisionStatus`) in Core
- Reusable `DecisionCard` Blazor component with action styling, priority, confidence, impact, status, category, and timestamp
- Expand/collapse details for reasoning, supporting signals, evidence, and future notes
- Distinct icon + accent color per action type (Start, Bench, Trade, Waiver, Add, Drop, Hold, Draft, Quick Pick, News)
- Dashboard Top Decisions section powered by 5 mock Decision Cards
- Shared `DecisionPresentation` helpers for labels, icons, and CSS hooks

### Decision Card component

`DecisionCard` is a pure presentation component. It accepts a `Decision` object and renders it — it contains no fantasy-football knowledge and no recommendation logic. Engines will populate `Decision` instances later.

### Expand/collapse behavior

Clicking a card toggles an expanded details panel with smooth fade-up animation. Collapsed state shows action, title, summary, confidence, priority, impact, status, category, and timestamp.

### Reuse strategy

Use `DecisionCard` anywhere recommendations appear (Dashboard, My Teams, Draft Assistant, Quick Picks, Replay Lab). Pass different `Decision` payloads; keep styling centralized in `decision-card.css`.

### Future recommendation engines

Decision, Projection, and related engines should emit `Decision` objects (action, confidence, impact, explainable reasoning). Swap dashboard mock arrays for injected services without rewriting the card UI.

## [Unreleased] — League Management Foundation

### Added

- Domain `League` model and enums (`LeaguePlatform`, `LeagueType`, `ScoringType`) in Core
- `ILeagueService` and `ILeagueState` application contracts
- `LeagueStateService` for process-lifetime selected league + change notifications
- `MockLeagueService` with Friends League, Dynasty League, and Work League
- `LeagueSwitcher` top-bar dropdown for live league selection
- Dashboard league context header (league, platform, type, scoring, week)
- xUnit coverage for mock selection and state notifications

### Files created

- `src/Playbook.Core/Leagues/*`
- `src/Playbook.Application/Leagues/*`
- `src/Playbook.Infrastructure/Leagues/MockLeagueService.cs`
- `src/Playbook.Web/Features/League/Components/LeagueSwitcher.razor`
- `src/Playbook.Web/Features/League/Models/LeagueDisplay.cs`
- `src/Playbook.Web/wwwroot/css/league.css`
- `tests/Playbook.Tests/LeagueStateTests.cs`

### Files modified

- `DependencyInjection` in Application and Infrastructure
- `Program.cs` registration order
- `TopBar.razor`, `DashboardPage.razor`, `app.css`, docs

### Architectural decisions

- Domain model lives in Core so engines can consume it without referencing Web
- Mock catalog in Infrastructure; selection notifications in Application state
- Singleton DI keeps the selected league stable across navigation

## [Unreleased] — Rebrand to Playbook

### Changed

- Official product and solution name renamed from Football Genie to **Playbook**
- Solution file renamed to `playbook.sln`
- Projects and namespaces renamed: `Playbook.Core`, `Playbook.Application`, `Playbook.Infrastructure`, `Playbook.Web`, `Playbook.Tests`
- UI branding, page titles, docs, and design-system CSS token prefix (`--pb-*`) updated to Playbook
- Git remote points to `https://github.com/ndoddridge/playbook.git`

## [Unreleased] — Application Shell

### Added

- Central design system tokens and primitives in `wwwroot/css/design-system.css`
- Application shell styles in `wwwroot/css/shell.css`
- App shell layout with left sidebar, top navigation bar, and main content area
- Sidebar navigation for Dashboard, My Teams, Quick Picks, Draft Assistant, Player Explorer, Replay Lab, and Settings
- Top bar showing Current League, Sync Status, live Current Time, and a reserved profile slot
- Dashboard page with four mock cards: Latest NFL News, Top Decisions, Trending Players, System Status
- Placeholder feature pages (Coming Soon) for My Teams, Quick Picks, Draft Assistant, Player Explorer, Replay Lab, and Settings
- Shared `ComingSoonPage` component for consistent placeholder pages
- Feature folder structure under `src/Playbook.Web/Features/`

### Files created

- `src/Playbook.Web/wwwroot/css/design-system.css`
- `src/Playbook.Web/wwwroot/css/shell.css`
- `src/Playbook.Web/Components/Layout/NavMenu.razor`
- `src/Playbook.Web/Components/Layout/TopBar.razor`
- `src/Playbook.Web/Components/Shared/ComingSoonPage.razor`
- `src/Playbook.Web/Features/_Imports.razor`
- `src/Playbook.Web/Features/Dashboard/DashboardPage.razor`
- `src/Playbook.Web/Features/MyTeams/MyTeamsPage.razor`
- `src/Playbook.Web/Features/QuickPicks/QuickPicksPage.razor`
- `src/Playbook.Web/Features/DraftAssistant/DraftAssistantPage.razor`
- `src/Playbook.Web/Features/PlayerExplorer/PlayerExplorerPage.razor`
- `src/Playbook.Web/Features/ReplayLab/ReplayLabPage.razor`
- `src/Playbook.Web/Features/Settings/SettingsPage.razor`
- `docs/CHANGELOG.md`

### Files modified

- `src/Playbook.Web/wwwroot/app.css` — imports fonts, design system, and shell styles
- `src/Playbook.Web/Components/Layout/MainLayout.razor` — shell composition (sidebar / top bar / main)
- `src/Playbook.Web/Components/Layout/MainLayout.razor.css` — error UI aligned to design tokens
- `src/Playbook.Web/Components/_Imports.razor` — shared usings for layout and features

### Files removed

- `src/Playbook.Web/Components/Pages/Home.razor` — replaced by `Features/Dashboard/DashboardPage.razor`

### Architectural decisions

- **Feature folders under Web** — UI routes live in `Features/{Name}` so each product area can grow without a flat `Pages` dump.
- **Centralized CSS tokens** — visual values live in the design system; shell and components consume CSS variables instead of scattered hard-coded styles.
- **Layout owns chrome** — `MainLayout` composes `NavMenu` and `TopBar`; pages only render feature content.
- **Mock data stays in the UI** — dashboard placeholders are local static content; no Application/Infrastructure coupling yet.
- **Interactive Server at the root** — `Routes` / `HeadOutlet` use Interactive Server render mode so the shell (sidebar toggle, live clock) is interactive without putting a render mode on `MainLayout` (layouts cannot accept `@Body` across a render-mode boundary).

### Future extension points

- Replace top-bar league/sync constants with injected application state (selected league, sync engine status).
- Swap dashboard mock arrays for Application services backed by Data/News/Decision engines.
- Expand each `Features/*` folder with feature-specific components, view models, and tests.
- Fill the reserved profile slot with auth/user controls.
- Persist sidebar collapse preference and promote design-system primitives into reusable Razor components as patterns stabilize.
