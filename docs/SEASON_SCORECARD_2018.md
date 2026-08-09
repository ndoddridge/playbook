# Season Scorecard — 2018 Regular Season (Weeks 1–17)

Developer measurement report from the multi-week historical evaluation runner.

**Important:** This measures the current baseline intelligence. It does **not** tune the model against 2018.

Entry point:

```csharp
await HistoricalReplayCommands.RunReal2018SeasonAsync(services);
// or
await HistoricalReplayCommands.RunSeasonAsync(services, season: 2018, startWeek: 1, endWeek: 17);
```

## Headline results

| Area | Result |
|---|---|
| Weeks completed | **17/17** (0 skipped) |
| Players evaluated | 289 |
| Valid reconstructions | 272 (94.1%) |
| Fair projection N | 261 |
| Current model MAE | **11.62** |
| Baseline A MAE (recent avg) | **9.03** ← better |
| Baseline B MAE (opportunity-aware) | **11.62** (= current primary) |
| Model bias (actual − predicted) | **−8.53** (systematic over-projection) |
| Decisions | 99 (94 graded) |
| Decision accuracy | **60.6%** (57/94) |
| Avg / median / total decision value | 3.19 / 3.25 / **+300.0** |
| Avg confidence | 28.4 |

Note: the current primary projection engine is Baseline B (`baseline-opportunity-aware-v1`). Week-7-only metrics are not sufficient to conclude model quality; this season sample is the first meaningful measurement.

## Confidence calibration (measurement only — not adjusted)

| Bucket | n | Success | Avg decision value |
|---|---:|---:|---:|
| 0–20% | 22 | 68.2% | +4.74 |
| 20–40% | 77 | 58.3% | +2.72 |
| 40–60% | 0 | — | — |
| 60–80% | 0 | — | — |
| 80–100% | 0 | — | — |

Observable: nearly all decisions sit in the low-confidence bands, and the lowest band is not worse than 20–40%. Confidence is **not calibrated**.

## Position

| Pos | Proj N | Model MAE | Decisions | Accuracy | Avg decision value |
|---|---:|---:|---:|---:|---:|
| QB | 46 | 11.45 | 18 | 66.7% | +3.72 |
| RB | 73 | 11.67 | 29 | 57.7% | +4.85 |
| WR | 94 | 11.37 | 33 | 61.3% | +3.37 |
| TE | 48 | 12.21 | 19 | 57.9% | +0.13 |

## Week-by-week (selected)

| Week | Proj MAE | Accuracy | Avg decision value | Decisions | Avg conf |
|---:|---:|---:|---:|---:|---:|
| 1 | n/a | 50% | +5.25 | 4 | 12 |
| 7 | 8.85 | 66.7% | +2.97 | 6 | 28.7 |
| 10 | 10.46 | 16.7% | −13.58 | 6 | 36 |
| 12 | 9.82 | 85.7% | +9.73 | 7 | 34.6 |
| 16 | 7.21 | 0% | −4.47 | 4 | 35.8 |
| 17 | 11.82 | 25% | −4.20 | 8 | 33.4 |

Full week table is available from `SeasonScorecard.ToScorecardText()`.

## Observable patterns (no invented explanations)

- Over-projection bias across QB/RB/WR/TE (signed error ≈ −6 to −10).
- High projected scorers (≥20) have higher MAE than mid projections (gap ≈ 4.5).
- Early season (W1–6) decision accuracy higher than late season (W12+) in this sample.
- RB graded accuracy lowest among positions with n≥8; QB highest.
- Strong recommendation margins only slightly better than weak margins.
- Limited-history decisions were not worse than Sufficient in this sample (small Limited n).

## Failure ledger (largest costs)

Structured incorrect decisions are retained with evidence, alternatives, cost, sufficiency, and cutoff. Largest 2018 costs include:

- W10 Travis Kelce Start / Zach Ertz Sit (−29.9)
- W11 DeAndre Hopkins Start (−26.9)
- W17 Zach Ertz Start (−25.4)
- W7 Matt Ryan Start (−13.5)

Ask the scorecard/failure ledger for the full set — do not only trust aggregate accuracy.

## Data quality vs model quality

| Domain | Status |
|---|---|
| Reconstructed pre-week projections | PARTIAL (baseline A/B from prior weeks) |
| Usage / opportunity proxies | PARTIAL (~94% coverage on lab roster) |
| Role / depth | PARTIAL (~99% prior-week depth) |
| Injuries | PARTIAL (~3% non-healthy signals with timestamp ≤ cutoff) |
| News | UNAVAILABLE |
| Fantasy ownership | UNAVAILABLE (lab roster) |
| External as-of projection archive | UNAVAILABLE |

Poor MAE vs Baseline A is primarily a **model/formula** issue (opportunity-aware primary overshoots), not a missing-week-data issue for mid/late season. Missing news/ownership still limit decision context.

## Temporal guarantees

- Each week has its own information cutoff.
- Week N projections use only weeks `< N`.
- Actual Week N outcomes attach only after decisions are recorded.
- Multi-week runner asserts source weeks never include N or N+1.

## Next step (not done here)

MEASURE → IDENTIFY FAILURES → FORM HYPOTHESES → IMPROVE → REPLAY → COMPARE

Do **not** tune to 2018 yet. Do **not** scale to 20 years yet.
