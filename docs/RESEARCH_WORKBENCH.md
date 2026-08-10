# Research Workbench

Command-line research tooling for Playbook historical evaluation, controlled experiments, and (later) multi-season simulation.

This does **not** change the live Blazor app. Production remains `KnowledgeMode.Passthrough`.

## Quick start

```bash
# From repo root
dotnet run --project src/Playbook.Research -- --help

# Or via helper script
./scripts/research --help
```

Results are written under `research-runs/<runId>/` and **never overwritten**.

---

## 1. Tests

```bash
dotnet test playbook.sln --nologo
# or
dotnet run --project src/Playbook.Research -- test
```

## 2. Historical evaluation

```bash
# Development seasons, Start/Sit, Baseline control, LabRoster (frozen-benchmark compatible)
dotnet run --project src/Playbook.Research -- eval \
  --seasons 2015,2018,2021 \
  --type startsit \
  --mode baseline \
  --universe lab \
  --scope development

# Expanded universe, both surfaces, Passthrough knowledge
dotnet run --project src/Playbook.Research -- eval \
  --seasons 2015,2018,2021 \
  --type both \
  --mode passthrough \
  --universe expanded \
  --scope development

# Explicit one-shot holdout (requires flag)
dotnet run --project src/Playbook.Research -- eval \
  --seasons 2024 \
  --type both \
  --mode baseline \
  --universe expanded \
  --scope holdout \
  --allow-holdout
```

## 3. Experiments

```bash
dotnet run --project src/Playbook.Research -- experiment --list

dotnet run --project src/Playbook.Research -- experiment --id shared-knowledge-expanded-universe-v1
dotnet run --project src/Playbook.Research -- experiment --id data-sufficiency-trust-v1
dotnet run --project src/Playbook.Research -- experiment --id quick-picks-historical-v1
```

Catalogued experiments own their internal freeze/holdout protocol. The CLI records git SHA, config, and reports under a new run directory.

## 4. Simulation (multi-season, repeatable)

**Do not casually run 20 seasons.** The entry point exists for later manual research.

```bash
# Preview only
dotnet run --project src/Playbook.Research -- simulate \
  --seasons 20 \
  --type both \
  --mode baseline \
  --universe expanded \
  --seed 1 \
  --dry-run

# Execute a small repeatable slice
dotnet run --project src/Playbook.Research -- simulate \
  --seasons 2015,2018,2021 \
  --type both \
  --mode baseline \
  --universe expanded \
  --seed 1 \
  --out research-runs

# Later: 20 seasons ending the year before holdout (2004–2023; no 2024)
dotnet run --project src/Playbook.Research -- simulate \
  --seasons 20 \
  --type both \
  --mode baseline \
  --universe expanded \
  --seed 1
# Equivalent explicit form:
#   --seasons 2004-2023

# If the window must include 2024, require the holdout flag (not for fitting)
dotnet run --project src/Playbook.Research -- simulate \
  --seasons 2005-2024 \
  --type both \
  --mode baseline \
  --universe expanded \
  --scope mixed \
  --allow-holdout \
  --seed 1
```

Each execution creates a **new** run directory so prior simulations are preserved for comparison.

## 5. Compare runs

```bash
dotnet run --project src/Playbook.Research -- compare \
  --a research-runs/<runIdA> \
  --b research-runs/<runIdB>
```

Shows MAE, accuracy, Top-5, decision value, coverage, per-season totals, and config/git deltas.

## 6. Inspect coverage / data availability

```bash
dotnet run --project src/Playbook.Research -- inspect \
  --season 2018 \
  --week 7 \
  --universe expanded
```

## 7. Where results are stored

```
research-runs/
  <timestamp>-<command>-<id>/
    manifest.json     # git SHA, argv, seasons, modes, seed, paths
    metrics.json      # machine-readable aggregates
    summary.md        # human summary
    report.txt        # experiment text (when applicable)
    config.txt        # simulation config snapshot
    comparison.md     # compare output
```

`research-runs/` is gitignored. Keep important summaries by copying into `docs/` when freezing a conclusion.

## 8. 2024 holdout protection

| Rule | Behavior |
|---|---|
| Development scope + 2024 | **Rejected** |
| Any season list including 2024 without `--allow-holdout` | **Rejected** |
| Fitting/parameter selection including 2024 | **Rejected** |
| Holdout scope | Must be only 2024 and require `--allow-holdout` |
| Catalogued experiments | Internal harness isolates holdout; CLI records `UsedHoldoutForFitting=false` |

Frozen LabRoster 2018 benchmark path remains the default universe for eval unless `--universe expanded` is set.

## 9. Rejected Knowledge transforms

Still rejected/disabled for enablement:

- Usage
- RoleHealth
- RecentForm
- RecentFormThinMargin
- DataSufficiencyTrust

PR #25 (`shared-knowledge-expanded-universe-v1`) tested Passthrough vs Baseline on ExpandedSkillUniverse and concluded **REGRESSION** on 2024 (Start/Sit tot 170.60 → 93.30). That conclusion remains documented in `docs/SHARED_KNOWLEDGE_EXPANDED_UNIVERSE_EXPERIMENT_V1.md`.

## 10. Production mode

**`KnowledgeMode.Passthrough`**

Every workbench command restores Passthrough after execution. The CLI refuses ad-hoc `--mode enhanced` without a catalogued `--experiment` id.

---

## Manual Research Loop

1. Make **ONE** hypothesis.
2. Change the smallest possible thing in code/config.
3. Fit/select only on development seasons (`2015/2018/2021` or an explicit non-holdout set).
4. Freeze the configuration (constants + docs).
5. Run the untouched holdout **once** with `--scope holdout --allow-holdout`.
6. Record the result under `research-runs/` (and copy a summary into `docs/` if accepted/rejected).
7. Revert/reject if it does not generalize.
8. `compare` against previous runs.
9. Only then consider the next hypothesis.

Repeated simulations:

```bash
# Run A
./scripts/research simulate --seasons 2015,2018,2021 --type both --mode baseline --universe expanded --seed 1
# ... make one small change ...
# Run B
./scripts/research simulate --seasons 2015,2018,2021 --type both --mode baseline --universe expanded --seed 1
# Compare
./scripts/research compare --a <runA> --b <runB>
```
