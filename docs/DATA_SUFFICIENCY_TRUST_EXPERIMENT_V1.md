# Data Sufficiency Trust Gate Experiment V1

**ExperimentId:** `data-sufficiency-trust-experiment-v1`  
**Date:** 2026-08-10  
**Status:** Complete — **NEUTRAL / DISABLED**

## Formulation chosen

**Option 3 — trust/confidence only** (simplest justified by architecture):

- Adjust `KnowledgeConfidence` (Start/Sit) / `Prediction.Confidence` (Quick Picks)
- Do **not** gate or re-enable Opportunity transforms (RecentForm / Usage / RoleHealth stay off)
- Do **not** change ProjectedValue / RankingScore

## Sufficiency rule (frozen, cutoff-safe)

From `HistoricalFeatureReconstructor` prior REG games only (never target week):

| Label | Prior REG games at cutoff |
|---|---|
| Sufficient | ≥ 3 |
| Limited | 1–2 |
| Insufficient | 0 |

Exposed on knowledge as fact `projection.data_sufficiency`.

## Trust gate (Enhanced `DataSufficiencyTrust`)

Starts from Baseline (contextual Opportunity groups stripped). Then:

| Sufficiency | KnowledgeConfidence delta |
|---|---|
| Sufficient | 0 |
| Limited | −`SelectedLimitedPenalty` |
| Insufficient | −(`SelectedLimitedPenalty` + 8) |

Clamp `[12, 95]`.

## Development selection

Candidates for `SelectedLimitedPenalty`: `{8, 12, 16}` (Insufficient = Limited + 8).

| Penalty | 2015 Δ | 2018 Δ | 2021 Δ | Mean Δ |
|---:|---:|---:|---:|---:|
| 8 | 0.00 | 0.00 | 0.00 | **0.00** |
| 12 | 0.00 | 0.00 | 0.00 | 0.00 |
| 16 | 0.00 | 0.00 | 0.00 | 0.00 |

**Frozen:** `SelectedLimitedPenalty = 8` (Insufficient = 16). Tie → lowest candidate.

Dev Enhanced vs Baseline: n=222, changed=0, tot=152.40 identical.

## 2024 holdout (once)

| Metric | Baseline | Enhanced | Δ |
|---|---:|---:|---:|
| Graded decisions | 77 | 77 | — |
| Accuracy | 46.8% | 46.8% | 0 |
| Total decision value | −52.80 | −52.80 | **0.00** |
| Change rate | — | 0% | — |
| Projection MAE | 7.54 | 7.54 | 0 |

## Quick Picks

| Scope | MAE | Top-5 | Ranks changed | Identical? |
|---|---:|---:|---:|---|
| Dev | 24.243 → 24.243 | unchanged | 0 | Yes |
| Holdout 2024 | 22.452 → 22.452 | 75.6% → 75.6% | 0 | Yes |

Expected: ranking identity (Confidence-only gate).

## Verdict

**NEUTRAL** — Recommendation: **DISABLED / rejected for enablement**

Confidence penalties on Limited/Insufficient players produced no Start/Sit recommendation flips under the lab roster + Baseline control (intel term is small vs projection/opportunity defaults). Production remains `KnowledgeMode.Passthrough`.

## Inventory

| Item | Status |
|---|---|
| Projection V2 / Confidence V2 / Decision Policy V1 | Unchanged |
| Usage / RoleHealth / RecentForm / RecentFormThinMargin | Still rejected/disabled |
| DataSufficiencyTrust | **REJECTED / DISABLED** |
