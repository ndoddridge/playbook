# Shared Knowledge Model V1

### Purpose

Build a reusable football intelligence substrate that answers:

> What does the system know about this player, team, matchup, and situation at the prediction cutoff, how strong is each piece of evidence, and what implications does that evidence have?

This layer is **not** Start/Sit-specific. Prediction types consume it.

Frozen experiment layers remain untouched:

- Projection V2
- Confidence calibration V2
- Confidence-aware decision policy V1
- 2018 frozen benchmark / 2024 holdout results

### Architecture

```
Historical Data / Live Sources
    ↓
Historical Feature Reconstruction (when historical)
    ↓
Projection / Raw Signals
    ↓
SHARED KNOWLEDGE MODEL   ← this layer
    ↓
PredictionContext
    ↓
Prediction Type (Start/Sit, Quick Picks, …)
    ↓
Decision / Prediction
    ↓
Outcome / Evaluation
```

Key types:

| Type | Location | Role |
|---|---|---|
| `KnowledgeEvidence` | `Playbook.Core/Knowledge` | Scoped evidence with provenance |
| `SharedKnowledgeBundle` | same | Player/situation knowledge at cutoff |
| `PredictionContext` | same | Reusable consumer context |
| `KnowledgeTemporalGuard` | same | Cutoff enforcement |
| `ISharedKnowledgeModel` | `Playbook.Application/Knowledge` | Assembly port |
| `SharedKnowledgeModel` | `Playbook.Infrastructure/Knowledge` | Implementation |

Reuses existing `KnowledgeFact`, `KnowledgeSignal`, `PlayerKnowledge`, and `EvidenceStatus` — does not duplicate the decision knowledge contract.

### Knowledge representation

Evidence is scoped:

- **Player** — production, usage, opportunity, role, health/injury, news, projection, trend
- **Team** — offensive/defensive environment, pace, scoring, opponent strength, recent form
- **Matchup** — opponent tendencies, positional matchup, game environment
- **Context** — home/away, rest, weather, game script, teammate availability, role changes

Each item carries:

- statement
- direction (positive / negative / neutral / uncertainty)
- strength
- status (known / unknown / conflicting / low confidence)
- confidence (0–100)
- reliability (unknown / low / moderate / high)
- source
- observed-at timestamp
- information cutoff

Unavailable aspects are explicit markers (`IsUnavailableMarker`) with `EvidenceStatus.Unknown` — never coerced into positive or negative evidence.

### Temporal provenance

Every knowledge item is bounded by `InformationCutoff`.

`KnowledgeTemporalGuard`:

- filters facts/evidence/signals after the cutoff
- asserts no future leak on assembled bundles
- treats post-cutoff injuries/news as unavailable

Historical path: snapshot builder already strips future rows; the knowledge model re-validates.

Quick Picks path: injury `Date` / `LastUpdated` and intelligence `Created` must be ≤ cutoff.

### Evidence model

Simple and explainable (not ML):

- Direction + strength communicate implication
- Confidence / reliability communicate quality
- Unknown stays unknown

Positive example: “Usage increased / high opportunity score.”  
Negative example: “Listed Questionable at cutoff.”  
Unknown example: “No reliable weather information available at cutoff.”

### PredictionContext

Assembled fields:

- prediction type
- season / week / cutoff
- player / team / opponent
- shared knowledge bundle
- optional projection + market line
- optional fantasy `DecisionContext`

Consumable by:

- `StartSit`
- `QuickPick`
- `PlayerProjection`
- `OverUnder`
- `Touchdown`
- `Ranking`
- `Matchup`
- `PlayerPerformance`
- future types

### Start/Sit integration

1. Live: `PlayerKnowledgeComposer` → `ISharedKnowledgeModel.BuildStartSitPredictionContext` → `PlayerKnowledge` → `DecisionEngine`
2. Historical: `HistoricalSnapshot` → `BuildHistoricalPredictionContext` → `PlayerKnowledge` → `DecisionEngine`

DecisionEngine, Projection V2, Confidence V2, and Decision Policy V1 formulas are unchanged. The knowledge layer sits **above** them.

### Quick Picks integration

`QuickPicksService` builds a `PredictionContext` for each evaluation and attaches it to `QuickPickEvaluationContext.PredictionContext`.

Quick Pick scoring (`QuickPicksEngine`) is **not** replaced in this phase — the board continues to use existing edge/probability logic while consuming the shared substrate as a first-class attached knowledge context.

### Historical replay behavior

`HistoricalReplayRunner` reconstructs knowledge week-by-week from cutoff-safe snapshots via `ISharedKnowledgeModel`. Given season/week/cutoff, the knowledge state is reproducible and leakage-checked before decisions.

### Leakage protections

1. Snapshot builder strips future injury/news
2. Shared knowledge filters by `ObservedAt`
3. `KnowledgeTemporalGuard.AssertNoFutureLeak` in Start/Sit + Quick Picks + replay paths
4. Existing replay leakage regression tests retained
5. New tests for future injury on Quick Picks knowledge path

### Tests

`SharedKnowledgeModelTests` covers:

- deterministic knowledge
- historical cutoff / future injury+news exclusion
- temporal guard boundary
- unavailable aspects remain unknown
- positive + negative evidence
- Start/Sit PredictionContext + replay consumption
- Quick Picks PredictionContext consumption
- future injury excluded from Quick Picks knowledge
- live Start/Sit still produces recommendations
- frozen Projection / Confidence / Policy constants unchanged
- frozen 2018 benchmark reproducible

### Known data limitations

Not fabricated (explicitly unavailable today):

- snap / target / carry share (partial elsewhere, not promoted when missing)
- depth chart certainty
- team pace / scoring environment
- opponent tendencies / positional matchup grades
- weather, rest, home/away (when not in source)
- game script / teammate availability
- historical news for nflverse weeks

### Future extension points

1. Promote reconstructed usage shares into player evidence when available
2. Add real team/matchup providers without changing PredictionContext shape
3. Let Quick Picks scoring optionally weight shared evidence
4. Add Over/Under and TD engines as new `PredictionType` consumers
5. Later: train models against historically reconstructed knowledge states

Do not train ML in this phase. Do not optimize against 2024. Do not bury intelligence inside a single prediction type.
