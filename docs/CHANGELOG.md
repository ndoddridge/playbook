# Changelog

All notable project changes are recorded here.

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
