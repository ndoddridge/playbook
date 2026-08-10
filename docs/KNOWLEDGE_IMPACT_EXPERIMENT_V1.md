# Knowledge Impact Experiment V1

### Hypothesis

Specific, explainable knowledge groups (recent form, usage/opportunity, role/health) can improve Start/Sit decision quality on unseen historical data when applied through explicit bounded transforms — without changing Projection V2 or Confidence V2.

### Available knowledge signals

| Group | Signals | Used by engine via | Historical coverage |
|---|---|---|---|
| RecentForm | `RecentProductionScore` | Bounded OpportunityScore Δ (±6) | High when prior REG games exist |
| Usage | `UsageScore` + `OpportunityScore` | Direct AssessValues terms | High |
| RoleHealth | Role note + Health/Injury | Health DV adj + Role Opportunity Δ (±4) | Role high; injury sparse |
| Matchup | Team/matchup aspects | — | **Unavailable** — not experimented |

Missing data is never treated as negative evidence (explicit unavailable / missing lists only).

### Data coverage

- nflverse: news unavailable; target/snap share often unavailable
- `OpponentTeam` null historically
- Weather / rest / home-away / game script: unavailable markers only
- Quick Picks: **no** historical prop-line archive or settled-stat grader in-repo

### Control

`KnowledgeMode.Baseline` under Projection V2 + Confidence V2 + Decision Policy Off:

- Projection retained
- Usage / opportunity / recent production / role / health knowledge stripped
- AssessValues sees projection + default missing penalties

Default runtime mode remains `Passthrough` (identity) so frozen Projection/Confidence/Policy benchmarks stay reproducible outside experiments.

### Experiment A — RecentForm

If recent production ≥ 65 → OpportunityScore +6; if ≤ 35 → −6.

### Experiment B — Usage

Restore OpportunityScore + UsageScore into AssessValues inputs.

### Experiment C — RoleHealth

Restore Health signals; RoleNote heuristics adjust OpportunityScore ±4.

### Experiment D — Matchup

**Not run** — insufficient historical coverage (would require fabricating opponent/team context).

### Development results

Leave-one-season-out on **2015 / 2018 / 2021** (2024 unused during fitting):

| Group | Mean val Δ total decision value | Dev classification |
|---|---:|---|
| RecentForm | **+0.00** | NEUTRAL |
| Usage | **+38.73** | POSITIVE (dev) |
| RoleHealth | **−10.00** | NEGATIVE (dev) |

Usage fold detail: 2015 **−35.9** / 2018 **+158.5** / 2021 **−6.4** (driven by 2018).

Pooled development (Baseline vs frozen Enhanced=Usage):

| Scope | N | Accuracy | Total decision value | Changed vs baseline |
|---|---:|---:|---:|---:|
| Baseline | 222 | 49.5% | +152.40 | — |
| Enhanced (Usage) | 241 | 53.1% | **+268.60** | 76.1% |

Projection MAE/bias unchanged (7.50 / +0.55) — transforms do not touch Projection V2.

### Frozen configurations

```
FrozenEnhancedGroups = Usage
RecentFormOpportunityDelta = 6 (thresholds 65 / 35)
RoleOpportunityDelta = 4
```

Projection V2, Confidence V2, Decision Policy V1 **unchanged**.

### 2024 holdout results

Official single-pass after freeze:

| Metric | Baseline | Enhanced (Usage) |
|---|---:|---:|
| Graded decisions | 77 | 93 |
| Accuracy | 46.8% | 49.5% |
| Average decision value | −0.69 | −1.21 |
| **Total decision value** | **−52.80** | **−112.10** |
| Worst decision cost | −27.30 | −36.70 |
| Decisions changed | — | **78 (101.3%)** |
| Projection MAE | 7.54 | 7.54 |
| Projection bias | +0.91 | +0.91 |

**Δ total decision value (Enhanced − Baseline): −59.30**

### Ablation results

| Group | Dev mean Δ | Holdout (if run) | Classification |
|---|---:|---|---|
| RecentForm | 0.00 | — | NEUTRAL |
| Usage | +38.73 | tot −112.10 vs base −52.80 | **NEGATIVE on holdout** |
| RoleHealth | −10.00 | — | NEGATIVE (dev) |
| Matchup | — | — | Not tested (no data) |

### Start/Sit impact

Primary evaluation path. Usage changed a large share of recommendations and improved development totals (especially 2018) but **worsened** untouched 2024 decision value by −59.3 despite a small accuracy bump (46.8% → 49.5%). Accuracy-alone would have been misleading.

### Quick Picks impact

Live Enhanced path can apply bounded OpportunityScore deltas from shared knowledge.  
**Historical Quick Picks evaluation is not available** (missing archived prop/closing lines and settled counting-stat outcomes at cutoff).  
**No Quick Picks predictive improvement is claimed.**

### Failure analysis

- Holdout Enhanced made **more** graded decisions (93 vs 77) with worse average and total decision value.
- Development gain was concentrated in **2018** (+158.5); 2015 and 2021 were negative for Usage — unstable LOOCV signal.
- Projection metrics identical → damage is decision-selection / ranking, not projection calibration.
- RoleHealth already looked harmful on development; correctly excluded from freeze.
- RecentForm produced no material decision changes under current thresholds (mid-range production → no ±6 trigger when opportunity already defaults to 50).

### Verdict

**REGRESSION**

Usage looked promising on development mean LOOCV (+38.7) but failed the official 2024 holdout (Δ **−59.3** total decision value, change rate 101%). Reject enabling Knowledge Enhanced (Usage) for production.

Keep default `KnowledgeMode.Passthrough`. Retain the Baseline/Enhanced machinery for future ablations.

### Knowledge signals worth keeping

- **Infrastructure**: Baseline/Enhanced applicator, ablation runner, shared PredictionContext wiring — keep.
- **No knowledge group earned production enablement** from this holdout.

### Knowledge signals to reject

| Signal group | Reason |
|---|---|
| Usage (as Enhanced transform) | Holdout regression (−59.3 total decision value) |
| RoleHealth | Negative development LOOCV (−10.0) |
| RecentForm (current thresholds) | Neutral / no material effect |
| Matchup | Insufficient historical data |

### Data limitations

- No historical matchup/team environment features
- Injury designations sparse
- Quick Picks cannot be graded historically yet
- Usage LOOCV was unstable across development seasons

### Next experiment

1. Diagnose why Usage helps 2018 but hurts 2024 (position mix, margin regimes, opportunity scale)
2. Consider narrower Usage transforms (e.g. only when projection margin is thin)
3. Build historical Quick Picks grading before claiming QP knowledge value
4. Only after a knowledge group earns holdout improvement: consider it as a future ML feature
