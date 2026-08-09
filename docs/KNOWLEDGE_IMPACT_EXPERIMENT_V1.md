# Knowledge Impact Experiment V1

### Hypothesis

Specific, explainable knowledge groups (recent form, usage/opportunity, role/health) can improve Start/Sit decision quality on unseen historical data when applied through explicit bounded transforms — without changing Projection V2 or Confidence V2.

### Available knowledge signals

| Group | Signals | Historical coverage |
|---|---|---|
| RecentForm | `RecentProductionScore` / `SignalType.RecentProduction` | High when prior REG games exist |
| Usage | `UsageScore` + `OpportunityScore` | High on reconstructed features |
| RoleHealth | `RoleNote` + Health/Injury signals | Role high; injury sparse |
| Matchup | Team/matchup aspects | **Unavailable** historically — not experimented |

Every transform records provenance via `knowledge_impact.transform` facts with cutoff timestamps.

### Data coverage

- nflverse: news unavailable; target/snap share often unavailable
- `OpponentTeam` null on historical PredictionContext
- Weather / rest / home-away / game script: unavailable markers only
- Quick Picks: **no** historical prop-line archive or settled-stat grader in-repo

### Control

`KnowledgeMode.Baseline` under Projection V2 + Confidence V2 + Decision Policy Off:

- Projection / floor / ceiling retained
- Usage, opportunity, recent production, role, health knowledge stripped
- AssessValues sees projection + default missing penalties

Default app mode remains `Passthrough` (identity) so frozen benchmarks stay reproducible outside the experiment.

### Experiment A — RecentForm

Explicit transform: if recent production ≥ 65 → OpportunityScore +6; if ≤ 35 → −6 (bounded).

### Experiment B — Usage

Explicit transform: restore OpportunityScore + UsageScore (AssessValues already reads them).

### Experiment C — RoleHealth

Explicit transform: restore Health signals; RoleNote heuristics adjust OpportunityScore ±4.

### Experiment D — Matchup

**Not run.** Team/matchup aspects are unavailable markers only; no fabricated opponent data.

### Development results

Leave-one-season-out on **2015 / 2018 / 2021** (2024 unused):

| Group | Mean val Δ total decision value | Classification |
|---|---:|---|
| RecentForm | **+0.00** | NEUTRAL |
| Usage | **+38.73** | POSITIVE (dev) |
| RoleHealth | **−10.00** | NEGATIVE (dev) |

Usage fold detail: 2015 −35.9 / 2018 +158.5 / 2021 −6.4 (driven by 2018).

### Frozen configurations

```
FrozenEnhancedGroups = Usage
RecentFormOpportunityDelta = 6 (thresholds 65 / 35)
RoleOpportunityDelta = 4
```

Projection V2, Confidence V2, Decision Policy V1 **unchanged**.

### 2024 holdout results

*(Filled after official single-pass evaluation.)*

### Ablation results

*(Filled after official evaluation.)*

### Start/Sit impact

Primary evaluation path for this experiment.

### Quick Picks impact

Live path applies bounded OpportunityScore deltas when Enhanced.  
**Historical Quick Picks evaluation is not available** (missing archived lines + settled outcomes). No Quick Picks predictive improvement is claimed.

### Failure analysis

*(Filled after holdout.)*

### Verdict

*(Filled after holdout.)*

### Knowledge signals worth keeping

*(Filled after holdout.)*

### Knowledge signals to reject

- Matchup (no data)
- RoleHealth (negative on development LOOCV) — pending holdout confirmation of frozen Usage-only set

### Data limitations

See coverage above. Do not invent matchup/weather/news.

### Next experiment

Candidate directions after this freeze:

1. Promote reconstructed snap/target shares into Usage when available
2. Build historical Quick Picks grading (prop archive + settled stats)
3. Only then consider learned models over earned signals
