# Injury Data Sources — Audit & Selection

Last verified against live API responses (2026-08-08).

## Current stack (selected)

| Concern | Provider | Auth | Notes |
| --- | --- | --- | --- |
| Current NFL injuries | ESPN `site/v2/sports/football/nfl/injuries` + Sleeper player injury fields | None | Current-report snapshot only |
| Historical NFL injuries | **nflverse** injury CSVs (2009+) | None | Official weekly injury reports; free GitHub releases |
| College injuries | — | — | **Not available** from selected free/stable sources |
| Practice / game status | ESPN + Sleeper (current); nflverse (historical weeks) | None | Historical practice_status on nflverse rows |
| Injury news | ESPN news (existing Live news provider) | None | Fed into Unconfirmed/Reported signals |

## Provider audit

### ESPN (configured Live current)

- **Endpoint:** `https://site.web.api.espn.com/apis/site/v2/sports/football/nfl/injuries`
- **Provides:** Current team injury groups, athlete display name, status, comments, date, athlete links
- **Does not provide:** Career historical injury archive, college injuries, stable Playbook mapping by ESPN id in our catalog (we match name+team)
- **IDs:** Athlete ids exist on the site but are not required for current mapping

### Sleeper (configured Live enrichment + player catalog)

- **Endpoints:** `https://api.sleeper.app/v1/players/nfl`, position-filtered variants
- **Provides:** Player catalog, `injury_status`, practice fields, **`gsis_id`**, **`espn_id`**, yahoo/etc.
- **Does not provide:** Historical injury timeline, college injuries
- **IDs:** `player_id` (Sleeper) → Playbook Guid via MD5; `gsis_id` / `espn_id` stored in `PlaybookPlayerIdentity`

### nflverse (selected historical NFL)

- **Source:** `https://github.com/nflverse/nflverse-data/releases/tag/injuries`
- **Files:** `injuries_{season}.csv` (2009–present)
- **Fields used:** season, week, team, gsis_id, full_name, report_primary/secondary_injury, report_status, practice_status, date_modified
- **Provides:** Multi-season NFL official injury/practice reports; body part; status; practice participation
- **Does not provide:** College injuries; explicit games-missed totals (left null)
- **License/use:** Public research dataset releases; no API key
- **Join key:** `gsis_id` ↔ Sleeper `gsis_id` via identity directory

### SportsDataIO / commercial APIs

- Richer injury + estimated return timelines; **requires paid API key**
- Not selected for default Playbook path (cost/credentials). Config surface can be added later behind `IHistoricalInjuryProvider`.

### College injuries

- No reliable free machine-readable college injury history API was found that meets Playbook’s bar (stable, legal, consistent IDs).
- `ICollegeInjuryProvider` remains the extension point; Live uses `NullCollegeInjuryProvider` and reports `NotSupportedByProvider`.

## Source confidence

| Label | Meaning |
| --- | --- |
| Verified | Structured official report (ESPN/Sleeper current designation or nflverse historical row) |
| Reported | Reliable news language describing practice/designation without structured feed confirmation |
| Unconfirmed | Speculative buzz (“reportedly”, “dealing with”, etc.) |
| Unknown | Insufficient information |

These are never silently merged.

## Config

```json
"Injuries": {
  "Provider": "Live",
  "HistoricalProvider": "Nflverse",
  "HistoricalSeasonCount": 8,
  "CacheFileName": "player-injuries-cache.json",
  "CacheTtlMinutes": 180,
  "TimeoutSeconds": 90
}
```

No API keys required for the default Live + nflverse path.
