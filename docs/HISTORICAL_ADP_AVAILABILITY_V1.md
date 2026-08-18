# Historical ADP and league availability v1

## Status

Available now: a read-only, descriptive historical layer on `/historical-intelligence`.
It is **not** used by `DraftAssistantService` or live recommendation scoring.

## Source audit

* Existing Playbook data: Sleeper is a trustworthy source for the league's actual historical picks, owners, draft settings, and timestamps. It does not provide preseason historical market ADP.
* nflverse is already used by Playbook for NFL history and identity support, but it does not supply season-correct preseason ADP snapshots.
* FantasyPros offers season-specific ADP pages (PPR and source-level/composite columns, including Sleeper). It is an established market source, but a season page alone does not prove the precise publication/export time needed to evaluate a particular past draft.

Therefore v1 intentionally has no automatic ADP scrape. It accepts a structured `HistoricalAdpSnapshot` only when the importing user supplies the original source, season, scoring format, stable player IDs, and actual snapshot timestamp. An undated record is rejected; a record published after the historical draft is retained but its pick comparison is unavailable.

## Coverage and format

Coverage is exactly the seasons explicitly imported. Snapshot records are keyed by league, season, league type, scoring label, source, and timestamp. Redraft and dynasty are separate; PPR, HalfPPR, and Standard are only joined when the snapshot label matches the draft scoring inferred from its reception setting. The import preserves optional overall rank, positional rank, and ADP without inventing missing fields.

## Comparison and classification

The newest snapshot at or before the draft timestamp is used. A stable Sleeper or Playbook player ID is required. If draft time, matching format, or ADP is absent, the comparison is `Unavailable`.

`delta = actual pick - ADP`; negative is earlier than market.

* `<= -18`: Major Reach
* `-17` through `-7`: Reach
* `-6` through `+6`: Market
* `+7` through `+17`: Value
* `>= +18`: Major Value

These fixed, deliberately broad thresholds are descriptions, not a claim of quality or predictive accuracy.

## Availability

Player ranges report observed draft count, seasons, min/median/mean/max, recent picks, distinct owners, and owner keys. Fewer than three league selections returns `UNKNOWN`. With enough observations: passing the observed maximum or median by the next pick is `LOW`; entering the observed range before its median is `MEDIUM`; staying before the observed minimum is `HIGH`. These labels are an evidence summary, not calibrated probabilities.

Position summaries report first position selected, average pick, and selections by round. Owner signals report position, sample/seasons, average selection and ADP delta only where time-correct comparisons exist.
