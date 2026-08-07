# Changelog

All notable project changes are recorded here.

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
- Feature folder structure under `src/FootballGenie.Web/Features/`

### Files created

- `src/FootballGenie.Web/wwwroot/css/design-system.css`
- `src/FootballGenie.Web/wwwroot/css/shell.css`
- `src/FootballGenie.Web/Components/Layout/NavMenu.razor`
- `src/FootballGenie.Web/Components/Layout/TopBar.razor`
- `src/FootballGenie.Web/Components/Shared/ComingSoonPage.razor`
- `src/FootballGenie.Web/Features/_Imports.razor`
- `src/FootballGenie.Web/Features/Dashboard/DashboardPage.razor`
- `src/FootballGenie.Web/Features/MyTeams/MyTeamsPage.razor`
- `src/FootballGenie.Web/Features/QuickPicks/QuickPicksPage.razor`
- `src/FootballGenie.Web/Features/DraftAssistant/DraftAssistantPage.razor`
- `src/FootballGenie.Web/Features/PlayerExplorer/PlayerExplorerPage.razor`
- `src/FootballGenie.Web/Features/ReplayLab/ReplayLabPage.razor`
- `src/FootballGenie.Web/Features/Settings/SettingsPage.razor`
- `docs/CHANGELOG.md`

### Files modified

- `src/FootballGenie.Web/wwwroot/app.css` — imports fonts, design system, and shell styles
- `src/FootballGenie.Web/Components/Layout/MainLayout.razor` — shell composition (sidebar / top bar / main)
- `src/FootballGenie.Web/Components/Layout/MainLayout.razor.css` — error UI aligned to design tokens
- `src/FootballGenie.Web/Components/_Imports.razor` — shared usings for layout and features

### Files removed

- `src/FootballGenie.Web/Components/Pages/Home.razor` — replaced by `Features/Dashboard/DashboardPage.razor`

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
