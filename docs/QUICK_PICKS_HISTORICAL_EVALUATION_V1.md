# Quick Picks Historical Evaluation V1

**EvaluationId:** `quick-picks-historical-evaluation-v1`  
**EvaluatorVersion:** `qp-hist-eval-v1`  
**Date:** 2026-08-10  

Makes Quick Picks historically measurable so shared football knowledge can be tested on a second prediction type without changing live UI or production scoring.

---

### Quick Picks semantics

Live Quick Picks (Engine v0.3) are **not** Start/Sit:

| Aspect | Live behavior |
|---|---|
| Unit | Player × sportsbook prop market (yards, receptions, TDs, …) |
| Inputs | `PropLine` + counting-stat projection (`PropStatProjector`) + intelligence/injury/usage |
| Score | Projection-vs-line → Edge / Probability / Confidence → composite `OpportunityScore` |
| Ranking | `OpportunityScore` ↓, then Edge, Probability, Confidence |
| Output | Diverse Top-N over/under leans on the selected NFL week slate |
| Knowledge | `SharedKnowledgeBundle` attached via `PredictionContext`; optional bounded OpportunityScore deltas under Knowledge Enhanced |

Historical V1 preserves the **player × counting market × projected value × ranking** core. It does **not** invent sportsbook lines.

---

### Historical prediction definition

`QuickPickHistoricalPrediction` at cutoff for week W:

- season / week / player / position / team  
- `PredictionType` = `CountingStatProjection`  
- market ∈ {PassingYards, RushingYards, ReceivingYards, Receptions}  
- `ProjectedValue` = cutoff-safe prior-week feature average (e.g. `AvgRushYards`)  
- `RankingScore` = projected value (Baseline); Enhanced may add bounded knowledge OpportunityScore deltas when allowed  
- `RankInMarket` = 1…N within (season, week, market)  
- confidence from reconstructed projection confidence when available  
- optional `KnowledgeContext` (Enhanced)  
- `CutoffTimestamp` = snapshot information cutoff  

Markets by position: QB→PassingYards; RB→Rush/Rec yards + Receptions; WR/TE→Rec yards + Receptions.

---

### Cutoff rules

At historical week W, Quick Picks may only consume information available before the prediction cutoff:

| Allowed | Forbidden |
|---|---|
| Prior-week counting averages / features | Week W actual performance |
| Pre-cutoff injury / news / role | Future injuries, role changes, news |
| Reconstructed projections at cutoff | Future projections / evidence |

Outcomes (`HistoricalPlayerOutcome` counting actuals) are attached **only after** predictions are finalized.  
`KnowledgeTemporalGuard.AssertNoFutureLeak` runs on Enhanced knowledge contexts. Explicit unit tests cover future-injury exclusion on the controlled 2018 W7 fixture.

---

### Grading methodology

**No sportsbook O/U hit rate** — there is no historical prop-line archive in-repo.

Primary (projection quality):

\[
\text{AbsoluteError} = |\hat{y} - y|, \quad
\text{SignedError} = \hat{y} - y, \quad
\text{TotalPredictionValue} = -\sum |\hat{y} - y|
\]

Secondary (ranking quality within market):

- Top-5 / Top-10 hit rate among projected-top-N (also in actual-top-N)  
- Mean rank absolute error \(|\hat{r} - r|\)

Minimum scorecard fields: weeks evaluated, predictions evaluated, MAE, bias, Top-5/Top-10 hit rates, mean rank error, total prediction value, average confidence.

---

### Development seasons

`{2015, 2018, 2021}` — used to establish the harness and Baseline scorecard.  
**2024 is not used** for formulas, thresholds, signal selection, weights, or policy.

### Holdout season

`2024` — exactly one official holdout after the evaluator is frozen.

---

### Baseline scorecard

`QuickPickMode.Baseline` — current QP historical semantics **without** knowledge influence.

| Season | Weeks | Predictions | MAE | Bias | Top-5 | Top-10 | Rank MAE | Tot. value | Avg conf |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 2015 | 16 | 508 | 22.521 | +8.664 | 82.2% | 95.9% | 2.285 | −11440.5 | 72.5 |
| 2018 | 16 | 549 | 26.727 | +9.514 | 69.1% | 85.9% | 3.200 | −14673.0 | 73.8 |
| 2021 | 17 | 612 | 23.444 | +10.269 | 79.1% | 87.6% | 2.526 | −14347.5 | 74.6 |
| **Dev agg** | — | **1669** | **24.230** | — | **76.8%** | — | — | **−40461.0** | — |

Baseline is deterministic across repeated runs.

---

### Knowledge-enhanced scorecard

`QuickPickMode.Enhanced` with:

```text
AllowedEnhancedGroups = None
```

Rejected Knowledge Impact V1 transforms stay disabled:

| Group | Status |
|---|---|
| Usage | REJECTED (2024 holdout regression) — not re-enabled |
| RoleHealth | REJECTED (negative development) — not re-enabled |
| RecentForm | Neutral — not assumed helpful |
| Matchup | Unavailable historically |

Enhanced attaches SharedKnowledge (observational) but applies **identity** transforms.

| Season | Predictions | MAE | Top-5 | Tot. value |
|---:|---:|---:|---:|---:|
| 2015 | 508 | 22.521 | 82.2% | −11440.5 |
| 2018 | 549 | 26.727 | 69.1% | −14673.0 |
| 2021 | 612 | 23.444 | 79.1% | −14347.5 |
| Dev agg | 1669 | 24.230 | 76.8% | −40461.0 |

**Identical to Baseline.**

---

### Prediction-change analysis

| Scope | Compared | Changed | Unchanged | % changed | Avg \|Δ\| | Identical? |
|---|---:|---:|---:|---:|---:|---|
| Development | 1669 | 0 | 1669 | 0.00% | 0 | **Yes** |
| Holdout 2024 | 607 | 0 | 607 | 0.00% | 0 | **Yes** |

MAE / Top-5 / total value unchanged Baseline → Enhanced on both scopes.

Having access to knowledge ≠ changing predictions. V1 Enhanced is explicitly **not** an improvement claim.

---

### Success/failure ledger

| Class | Development | Holdout 2024 |
|---|---:|---:|
| HELPED (baseline wrong → enhanced better rank) | 0 | 0 |
| HURT (baseline correct → enhanced worse rank) | 0 | 0 |
| NEUTRAL | 1669 | 607 |

No position / role / confidence / margin slices yet — empty change set. Ledger machinery is ready for future allowed groups.

---

### Official 2024 holdout

| Mode | Weeks | Predictions | MAE | Bias | Top-5 | Top-10 | Rank MAE | Tot. value | Avg conf |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Baseline | 17 | 607 | 22.452 | +4.950 | 75.6% | 90.2% | 2.547 | −13628.2 | 73.9 |
| Enhanced | 17 | 607 | 22.452 | +4.950 | 75.6% | 90.2% | 2.547 | −13628.2 | 73.9 |

Holdout unused during development. Frozen layers unchanged (Projection V2 / Confidence V2 / Decision Policy V1).

---

### Data limitations

- **No historical prop-line archive** → cannot grade live O/U hit rate or Edge vs books  
- Evaluation uses the reconstructed **lab roster** (~17 skill players/week), not a full NFL slate — Top-N hit rates are inflated vs a true market-wide board  
- Week 1 often yields fewer predictions (insufficient prior history)  
- Matchup / weather / rest / game script remain unavailable  
- Injury designations sparse historically  
- Counting projections are simple prior-week averages, not the full live `PropStatProjector` path  

---

### Leakage tests

Covered in `QuickPicksHistoricalEvaluationTests`:

- Snapshot builder strips future injury/news; counting actuals stay on outcomes only  
- Enhanced knowledge for Delta WR cannot see future Out/Hamstring as known injury evidence  
- Predictions finalized before outcome attachment; projected ≠ actual week values  
- Generator + week scorecards deterministic on repeated runs  
- Season isolation: development `{2015,2018,2021}`, holdout `2024`  
- Rejected knowledge groups not in `AllowedEnhancedGroups`  
- Official evaluation restores `KnowledgeMode.Passthrough`

---

### Verdict

**BASELINE ESTABLISHED**

- Quick Picks can be replayed and graded historically  
- Baseline performance is documented and deterministic  
- Enhanced (AllowedGroups=None) is observational and **identical** to Baseline  
- No Quick Picks knowledge improvement is claimed  
- Rejected transforms were not silently re-enabled  
- Frozen projection / confidence / decision layers unchanged  
- Harness is ready for future knowledge experiments on this surface  

---

### Next experiment

1. Propose **one** candidate knowledge transform for Quick Picks that is justified independently of the rejected Start/Sit Usage transform (do not retest/tune Usage).  
2. Run development Baseline vs Enhanced on this harness; freeze; one 2024 holdout.  
3. Only claim improvement if holdout MAE / ranking value improves with a material change rate.  
4. Optionally expand candidate pool beyond the lab roster once a broader historical player set is available.  
5. Still do **not** add ML.
