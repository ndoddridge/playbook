# Real Historical Data Coverage — 2018 Week 7

Audit of nflverse public releases for the first real-data replay target.

Statuses: **AVAILABLE** · **PARTIAL** · **UNAVAILABLE**

| Domain | Status | Source | Notes |
|---|---|---|---|
| Player IDs (GSIS) | AVAILABLE | `weekly_rosters` / `player_stats` / `injuries` | Primary join key. PlaybookId derived deterministically as `playbook:gsis:nfl:{gsisId}`. |
| Player names | AVAILABLE | weekly rosters / stats | Display only — never used as sole join key. |
| Positions | AVAILABLE | weekly rosters / stats | Skill map QB/RB/WR/TE/K; FB/HB → RB. Non-skill skipped for Start/Sit. |
| Teams | AVAILABLE | weekly rosters / stats / schedules | Team abbreviations. |
| Weekly NFL rosters | AVAILABLE | `roster_weekly_{season}.csv` | Week-7 ACT rows used as player universe. No exact publish timestamp — treated as week-level pre-game roster state (documented limitation). |
| Weekly player statistics | AVAILABLE | `player_stats_{season}.csv.gz` | Week 7 rows are **outcomes only**. Weeks 1–6 used for pre-game recent production / opportunity proxies. |
| Game schedules / kickoffs | AVAILABLE | `schedules/games.csv` | Used to set information cutoff before first Week 7 kickoff (2018 TNF). |
| Injuries | PARTIAL | `injuries_{season}.csv` | Week-7 rows with `date_modified <= cutoff` included. Rows after cutoff excluded. Exact practice-report cadence not fully reconstructed. |
| Depth charts | PARTIAL | `depth_charts_{season}.csv` | **Week 6** depth used as pre-Week-7 role signal (no trustworthy pre-kickoff timestamp on Week 7 depth). |
| Snap counts | PARTIAL | `snap_counts_{season}.csv.gz` | **Weeks 1–6 only** for usage proxies. Week 7 snaps are post-game and excluded from pre-game context. |
| Fantasy opportunity/usage | PARTIAL | Derived from weeks 1–6 targets/carries/attempts + snaps | Transparent 0–100 heuristics — not official nflverse “opportunity” products. |
| Historical fantasy league ownership | UNAVAILABLE | — | No week-by-week Sleeper/ESPN ownership archive. Replay uses a **reconstructed lab roster** from pre-week production (labeled, not historical ownership). |
| Pre-week projections (external archive) | UNAVAILABLE | — | No vendor as-of projection archive. |
| Pre-week projections (reconstructed baseline) | PARTIAL | Feature reconstructor + Baseline A/B | Built only from weeks 1..(N-1). Transparent baselines — not a trained model. |
| News archive | UNAVAILABLE | — | Not integrated. |
| Betting / matchup lines | UNAVAILABLE | — | Schedules contain some market columns; not used as pre-game intelligence in this step. |

## Temporal policy (2018 Week 7)

- First kickoff: **2018-10-18 20:20 America/New_York** (DEN @ ARI TNF)
- Information cutoff: **2018-10-18T20:00:00-04:00** (20 minutes before first kickoff)
- Pre-game: weeks 1–6 stats/snaps, week 6 depth, week 7 roster identity, injuries with `date_modified <= cutoff`
- Post-game outcomes: week 7 `fantasy_points` / scoring from counting stats — attached only after decisions

## Honesty limits

Where nflverse lacks exact pre-game timestamps (weekly roster / depth chart publish time), Playbook marks the field **PARTIAL** and chooses the more conservative prior-week artifact rather than inventing timestamps.
