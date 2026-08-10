# Shared Knowledge × Expanded Universe Experiment V1

**ExperimentId:** `shared-knowledge-expanded-universe-v1`  
**Date:** 2026-08-10  
**Status:** Complete — **Regression**

## Question

Does assembled shared knowledge (production `Passthrough`) improve prediction/decision quality
vs stripped `Baseline` when evaluation uses `ExpandedSkillUniverse`?

## Protocol

- Control: `KnowledgeMode.Baseline`, ActiveGroups=None
- Treatment: `KnowledgeMode.Passthrough`, ActiveGroups=None
- Universe: `ExpandedSkillUniverse`
- Dev seasons: 2015/2018/2021 (informational folds; no parameter selection)
- Holdout: 2024 once
- Rejected transforms stay off (Usage / RoleHealth / RecentForm / ThinMargin / DataSufficiencyTrust)
- Projection V2 / Confidence V2 / Decision Policy V1 unchanged
- Frozen 2018 LabRoster benchmark path untouched

## Verdict

**Regression**

REGRESSION: holdout Δ=-77.30 (≤ −20) with change 87.6%. Do not treat Passthrough shared knowledge as an improvement signal on ExpandedSkillUniverse. Production remains Passthrough (status quo).

## Start/Sit holdout

| Metric | Baseline | Passthrough | Δ |
|---|---:|---:|---:|
| Graded | 89 | 107 | — |
| Accuracy | 57.3% | 55.1% | — |
| Total DV | 170.60 | 93.30 | -77.30 |
| Change rate | — | 87.6% | — |
| Proj MAE | 4.98 | 4.98 | — |

Candidates: 7508. Usable knowledge rate: 94.5%.

## Quick Picks holdout

| Metric | Baseline | Treatment | Δ |
|---|---:|---:|---:|
| Predictions | 9726 | 9726 | — |
| MAE | 15.178 | 15.178 | — |
| Top5 | 21.0% | 21.0% | — |
| Ranks changed | — | 0 | — |

## Production

Remains `KnowledgeMode.Passthrough`. No rejected transform re-enabled.

## Full machine report

See `SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_V1_REPORT.txt`.