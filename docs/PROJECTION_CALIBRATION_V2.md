# Experiment 1 — Projection Calibration V2

## Hypothesis

The current projection system systematically over-projects, and a simple calibration layer can reduce projection error without overfitting.

## Success criteria (pre-registered before 2024 holdout)

Recorded in `ProjectionCalibrationSuccessCriteria` before the official holdout run:

1. Development LOOCV MAE improves ≥10% vs V1  
2. Development LOOCV |bias| reduces ≥50% vs V1  
3. Official 2024 MAE improves ≥5% vs V1  
4. Official 2024 |bias| reduces ≥40% vs V1  
5. Official 2024 total decision value does not degrade by more than 50 points vs V1  

MAE-only improvement with worse decisions is **not** automatic success.

## Baseline

**Projection V1** = `OpportunityAwareProjectionEngine` (`baseline-opportunity-aware-v1`)  
Unchanged. Still the default primary for frozen benchmarks.

**Baseline A** = recent-average (`baseline-recent-average-v1`) — comparison only.

## Method

1. Collect fair (V1 predicted, actual) pairs from development seasons only: **2015, 2018, 2021**.  
2. **2024 never read during fitting.**  
3. Leave-one-season-out validation across development seasons.  
4. Candidate forms compared: GlobalScale, GlobalAffine, PiecewiseScaleAt20, PiecewiseAffineAt20.  
5. Selected by mean validation MAE (tie-break |bias|).  
6. Refit selected form on all development observations.  
7. **Freeze** parameters in `FrozenProjectionCalibrationV2`.  
8. ONE official 2024 holdout evaluation.

### Frozen V2

```
Method: PiecewiseScaleAt20
x < 20  →  0.9240 * x
x ≥ 20  →  0.6005 * x
```

Confidence formulas were **not** changed.

## Development validation (LOOCV)

| Model | N | MAE | RMSE | Bias |
|---|---:|---:|---:|---:|
| V1 | 791 | 11.81 | 14.31 | −9.20 |
| **V2** | 791 | **7.59** | **9.47** | **+0.49** |
| Baseline A | 791 | 9.01 | 10.89 | −4.35 |

LOOCV folds (selected method):

| Val season | V1 MAE | V2 MAE | A MAE | V1 bias | V2 bias |
|---|---:|---:|---:|---:|---:|
| 2015 | 11.47 | 7.15 | 8.71 | −9.01 | +0.43 |
| 2018 | 11.62 | 7.84 | 9.03 | −8.53 | +2.32 |
| 2021 | 12.25 | 7.73 | 9.23 | −9.96 | −1.12 |

Relative to V1: MAE −35.7%, |bias| −94.7%. V2 also beats Baseline A on development LOOCV MAE.

## Official holdout — 2024 (single evaluation)

| Model | N | MAE | RMSE | Bias |
|---|---:|---:|---:|---:|
| V1 | 286 | 11.39 | 13.91 | −7.94 |
| **V2** | 286 | **7.54** | **9.66** | **+0.91** |
| Baseline A | 286 | 8.58 | 10.80 | −3.28 |

Relative to V1: MAE −33.8%, |bias| −88.5%.  
V2 also beats Baseline A on the 2024 holdout (7.54 vs 8.58).

## Decision impact (engine unchanged; only projection input swapped)

### Development (2015+2018+2021)

| | V1 | V2 |
|---|---:|---:|
| Accuracy | 51.1% | **52.8%** |
| Avg decision value | +0.64 | **+0.90** |
| Total decision value | +174.9 | **+223.6** |
| Changed decisions | 220 (improved 10 / worsened 14 / unchanged outcome 196) |

### Holdout 2024

| | V1 | V2 |
|---|---:|---:|
| Accuracy | 44.8% | **50.0%** |
| Avg decision value | −1.67 | **−1.11** |
| Total decision value | −175.6 | **−104.0** |
| Changed decisions | 75 (improved 8 / worsened 7 / unchanged outcome 60) |

Decision value improved by **+71.6** on holdout (within success criterion #5).

## Conclusion

**Verdict: IMPROVEMENT**

V2 meets all pre-registered criteria on development LOOCV and the official 2024 holdout. Systematic over-projection is largely removed; holdout MAE beats both V1 and Baseline A. Start/Sit accuracy and decision value improve without changing decision logic or confidence.

### Architecture status

- V1 and V2 **coexist**.  
- Default primary remains **V1** so the frozen 2018 benchmark stays locked.  
- V2 is selected via `HistoricalProjectionExperimentState.PrimaryMode = ProjectionV2`.  
- Do **not** retune V2 against 2024.  

### What this does *not* claim

- Confidence is still uncalibrated (Experiment 2).  
- Decision engine logic is unchanged.  
- News/ownership gaps remain.  
- Negative absolute decision value on 2024 means the system is better than V1 but still not strongly profitable in this lab-roster setup.

## How to reproduce

```csharp
await HistoricalReplayCommands.RunProjectionCalibrationExperimentAsync(services);
```

## Next

BASELINE → HYPOTHESIS → DEVELOPMENT → FREEZE → HOLDOUT → EVALUATE → **ACCEPT V2**  

Next controlled experiment should address **confidence calibration**, still without contaminating the 2024 holdout during fitting.
