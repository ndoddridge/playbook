# RecentForm Thin-Margin Experiment V1

**ExperimentId:** `recent-form-thin-margin-experiment-v1`  
**Date:** 2026-08-10  
**Status:** Complete — **NEUTRAL / DISABLED**

## Hypothesis

Ungated RecentForm was NEUTRAL on Start/Sit and Quick Picks. Gating the same RecentForm Opportunity deltas to thin comparative margins (`ComparativeMargin < 3`) should concentrate form influence where peers are close and improve Start/Sit decision value on unseen 2024 data.

## One variable

`KnowledgeImpactGroup.RecentFormThinMargin` at the shared `IKnowledgeImpactApplicator` layer.

- Same RecentForm thresholds (65/35) and deltas (Start/Sit ±6; QP ±0.6)
- Gate: nearest same-position (Start/Sit) or same-market (QP) projection gap `< 3.0`
- Pre-registered from existing weak-margin bucket; not fit on 2024
- Usage / RoleHealth remain off

## Development (informational)

| Season | Baseline tot | Enhanced tot | Δ |
|---:|---:|---:|---:|
| 2015 | 43.40 | 48.80 | +5.40 |
| 2018 | 179.60 | 179.60 | 0.00 |
| 2021 | −70.60 | −101.80 | −31.20 |
| **Mean Δ** | — | — | **−8.60** |

Dev change rate 2.3% (5 decisions). Deterministic on repeat.

## 2024 holdout (official, once)

| Metric | Baseline | Enhanced | Δ |
|---|---:|---:|---:|
| Graded decisions | 77 | 78 | — |
| Accuracy | 46.8% | 46.2% | −0.6pp |
| Avg decision value | −0.69 | −0.68 | +0.01 |
| **Total decision value** | **−52.80** | **−53.00** | **−0.20** |
| Decisions changed | — | 1 (1.3%) | — |
| Projection MAE | 7.54 | 7.54 | 0 |

Success criteria require holdout Δ ≥ +20 and change rate ≥ 5%. Neither met.

## Verdict

**NEUTRAL (NoMaterialImprovement)** — Recommendation: **DISABLED**

Reject enabling RecentFormThinMargin. Production remains `KnowledgeMode.Passthrough`.

## Frozen / rejected inventory

| Item | Status |
|---|---|
| Projection V2 / Confidence V2 / Decision Policy V1 | Unchanged (frozen) |
| Usage | Still REJECTED |
| RoleHealth | Still REJECTED |
| RecentForm (ungated) | Still NEUTRAL / DISABLED |
| RecentFormThinMargin | **REJECTED / DISABLED** (this experiment) |

## Next recommended controlled experiment

Do **not** further retune RecentForm gates/thresholds against 2024.

Next highest-value candidate with historical coverage that is not already rejected:

**DataSufficiency / Limited-history trust gate on knowledge transforms** — or expand beyond OpportunityScore deltas to a shared knowledge signal that can alter decision inputs when history is Limited (player-local, cutoff-safe, cross-type), evaluated once on Start/Sit holdout criteria.

Alternatively, diagnose Usage’s 2018+/2024− instability only as an analysis note — do **not** re-enable Usage without a qualitatively different, pre-registered hypothesis that is not a holdout retune.
