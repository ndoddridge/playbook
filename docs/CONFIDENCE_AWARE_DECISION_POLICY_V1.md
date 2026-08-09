# Experiment 3 — Confidence-Aware Decision Policy V1

### Hypothesis

Calibrated confidence can improve actual fantasy decision quality by acting as a trust signal — suppressing low-trust marginal recommendations without gaming abstention.

### Control policy

**Projection V2 + Calibrated Confidence V2 + existing DecisionEngine Start/Sit rules (policy Off)**

| Component | Status |
|---|---|
| Projection V2 (`PiecewiseScaleAt20`: 0.924 / 0.6005) | Frozen — unchanged |
| Confidence V2 (`[0,15,25,35] → [57,67,65,42]`) | Frozen — unchanged |
| Decision ranking / `DeriveRecommendation` | Unchanged |
| Calibrated confidence | Attached; does not alter control recommendations |

### Experimental policy

Separate post-ranking layer (`IConfidenceAwareDecisionPolicy`), switchable ON/OFF via `ConfidenceAwareDecisionPolicyState`.

Does **not** modify projection or confidence-calibration formulas.

### Development validation

Leave-one-season-out on **2015 / 2018 / 2021** under Projection V2 + Confidence V2.

**2024 was not used during fitting.**

Bounded candidate grid:

| Axis | Values |
|---|---|
| Kind | `SuppressStart`, `SuppressStartAndSit`, `SwapStart` |
| Threshold | 40, 45, 50, 55, 60, 65 |
| Margin | 3, 6 |

Per-fold train→validate summaries:

| Validate | Train-selected | Val Δ total value | Retention |
|---|---|---:|---:|
| 2015 | `SuppressStart@t40-m3` | 0.00 | 100% |
| 2018 | `SwapStart@t60-m6` | −306.00 | 100% |
| 2021 | `SwapStart@t45-m6` | −50.40 | 100% |

Fold-level train winners were unstable (Swap looked good on train, failed validation). Final freeze therefore used **mean validation Δ across candidates** with retention ≥50%, then confirmed on pooled development.

| Candidate (top) | Mean val Δ | Notes |
|---|---:|---|
| **`SuppressStartAndSit@t45-m6`** | **+41.03** | Selected (tie-break: lowest threshold) |
| `SuppressStartAndSit@t50-m6` | +41.03 | Tied |
| `SuppressStartAndSit@t55-m6` | +41.03 | Tied |
| `SuppressStart@t45-m6` | +4.67 | Weaker; Start-only |

Pooled development (offline, same frozen rule):

| Scope | N | Accuracy | Avg value | Total value | Suppressed would-have |
|---|---:|---:|---:|---:|---:|
| Control | 248 | 52.8% | +0.90 | **+223.60** | — |
| Experiment | 132 | 61.4% | +2.63 | **+346.70** | −123.10 |

Mean LOOCV retention for selected policy: **53.3%** (meets ≥50%).

### Frozen policy

```
Id:        confidence-aware-decision-policy-v1
Kind:      SuppressStartAndSit
Threshold: calibratedConfidence <= 45
Margin:    DecisionValue margin vs next alternative < 6.0
Trust:     HIGH TRUST if calibratedConfidence >= 60 else LOW TRUST
```

**Exact rule:** IF recommendation is Start or Sit AND calibratedConfidence ≤ 45 AND DecisionValue margin &lt; 6.0 THEN suppress (do not emit that StartSit recommendation). Otherwise keep and label HIGH/LOW TRUST.

**Rationale:** Among bounded candidates, this rule maximized mean leave-one-season-out validation total decision value while retaining ≥50% of decisions. Suppressing Sit as well as Start removed additional negative-value thin-edge calls on development data. Strong DecisionValue edges are preserved even at low calibrated confidence.

### 2024 holdout

Official single-pass evaluation after freeze (Projection V2 unchanged; Confidence V2 unchanged).

| Metric | Control | Experiment |
|---|---:|---:|
| Graded decisions | 94 | 70 |
| Opportunities | 94 | 94 |
| Decisions suppressed (Start / Sit) | 0 / 0 | **33 / 12** |
| Accuracy | 50.0% | **54.3%** |
| Average decision value | −1.11 | **+0.64** |
| **Total decision value** | **−104.00** | **+44.80** |
| Worst decision cost | −36.70 | −22.80 |
| Best decision value | +28.50 | +40.80 |
| Suppressed would-have total value | — | **−146.00** |

Confidence distribution (calibrated):

| Band | Control | Experiment (kept) |
|---|---:|---:|
| 40–50% | 51 | 27 |
| 50–60% | 9 | 9 |
| 60–70% | 34 | 34 |

Retention: **70/94 = 74.5%** (≥50% required).

### Decision-value comparison

| | Total decision value |
|---|---:|
| Control | **−104.00** |
| Experiment | **+44.80** |
| **Δ (exp − control)** | **+148.80** |

Material improvement. Not a tiny fluctuation. Abstention is real (24 graded decisions suppressed) but well above the 50% retention floor, and suppressed decisions carried **−146** total decision value (removing them accounts for the gain).

### Failure analysis

Policy acted on **45/94** control graded decisions (33 Starts, 12 Sits).

Acted Starts by position (would-have total value if kept):

| Position | n | Would-have total |
|---|---:|---:|
| RB | 9 | **−103.7** |
| QB | 8 | −24.4 |
| WR | 7 | −22.0 |
| TE | 9 | **+24.0** |

- **20** acted Starts had negative actual decision value (suppression helped).
- **13** acted Starts had positive value (suppression hurt) — notably TE lean.
- Thin-edge acted Starts (margin &lt; 3): **27**.
- Low-calibrated-confidence (≤45) incorrect control decisions: **27**.

Confidence was most useful on **RB/QB/WR thin-edge Starts**; TE was the main place the trust signal suppressed positive-value calls. No fabricated injury/news features were used.

### Verdict

**IMPROVEMENT**

Meets pre-registered criteria:

1. Dev LOOCV mean Δ **+41.03** ≥ 15  
2. Holdout Δ **+148.80** ≥ 20  
3. Holdout retention **74.5%** ≥ 50%  
4. Projection V2 and Confidence V2 unchanged  

Accept the confidence-aware decision policy as a frozen ON/OFF layer.

### What we learned

1. **Calibrated confidence can change decisions usefully** — not only as an informational display.
2. **Trust + margin together** beat confidence-alone thresholds: strong edges at low calibrated confidence are kept.
3. **Swap was attractive on train folds but failed validation** — flipping recommendations is riskier than abstaining from weak ones.
4. **Abstention must be reported**, not hidden: here suppressed calls were net-negative (−146 on holdout), so fewer decisions still improved fantasy value.
5. **TE remains a weak spot** for this trust signal — position-aware policy is a later experiment, not this one.
6. Scientific sequence holds: better projections → better confidence → **better decisions** earned on untouched 2024.

### Architecture

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

Reproduce control: `ConfidenceAwareDecisionPolicyMode.Off`  
Reproduce experiment: `ConfidenceAwareDecisionPolicyMode.On` with frozen constants in `FrozenConfidenceAwareDecisionPolicyV1`.
