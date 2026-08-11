# Position-Segmented Projection Calibration V1

**ExperimentId:** `position-segmented-calibration-v1`
**Date:** 2026-08-11
**Status:** Complete — **REJECTED** (development gate failed; 2024 holdout not run)

## Hypothesis

Different positions have different volume/scoring distributions that one global piecewise
calibration curve cannot capture. Fitting separate piecewise calibration parameters per position
group should reduce projection error and/or improve Start/Sit decision value beyond the single
frozen global Projection V2 curve (`FrozenProjectionCalibrationV2`: `x<20 → 0.9240x`,
`x≥20 → 0.6005x`).

## Grouping (decided from real counts, not assumed)

Candidate positions QB/RB/WR/TE; K/DST excluded (different scoring shape — not part of this
experiment, always fall back to global Projection V2). A position only gets its own group if it
clears **25 observations in every development season**; otherwise TE was pre-planned to fold into
WR (standard "pass-catcher" grouping).

Actual per-season development counts:

| Position | 2015 | 2018 | 2021 |
|---|---:|---:|---:|
| QB | 45 | 46 | 51 |
| RB | 71 | 73 | 85 |
| WR | 85 | 94 | 102 |
| TE | 40 | 48 | 51 |

All four positions cleared the threshold in every development season, so the grouping used was
**QB / RB / WR / TE**, four separate groups — no TE→WR fold was needed in practice.

## Method

1. Collect (V1 predicted, actual) pairs from development seasons only: **2015, 2018, 2021**.
2. Split by the grouping above.
3. Per group: leave-one-season-out selection and refit, reusing `ProjectionCalibrationFitter`
   (identical candidate forms and selection rule as the accepted global Projection V2 experiment —
   no new fitting math).
4. Pre-registered development gate (`PositionSegmentedCalibrationSuccessCriteria`), decided before
   any holdout data was touched:
   - Pooled dev LOOCV MAE improves ≥2% (relative) vs global V2
   - No single group's own dev LOOCV MAE is >15% worse (relative) than global V2 on that group
   - Dev pooled Start/Sit total decision value does not degrade by more than 25 points vs global V2
5. **2024 holdout is only read if all three pass.**

## Development LOOCV — per group (pooled)

| Group | N | MAE global V2 | MAE segmented | Bias global V2 | Bias segmented |
|---|---:|---:|---:|---:|---:|
| QB | 142 | 6.70 | 6.78 | +1.95 | +0.62 |
| RB | 229 | 7.63 | 7.77 | +0.56 | +1.89 |
| WR | 281 | 8.01 | **7.90** | +0.72 | +3.67 |
| TE | 139 | 7.15 | **6.76** | −1.41 | +1.49 |
| **Pooled (all groups)** | 791 | **7.51** | **7.46** | **+0.52** | **+2.22** |

Segmented calibration only won outright on WR and TE; QB and RB were slightly worse than the
already-accepted global curve. Pooled MAE improvement was **+0.7%** — short of the ≥2% bar — and
pooled bias got noticeably worse (0.52 → 2.22), even though bias magnitude was not itself a gate
criterion.

## Development decision impact (global V2 vs segmented; Start/Sit, pooled 2015+2018+2021)

| | Global V2 | Segmented | Δ |
|---|---:|---:|---:|
| Decisions (graded) | 270 (248) | 260 (237) | — |
| Accuracy | 52.8% | 52.7% | −0.1pp |
| Avg decision value | +0.90 | +0.65 | −0.25 |
| **Total decision value** | **+223.60** | **+154.80** | **−68.80** |
| Decisions changed | — | 200 (improved 12 / worsened 14 / unchanged outcome 174) | — |

## Development gate result

```
DevJustifiesHoldout = False
Pooled dev LOOCV MAE improvement = 0.7%   (need >= 2%,  FAIL)
Worst per-group regression = RB 1.8%      (cap 15%,     ok)
Dev decision value Δ = -68.8              (floor -25,   FAIL)
```

Two of three pre-registered criteria failed: the MAE improvement was far too small to justify the
added model complexity, and pooled Start/Sit decision value degraded well beyond the tolerance —
driven mainly by QB and RB, where segmentation made the calibration slightly worse, not better.

## Verdict

**NoMaterialImprovement — REJECTED.**

Per the pre-registered stop condition, **the 2024 holdout was never read** (`Holdout used during
fitting: False`; `2024 HOLDOUT: NOT RUN`). Position-segmented calibration is not adopted. The
frozen global Projection V2 curve remains the projection calibration in use; production
`HistoricalProjectionExperimentState.PrimaryMode` default and `KnowledgeMode.Passthrough` are
unchanged.

## Why it likely failed

Splitting by position gives each group's fitter less data and one fewer effective degree of
freedom to average over noise (down from ~791 pooled observations to 139–281 per group). WR and TE
had enough of a genuinely different volume/scoring shape for the split to pay for itself; QB and RB
did not — their per-group fits landed close to the global curve's own numbers but with added
variance, netting out to a wash-to-slightly-worse pool average and a worse pooled decision value.

## Frozen / rejected inventory

| Item | Status |
|---|---|
| Projection V2 / Confidence V2 / Decision Policy V1 | Unchanged (frozen) |
| Usage / RoleHealth / RecentForm / RecentFormThinMargin / DataSufficiencyTrust | Still REJECTED |
| **Position-Segmented Projection Calibration V1** | **REJECTED** (this experiment) |

## How to reproduce

```bash
dotnet run --project src/Playbook.Research -- experiment --id position-segmented-calibration-v1
```

## Next

Do **not** retune the grouping or thresholds against these development results and re-run — that
would be a holdout-adjacent form of overfitting on the same fixed development seasons. A future
attempt would need a materially different hypothesis (e.g. segmenting on something other than raw
position, or targeting only the groups — WR/TE — where the split actually helped) evaluated fresh.
