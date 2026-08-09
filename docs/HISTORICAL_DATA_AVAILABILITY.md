# Historical Data Availability Assessment (Replay v1)

Internal audit of whether Playbook can currently support a trustworthy ~20-year week-by-week replay.

Statuses:

- **AVAILABLE** — can be reconstructed or computed reliably for historical weeks
- **PARTIAL** — some history exists, but as-of cutoff coverage or depth is incomplete
- **UNAVAILABLE** — no integrated historical source; must not be fabricated

| Domain | Status | Notes |
|---|---|---|
| Player game statistics (nflverse) | PARTIAL | Season archives via `NflversePlayerStatsProvider`; sync depth capped; live stats path is not cutoff-filtered |
| Fantasy scoring from counting stats | AVAILABLE | `LeagueFantasyScoring` for Standard / Half PPR / PPR |
| Historical fantasy rosters / ownership | UNAVAILABLE | Live Sleeper / mock only |
| Injuries (as-of) | PARTIAL | nflverse history exists; live services are current-state; Replay enforces cutoff only inside the replay pipeline |
| News archive | UNAVAILABLE | Live ESPN feed only |
| Depth charts | UNAVAILABLE | Not integrated |
| Snap / usage shares | PARTIAL | Limited proxies in recent stats; not general as-of signals |
| Projections (as-of) | UNAVAILABLE | Live engine only; Replay v1 uses controlled fixture projections |
| Betting / matchup context | UNAVAILABLE | Live odds / unavailable stubs |
| Player availability universe | PARTIAL | Current catalog; no retired as-of snapshots |
| Decision records / outcomes | PARTIAL | Schema ready; in-memory store only |
| NFL calendar / week identity | PARTIAL | `NflWeekRef` identity exists; calendar service is live-oriented |

## Implication for 20-year replay

Replay Engine v1 proves the **pipeline** (snapshot → knowledge → decision → record → outcome → grade) with a controlled fixture and hard information-cutoff enforcement.

**Real-data status (2018 Week 7):** nflverse-backed `IHistoricalDataProvider` can load one real week end-to-end. See `docs/REAL_HISTORICAL_DATA_COVERAGE_2018_W7.md`.

**Multi-week measurement (2018 W1–17):** `IMultiWeekHistoricalReplayRunner` replays inclusive week ranges with independent cutoffs, season scorecard, confidence buckets, and failure ledger. See `docs/SEASON_SCORECARD_2018.md`.

**Multi-season frozen benchmark (2015/2018/2021/2024):** `IMultiSeasonHistoricalBenchmarkRunner` runs identical frozen-model evaluation across diverse eras with OOS roles (Development / FrozenBenchmark / HoldoutTest). See `docs/MULTI_SEASON_BENCHMARK.md`.

Before expanding to ~20-year runs, still need:

1. As-of projection archives (currently **UNAVAILABLE** — reconstructed baselines used instead)
2. Week-by-week historical fantasy league ownership (lab roster is reconstructed, not historical)
3. Stronger cutoff-safe injury/news reconstruction
4. Durable decision/outcome storage
5. Model improvements driven by measured failure patterns (do not tune to a single season yet)

Until those exist, mark missing domains **UNAVAILABLE** inside snapshots rather than inventing them.
