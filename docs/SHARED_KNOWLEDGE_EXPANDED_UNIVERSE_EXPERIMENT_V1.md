# Shared Knowledge × Expanded Universe Experiment V1

**ExperimentId:** `shared-knowledge-expanded-universe-v1`  
**Date:** 2026-08-10  
**Status:** Complete — **REGRESSION / rejected as improvement signal**

## Question

Does assembled shared knowledge (production `Passthrough`) improve prediction/decision quality
vs stripped `Baseline` when evaluation uses `ExpandedSkillUniverse`?

## Protocol

- Control: `KnowledgeMode.Baseline`, ActiveGroups=`None`
- Treatment: `KnowledgeMode.Passthrough`, ActiveGroups=`None`
- Universe: `ExpandedSkillUniverse`
- Dev seasons: 2015/2018/2021 (informational folds; **no parameter selection**)
- Holdout: 2024 once
- Rejected transforms stay off (Usage / RoleHealth / RecentForm / ThinMargin / DataSufficiencyTrust)
- Projection V2 / Confidence V2 / Decision Policy V1 unchanged
- Frozen 2018 LabRoster benchmark path untouched

## Development (informational)

| Season | Baseline tot | Passthrough tot | Δ |
|---|---:|---:|---:|
| 2015 | −29.00 | −37.90 | −8.90 |
| 2018 | 224.70 | 400.00 | +175.30 |
| 2021 | 43.20 | −0.10 | −43.30 |
| **Mean Δ** | — | — | **+41.03** |

Dev graded: Baseline n=234 acc=50.4% tot=238.90 → Passthrough n=266 acc=55.6% tot=362.00 (changed 94%).  
Candidates: 23072. Usable shared knowledge: **94.2%**.

## 2024 holdout (once)

### Start/Sit

| Metric | Baseline | Passthrough | Δ |
|---|---:|---:|---:|
| Candidates | 7508 | 7508 | — |
| Graded | 89 | 107 | — |
| Accuracy | 57.3% | 55.1% | −2.2pp |
| Total DV | 170.60 | 93.30 | **−77.30** |
| Change rate | — | 87.6% | — |
| Proj MAE | 4.98 | 4.98 | 0 |

Usable knowledge rate: **94.5%** (7095/7508). Projection-only/unknown: 413.

### Quick Picks

| Metric | Baseline | Treatment | Δ |
|---|---:|---:|---:|
| Predictions | 9726 | 9726 | — |
| MAE | 15.178 | 15.178 | 0 |
| Top5 | 21.0% | 21.0% | 0 |
| Ranks changed | — | 0 | — |
| Knowledge attached | 0 | 9726 | — |

QP ranking identity expected (no rejected transforms; Enhanced+None applicator is no-op on ranking).

## Verdict

**REGRESSION** — holdout Δ −77.30 with 87.6% change rate.  
Do **not** treat Passthrough shared knowledge as an improvement vs Baseline on ExpandedSkillUniverse.  
Unstable across development folds (helps 2018, hurts 2015/2021).  
Production remains `KnowledgeMode.Passthrough` (status quo; no new enablement).  
Rejected Enhanced transforms remain disabled.

## Production / next steps

- Production unchanged.
- Not ready to claim shared-knowledge value on the expanded surface.
- Do **not** start the 20-season simulation yet based on this result.
- Full machine report: `SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_V1_REPORT.txt`.
