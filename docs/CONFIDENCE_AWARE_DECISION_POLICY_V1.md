# Experiment 3 — Confidence-Aware Decision Policy V1

## Hypothesis

Calibrated confidence can improve actual fantasy decision quality by acting as a trust signal — suppressing low-trust marginal recommendations without gaming abstention.

## Control policy

**Projection V2 + Calibrated Confidence V2 + existing DecisionEngine Start/Sit rules (policy Off)**

- Projection V2 frozen (`PiecewiseScaleAt20`: 0.924 / 0.6005) — **unchanged**
- Confidence V2 frozen (`[0,15,25,35] → [57,67,65,42]`) — **unchanged**
- Decision ranking / DeriveRecommendation — **unchanged**
- Calibrated confidence remains attached but does not alter control recommendations

## Experimental policy

Separate post-ranking layer (`IConfidenceAwareDecisionPolicy`), switchable ON/OFF via `ConfidenceAwareDecisionPolicyState`.

### Frozen policy

```
Kind:      SuppressStartAndSit
Threshold: calibratedConfidence <= 45
Margin:    DecisionValue margin vs next alternative < 6.0
Trust:     HIGH TRUST if calibratedConfidence >= 60, else LOW TRUST
```

Rule: IF recommendation is Start or Sit AND calibratedConfidence ≤ 45 AND DecisionValue margin &lt; 6.0 THEN suppress (abstain from emitting that StartSit recommendation). Otherwise keep and label trust.

## Development validation

Leave-one-season-out on **2015 / 2018 / 2021** under Projection V2 + Confidence V2.

Candidate grid (bounded):

- Kinds: `SuppressStart`, `SuppressStartAndSit`, `SwapStart`
- Thresholds: 40, 45, 50, 55, 60, 65
- Margins: 3, 6

**2024 was not used during fitting.**

Selected: `SuppressStartAndSit@t45-m6`

| Metric | Value |
|---|---:|
| Mean LOOCV validation Δ total decision value | **+41.03** |
| Mean validation retention | **53.3%** |
| Pooled development Δ (offline) | **+123.10** |

Tied with `t50-m6` / `t55-m6` on mean validation Δ; lowest threshold retained.

## Success criteria (pre-registered before 2024 holdout)

1. Development LOOCV mean total decision value improves by ≥15 vs control  
2. Official 2024 total decision value improves by ≥20 vs control  
3. Holdout retains ≥50% of control graded decisions  
4. Projection V2 and Confidence V2 unchanged  

Tiny numerical fluctuations are **not** improvement.

## 2024 holdout

*(Filled after official single-pass evaluation.)*

## Decision-value comparison

*(Filled after official single-pass evaluation.)*

## Failure analysis

*(Filled after official single-pass evaluation.)*

## Verdict

*(Filled after official single-pass evaluation.)*

## What we learned

*(Filled after official single-pass evaluation.)*

## Architecture

```
Historical Data
→ Feature Reconstruction
→ Projection V2
→ Knowledge
→ Decision Candidate
→ Calibrated Confidence
→ Confidence-Aware Decision Policy   ← this layer (ON/OFF)
→ Final Recommendation
→ Outcome
→ Evaluation
```

Policy is not buried inside the projection model. Control and experiment are reproducible by toggling `ConfidenceAwareDecisionPolicyState.Mode`.
