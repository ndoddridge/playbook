# Multi-Season Historical Benchmark (Frozen Model)

Developer measurement report. **No projection, decision, or confidence formulas were changed.**

Entry points:

```csharp
await HistoricalReplayCommands.RunDefaultMultiSeasonBenchmarkAsync(services);
// or
await HistoricalReplayCommands.RunMultiSeasonBenchmarkAsync(
    services, seasons: [2015, 2018, 2021, 2024]);
```

## Sample & roles (OOS structure)

| Season | Role | Why included |
|---|---|---|
| 2015 | Development | Pre-17-game / mid-PPR boom era |
| 2018 | **FrozenBenchmark** | Existing locked single-season scorecard |
| 2021 | Development | First 18-game season / modern usage |
| 2024 | **HoldoutTest** | Recent environment — do not tune against |

Future improvements should be developed on Development seasons and judged on HoldoutTest. Improving FrozenBenchmark alone is not sufficient evidence.

## Aggregate (all seasons)

| Metric | Value |
|---|---|
| Weeks | **70** (17+17+18+18) |
| Fair projections | **1077** |
| Decisions | 403 (379 graded) |
| Current model MAE | **11.70** |
| Baseline A MAE | **8.89** |
| Baseline B MAE | **11.70** (= current primary) |
| Bias (actual − predicted) | **−8.86** |
| Decision accuracy | **49.3%** |
| Avg / median / total decision value | −0.00 / −0.40 / **−0.70** |
| Worst / best decision value | −46.30 / +40.80 |
| Avg confidence | 29.0 |
| Seasons current beats A | **0 / 4** |
| Seasons Baseline A wins | **4 / 4** |

## Per-season

| Season | Proj N | Current MAE | Base A MAE | Δ vs A | Bias | Acc | Total dec. value | Conf |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 2015 | 241 | 11.47 | **8.71** | +2.76 (+31.7%) | −9.01 | 46.2% | −90.3 | 28.5 |
| 2018 | 261 | 11.62 | **9.03** | +2.59 (+28.7%) | −8.53 | 60.6% | **+300.0** | 28.4 |
| 2021 | 289 | 12.25 | **9.23** | +3.02 (+32.7%) | −9.96 | 46.1% | −34.8 | 29.9 |
| 2024 | 286 | 11.39 | **8.58** | +2.81 (+32.8%) | −7.94 | 44.8% | −175.6 | 29.2 |

## Baseline A head-to-head

Current model loses in **every** sampled season (~29–33% worse MAE).  
This is a **confirmed structural problem**, not a 2018 anomaly.

## Over-projection diagnosis

| Slice | Bias | Notes |
|---|---:|---|
| All seasons | −7.9 to −10.0 | Persistent |
| All skill positions | −7.0 to −9.6 | Not position-specific |
| Predicted ≥20 | −11.04 | Primary overshoot zone |
| Predicted 10–20 | −0.72 | Near calibrated |
| Limited history | −15.01 | Worse |
| Early weeks (1–6) | −12.28 | Worse than late season |

## Confidence calibration (all seasons)

| Bucket | n | Success | Avg decision value |
|---|---:|---:|---:|
| 0–20% | 89 | **57.5%** | +2.12 |
| 20–40% | 314 | 47.2% | −0.57 |
| 40–100% | 0 | — | — |

Higher confidence does **not** improve outcomes. Confidence is massed in low bands and inverted vs success.

## Structural findings classification

### A. Confirmed structural problems
1. Current model loses to Baseline A in 4/4 seasons.
2. Systematic over-projection bias across seasons and positions.
3. High projected scorers (≥20) have much larger MAE.
4. Confidence uncalibrated / massed in low bands.
5. Total decision value negative in 2015, 2021, and 2024.

### B. Possible problems
- Strong recommendation margins are not reliably better than mid margins in this sample.

### C. 2018-specific anomaly
- 2018 decision accuracy 60.6% vs ~45.7% mean of other seasons.
- 2018 total decision value (+300) is an outlier vs negative totals elsewhere.
- **Do not optimize to 2018.**

### D. Data limitations
- News archive: UNAVAILABLE
- Fantasy ownership: UNAVAILABLE (lab roster)
- Injuries / depth / snaps: PARTIAL

## Frozen 2018 lock

`Frozen2018SeasonBenchmark` constants and regression test require 2018 metrics to remain exactly:

- MAE 11.62 / Base A 9.03 / bias −8.53
- Accuracy 60.6% / total decision value +300 / avg conf 28.4

These must not be “improved” by silent retuning.

## Next experimental loop

MEASURE → IDENTIFY REPEATED FAILURE → HYPOTHESIZE → **ONE** controlled improvement → TEST ON UNSEEN (2024 holdout) → COMPARE

Do not train ML yet. Do not tune to 2018.
