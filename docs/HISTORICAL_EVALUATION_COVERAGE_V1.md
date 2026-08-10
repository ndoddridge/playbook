# Historical Evaluation Coverage Expansion V1

**ProtocolId:** `historical-evaluation-coverage-v1`  
**Date:** 2026-08-10  
**Status:** COMPLETE — measurement surface expanded; no model changes

## 1. What defines the “lab roster”

`NflverseHistoricalDataProvider.SelectLabRoster`:

- Universe seed: week-W **ACT** skill identities (QB/RB/WR/TE) from `roster_weekly_{season}.csv`
- Rank: pre-week (weeks `1..W-1`) fantasy PPG, tie-break GSIS
- Cap: **QB3 / RB5 / WR6 / TE3** (~17/week), with reconstructed starter flags QB1/RB2/WR3/TE1
- Explicitly **not** historical league ownership (marked UNAVAILABLE)

Before this change, both:

- Start/Sit candidates (`snapshot.Roster`)
- Quick Picks candidates (`snapshot.Players`)

were built from that same truncated lab roster.

## 2. Why it limited historical evaluation

The provider discarded the already-loaded ACT skill universe after ranking. Peer competition for Start/Sit and market boards for Quick Picks were artificially small (~17 players/week), inflating Top-N rates and hiding Limited-history / thin-margin cases.

## 3. Broader universe actually available

Already loaded each week from nflverse (cutoff-safe):

| Source | Content |
|---|---|
| `roster_weekly` | All ACT skill players for week W |
| `player_stats` weeks `1..W-1` | Prior REG games for reconstruction |
| `player_stats` week W | Outcomes (segregated) |
| Snaps / depth / injuries | Partial pre-cutoff signals |

## 4. Completely evaluable candidates

A player-week is fully evaluable when:

1. ACT skill identity on week W
2. ≥1 prior REG game (valid reconstructed projection)
3. Week-W outcome row present (for grading)

## 5. Still excluded

| Reason | Why |
|---|---|
| Non-skill / K / DST | Filtered by `IsSkillPosition` |
| Non-ACT roster status | Identity loader skips |
| No prior REG games | Invalid projection; QP skips `projected ≤ 0` |
| No week-W stats row | Ungraded |
| Real league ownership | UNAVAILABLE — not fabricated |
| Prop lines / as-of vendor projections | UNAVAILABLE — not fabricated |
| News archive | UNAVAILABLE |

Non-skill / non-ACT exclusions occur **before** snapshot construction (counts appear as 0 inside the snapshot tally).

## Implementation

`HistoricalCandidateUniverse`:

| Mode | Players | Start/Sit Roster | Default? |
|---|---|---|---|
| `LabRoster` | SelectLabRoster only | same | **Yes** — frozen 2018 lock |
| `ExpandedSkillUniverse` | All ACT skill identities | All of them; starters QB1/RB2/WR3/TE1 uncapped bench | Opt-in for measurement / next knowledge runs |

No Projection V2 / Confidence V2 / Decision Policy V1 / Knowledge transform changes. Production remains `KnowledgeMode.Passthrough`.

## BEFORE vs AFTER counts

See `docs/HISTORICAL_EVALUATION_COVERAGE_V1_REPORT.txt`.

### 2018 W1–17 (development / frozen-benchmark season — counts only)

| Metric | BEFORE | AFTER | Δ |
|---|---:|---:|---:|
| Distinct players | 61 | 653 | +592 |
| Player-weeks | 289 | 8026 | +7737 |
| Start/Sit candidates | 289 | 8026 | +7737 |
| Start/Sit predictions | 99 | 111 | +12 |
| Quick Picks predictions | 576 | 12233 | +11657 |

### 2024 W1–17 (holdout — isolated; not used for tuning)

| Metric | BEFORE | AFTER | Δ |
|---|---:|---:|---:|
| Distinct players | 68 | 640 | +572 |
| Player-weeks | 289 | 7069 | +6780 |
| Start/Sit candidates | 289 | 7069 | +6780 |
| Start/Sit predictions | 100 | 111 | +11 |
| Quick Picks predictions | 576 | 11128 | +10552 |

Note: Start/Sit **prediction** count stays sparse because the DecisionEngine grades the UI recommendation set (Start + limited Sit), not one prediction per roster member. The **candidate / peer** set is what expanded.

## Sufficiency for next Knowledge experiment

**Yes, with opt-in.** Quick Picks measurement surface is now market-wide ACT skill. Start/Sit peer competition is expanded. Next controlled Knowledge experiment should set `CandidateUniverse = ExpandedSkillUniverse` and keep LabRoster for frozen 2018 regression locks.

Do **not** start the 20-season simulation yet.
