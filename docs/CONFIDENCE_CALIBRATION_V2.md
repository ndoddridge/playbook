# Experiment 2 — Confidence Calibration V2

## Hypothesis

Raw confidence is poorly calibrated and can be converted into a more meaningful probability-like signal using historical development outcomes.

## Baseline

**Projection V2 + Raw Confidence**

- Projection V2 frozen (`PiecewiseScaleAt20`: 0.924 / 0.6005) — **unchanged**
- Decision rules / ranking / thresholds — **unchanged**
- Raw confidence = existing evidence-quality score from `DecisionEngine.ComputeDecisionConfidence`

## Success criteria (pre-registered before 2024 holdout)

1. Development LOOCV ECE improves ≥15% vs raw  
2. Official 2024 ECE improves ≥10% vs raw  
3. Official 2024 useful ordering: upper-half calibrated confidence success exceeds lower-half by ≥5pp  
4. Recommendations / decision values unchanged (informational calibration only)

## Method

1. Run development seasons **2015 / 2018 / 2021** under **Projection V2**.  
2. Collect graded Start/Sit decisions `(rawConfidence, wasCorrect)`.  
3. **Never read 2024 during fitting.**  
4. Leave-one-season-out over candidate raw-confidence grids.  
5. Select grid with lowest LOOCV ECE.  
6. Map each bin → empirical success rate (reliability mapping).  
7. Refit on all development graded decisions and **freeze**.  
8. ONE official 2024 holdout evaluation.

### Frozen mapping

```
Raw confidence bins: [0, 15, 25, 35]
Calibrated rates %:  [57, 67, 65, 42]
```

Interpretation: calibrated confidence is an estimated success probability, not a rescaled evidence-quality score. Higher calibrated values are intended to mean higher expected success — even when that inverts the raw-confidence order (raw confidence was anti-correlated with success in development).

Confidence remap is attached as `CalibratedConfidence` and does **not** feed recommendation selection.

## Development validation (LOOCV)

| Signal | N | ECE | Brier | Ordering gap |
|---|---:|---:|---:|---:|
| Raw | 248 | 0.24 | 0.33 | **−20.2pp** |
| Calibrated | 248 | **0.08** | **0.25** | **+21.8pp** |

Raw development buckets:

| Bucket | n | Success | Avg conf | Avg decision value |
|---|---:|---:|---:|---:|
| 0–20% | 61 | 60.7% | 13.8 | +2.40 |
| 20–40% | 187 | 50.3% | 33.2 | +0.41 |
| 40–100% | 0 | — | — | — |

## Official holdout — 2024 (single evaluation)

| Signal | N | ECE | Brier | Ordering gap |
|---|---:|---:|---:|---:|
| Raw | 94 | 0.20 | 0.31 | **−10.6pp** |
| Calibrated | 94 | **0.16** | **0.26** | **+6.4pp** |

Relative ECE improvement on holdout: **20.1%** (meets ≥10% criterion).

### Holdout calibration table (calibrated confidence)

| Bucket | n | Avg calibrated conf | Actual success | Avg decision value |
|---|---:|---:|---:|---:|
| 40–60% | 60 | 44.2 | 55.0% | −0.82 |
| 60–80% | 34 | 65.8 | 41.2% | −1.61 |

### Decision impact

Recommendations were **not** changed by confidence remap:

| Metric | Value |
|---|---:|
| Accuracy | 50.0% |
| Avg decision value | −1.11 |
| Total decision value | −104.0 |
| Worst / best | −36.7 / +28.5 |
| Recommendations affected by confidence thresholds | **0** |

## Conclusion

**Verdict: IMPROVEMENT**

Pre-registered criteria met:

- Dev ECE −66.0%  
- Holdout ECE −20.1%  
- Holdout ordering gap +6.4pp  
- Projection V2 untouched; recommendations unchanged  

### Honest caveats

1. Raw confidence remains anti-correlated with success (low raw often does better). Calibration largely **relabels** that pattern into a probability-like score.  
2. Holdout standard buckets are not perfectly monotonic (60–80% underperformed 40–60%). Median-split ordering still cleared +5pp, but this is not a polished calibration curve.  
3. Almost all mass remains mid-band; the system still rarely expresses very high confidence.  
4. Because confidence does not yet gate recommendations, decision accuracy/value are unchanged by this experiment — the gain is **informational**.

### Architecture status

- `DecisionResult.CalibratedConfidence` / grade field always populated  
- `FrozenDecisionConfidenceCalibrationV2.Apply(raw)` is the frozen mapper  
- Default projection mode remains V1 for the locked 2018 benchmark  
- Experiment entry: `HistoricalReplayCommands.RunConfidenceCalibrationExperimentAsync(services)`

## Next

PROJECTION ✓ → **CONFIDENCE ✓ (informational)** → DECISION QUALITY → CONTEXTUAL INTELLIGENCE → MULTI-YEAR → eventually ML

Next controlled experiment should use calibrated confidence (or a related trust signal) inside **decision presentation / thresholds**, still without contaminating the 2024 holdout during fitting.
