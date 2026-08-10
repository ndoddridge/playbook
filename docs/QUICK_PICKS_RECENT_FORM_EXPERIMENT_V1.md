# Quick Picks RecentForm Experiment V1

**ExperimentId:** `quick-picks-recent-form-experiment-v1`  
**Date:** 2026-08-10  
**Status:** Complete — **NEUTRAL / DISABLED**

---

### Hypothesis

RecentForm was **NEUTRAL** on Start/Sit (+0.0 mean Δ total decision value) and was **not rejected**.

Hypothesis: the same shared RecentForm signal can improve Quick Picks counting-stat ranking quality (lower MAE and/or higher Top-5 hit rate) on unseen 2024 data.

Single experimental variable: **Quick Picks Baseline vs Quick Picks + RecentForm**.

---

### Existing RecentForm definition

Unchanged from Knowledge Impact Experiment V1 / shared knowledge model:

| Parameter | Value | Source |
|---|---:|---|
| Evidence aspect | `KnowledgeAspect.RecentProduction` | SharedKnowledgeBundle |
| Score source | `RecentProductionScore` (0–100) | HistoricalKnowledgeFactory ← prior REG weeks |
| High threshold | **65** | `FrozenKnowledgeImpactExperimentV1.RecentFormHighThreshold` |
| Low threshold | **35** | `FrozenKnowledgeImpactExperimentV1.RecentFormLowThreshold` |
| QP OpportunityScore delta | **±0.6** | `KnowledgeImpactApplicator.ApplyToQuickPickPrediction` |

Not retuned. Not optimized against 2024. No second RecentForm implementation.

---

### Integration point

```
Historical Sources
       ↓
Shared Knowledge Model  (RecentProduction evidence)
       ↓
PredictionContext
       ↓
IKnowledgeImpactApplicator.ApplyToQuickPickPrediction
       ↓
Quick Picks historical RankingScore
```

**Mapping (documented, smallest bridge):**

1. Enhanced builds `PredictionContext` via `ISharedKnowledgeModel.BuildHistoricalPredictionContext(..., PredictionType.QuickPick)`.
2. Bridge `Prediction.OpportunityScore = 50` (neutral base — avoids Clamp(yards,0,100) saturation).
3. Applicator applies existing RecentForm ±0.6 when `ActiveGroups` includes RecentForm.
4. `RankingScore = ProjectedValue + (AdjustedOpportunity − 50)`.

Quick Picks does **not** own RecentForm logic. Usage / RoleHealth remain off.

Production default stays `KnowledgeMode.Passthrough`.

---

### Development baseline

| Season | Predictions | MAE | Top-5 | Tot. value |
|---:|---:|---:|---:|---:|
| 2015 | 508 | 22.521 | 82.2% | −11440.5 |
| 2018 | 549 | 26.727 | 69.1% | −14673.0 |
| 2021 | 612 | 23.444 | 79.1% | −14347.5 |
| **Agg** | **1669** | **24.230** | **76.8%** | **−40461.0** |

Determinism: development Baseline and Enhanced seasons re-run identically before holdout.

---

### Development experiment

Enhanced groups = `RecentForm` only.

| Metric | Baseline | Enhanced | Δ |
|---|---:|---:|---:|
| Predictions | 1669 | 1669 | 0 |
| MAE | 24.230 | 24.230 | 0.000 |
| Top-5 | 76.8% | 76.8% | 0.0pp |
| Tot. value | −40461.0 | −40461.0 | 0 |
| Score changed | — | 1668 (99.94%) | — |
| **Ranks changed** | — | **2 (0.12%)** | — |
| HELPED / HURT / NEUTRAL | — | 1 / 1 / 1667 | — |

MAE is invariant because RecentForm adjusts **RankingScore**, not `ProjectedValue`. Nearly every score moved by ±0.6, but almost no ranks flipped on development (uniform lab-roster form shifts).

---

### 2024 holdout

Exactly one official holdout after freeze. 2024 did not influence thresholds, deltas, or policy.

| Metric | Baseline | Enhanced | Δ |
|---|---:|---:|---:|
| Predictions | 607 | 607 | 0 |
| MAE | 22.452 | 22.452 | **0.000** |
| Top-5 | 75.6% | 75.9% | **+0.3pp** |
| Top-10 | 90.2% | 90.2% | 0 |
| Rank MAE | 2.547 | 2.547 | 0 |
| Tot. value | −13628.2 | −13628.2 | 0 |
| Score changed | — | 593 (97.69%) | — |
| **Ranks changed** | — | **29 (4.78%)** | — |
| HELPED / HURT / NEUTRAL | — | **14 / 15 / 578** | — |

**Did RecentForm improve Quick Picks on unseen 2024 data?** No meaningful improvement under pre-registered rules (MAE flat; Top-5 +0.3pp &lt; 1.0pp threshold; HELPED ≈ HURT).

---

### Prediction-change analysis

| Scope | Score % changed | Rank % changed | Avg \|Δ\| | MAE | Top-5 |
|---|---:|---:|---:|---:|---:|
| Development | 99.94% | 0.12% | 0.601 | unchanged | unchanged |
| Holdout 2024 | 97.69% | 4.78% | 0.661 | unchanged | +0.3pp |

Most “changes” are ±0.6 RankingScore bumps that do not reorder the market. Material rank flips are rare and roughly balanced.

---

### Success/failure ledger

**Holdout HELPED (examples):**

| Week | Player | Market | Rank | Rank err | Form | Actual |
|---|---|---|---|---|---:|---:|
| W5 | Justin Jefferson | Receptions | 8→6 | 2→0 | 100 | 6 |
| W5 | George Kittle | Receptions | 7→5 | 5→3 | 72 | 8 |
| W5 | George Kittle | ReceivingYards | 8→7 | 1→0 | 72 | 64 |

**Holdout HURT (examples):**

| Week | Player | Market | Rank | Rank err | Form | Actual |
|---|---|---|---|---|---:|---:|
| W5 | Jake Ferguson | Receptions | 4→7 | 0→3 | 53 | 6 |
| W10 | Travis Kelce | Receptions | 5→7 | 3→5 | 64 | 8 |
| W12 | Travis Kelce | Receptions | 6→8 | 2→4 | 62 | 6 |

Many mid-range form scores (35–65) still change rank via **peer** ±0.6 shifts — another sign the transform is a weak, noisy reorderer on this lab roster.

---

### Leakage controls

- Predictions finalized before outcome attachment  
- `KnowledgeTemporalGuard` on Enhanced knowledge contexts  
- Future injury excluded on controlled fixture (Delta WR)  
- Development seasons exclude 2024  
- Evaluator / RecentForm thresholds frozen before holdout  
- Rejected Usage / RoleHealth not enabled  
- Projection V2 / Confidence V2 / Decision Policy V1 unchanged  
- Production mode restored to Passthrough after experiment  

---

### Verdict

**NEUTRAL**

Recommendation: **DISABLED**

RecentForm moves almost all RankingScores (±0.6) but rarely improves market order. Holdout MAE unchanged; Top-5 +0.3pp is below the 1.0pp improvement bar; HELPED (14) ≈ HURT (15).

Do **not** enable for production. Do **not** retune against 2024.

---

### What we learned

1. Shared knowledge can drive Quick Picks through `PredictionContext` without duplicating RecentForm logic.  
2. Cross-type transfer is not automatic: Start/Sit-neutral RecentForm is also QP-neutral under the existing ±0.6 mapping.  
3. Score-change rate ≠ rank-change rate — attribution must track ranks.  
4. Because RecentForm does not alter `ProjectedValue`, MAE cannot improve; ranking metrics are the right lens for this transform.  
5. ±0.6 is small relative to yard/reception gaps on the lab roster → weak reorder signal.

---

### Next candidate experiment

Pick **one** new hypothesis that is **not** Usage and **not** RoleHealth (both rejected on Start/Sit):

1. A narrower RecentForm variant (e.g. only when projection margin is thin) — only if justified without 2024 tuning, or  
2. A different allowed knowledge aspect with historical coverage that could change **ProjectedValue** or a stronger ranking feature, or  
3. Expand the historical candidate pool beyond the lab roster so rank competition is more realistic  

Keep the scientific loop: one change → development → freeze → one 2024 holdout → ACCEPT/REJECT.
