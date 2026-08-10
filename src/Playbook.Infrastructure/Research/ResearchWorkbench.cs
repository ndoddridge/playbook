using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Playbook.Application.Predictions;
using Playbook.Application.Replay;
using Playbook.Core.Knowledge;
using Playbook.Core.Leagues;
using Playbook.Core.Predictions;
using Playbook.Core.Replay;
using Playbook.Core.Research;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Replay;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Offline research orchestration. Does not alter live Web production defaults.
/// Always restores KnowledgeMode.Passthrough after runs.
/// </summary>
public sealed class ResearchWorkbench
{
    private readonly string _workingDirectory;
    private readonly ResearchRunStore _store;

    public ResearchWorkbench(string? workingDirectory = null, string? outputRoot = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory ?? Directory.GetCurrentDirectory());
        _store = new ResearchRunStore(
            outputRoot is null
                ? Path.Combine(_workingDirectory, ResearchIntegrity.DefaultOutputRoot)
                : Path.IsPathRooted(outputRoot)
                    ? outputRoot
                    : Path.Combine(_workingDirectory, outputRoot));
    }

    public ResearchRunStore Store => _store;

    public async Task<int> ExecuteAsync(ResearchCliRequest request, CancellationToken cancellationToken = default)
    {
        return request.Command switch
        {
            ResearchCommandKind.Test => await RunTestsAsync(request, cancellationToken).ConfigureAwait(false),
            ResearchCommandKind.ListExperiments => ListExperiments(),
            ResearchCommandKind.Eval => await RunEvalAsync(request, cancellationToken).ConfigureAwait(false),
            ResearchCommandKind.Experiment => await RunExperimentAsync(request, cancellationToken).ConfigureAwait(false),
            ResearchCommandKind.Simulate => await RunSimulateAsync(request, cancellationToken).ConfigureAwait(false),
            ResearchCommandKind.Compare => RunCompare(request),
            ResearchCommandKind.Inspect => await RunInspectAsync(request, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unknown command: {request.Command}")
        };
    }

    private async Task<int> RunTestsAsync(ResearchCliRequest request, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var runDir = _store.CreateRunDirectory("test", timestamp);
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "test playbook.sln --nologo",
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet test.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        _store.WriteText(runDir, "dotnet-test.stdout.txt", stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _store.WriteText(runDir, "dotnet-test.stderr.txt", stderr);
        }

        WriteCommonManifest(request, runDir, timestamp, seasons: [],
            scope: ResearchScopeMode.Development, verdict: process.ExitCode == 0 ? "TESTS_PASSED" : "TESTS_FAILED",
            artifacts: new Dictionary<string, string>
            {
                ["stdout"] = Path.Combine(runDir, "dotnet-test.stdout.txt")
            },
            notes: new Dictionary<string, string>
            {
                ["exitCode"] = process.ExitCode.ToString()
            });

        Console.WriteLine(stdout);
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine(stderr);
        }

        Console.WriteLine($"Artifacts: {runDir}");
        return process.ExitCode;
    }

    private int ListExperiments()
    {
        Console.WriteLine("Catalogued research experiments:");
        foreach (var (id, desc) in ResearchIntegrity.ExperimentCatalog.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"  {id}");
            Console.WriteLine($"      {desc}");
        }

        Console.WriteLine();
        Console.WriteLine($"Rejected transforms: {string.Join(", ", ResearchIntegrity.RejectedKnowledgeTransforms)}");
        Console.WriteLine($"Production KnowledgeMode: {ResearchIntegrity.ProductionKnowledgeMode}");
        Console.WriteLine($"Holdout season: {ResearchIntegrity.HoldoutSeason} (require --allow-holdout)");
        return 0;
    }

    private async Task<int> RunEvalAsync(ResearchCliRequest request, CancellationToken cancellationToken)
    {
        var seasons = request.Seasons.Count > 0
            ? request.Seasons
            : ResearchIntegrity.DefaultDevelopmentSeasons.ToList();
        ResearchHoldoutGuard.ValidateSeasonScope(
            seasons, request.ScopeMode, request.AllowHoldout, isFittingOrParameterSelection: false);
        ResearchHoldoutGuard.ValidateExperimentNotSilentlyMutatingProduction(
            request.KnowledgeMode, request.ExperimentId);

        var timestamp = DateTimeOffset.UtcNow;
        var runDir = _store.CreateRunDirectory("eval", timestamp);

        using var provider = ResearchServiceFactory.CreateProvider();
        var knowledgeState = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        ConfigureKnowledge(knowledgeState, request.KnowledgeMode);

        try
        {
            var metrics = await EvaluateSeasonsAsync(
                    provider, seasons, request.PredictionSurface, request.CandidateUniverse, cancellationToken)
                .ConfigureAwait(false);
            metrics = metrics with { RunId = Path.GetFileName(runDir) };

            _store.WriteJson(runDir, "metrics.json", metrics);
            var md = BuildEvalMarkdown(request, seasons, metrics);
            _store.WriteText(runDir, "summary.md", md);
            WriteCommonManifest(request, runDir, timestamp, seasons, request.ScopeMode,
                verdict: null,
                artifacts: new Dictionary<string, string>
                {
                    ["metrics"] = Path.Combine(runDir, "metrics.json"),
                    ["summary"] = Path.Combine(runDir, "summary.md")
                },
                notes: new Dictionary<string, string>
                {
                    ["label"] = "historical-evaluation",
                    ["knowledgeMode"] = request.KnowledgeMode.ToString()
                });

            Console.WriteLine(md);
            Console.WriteLine($"Artifacts: {runDir}");
            return 0;
        }
        finally
        {
            knowledgeState.ConfigurePassthrough();
            AssertProductionRestored(knowledgeState);
        }
    }

    private async Task<int> RunExperimentAsync(ResearchCliRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ExperimentId) ||
            !ResearchIntegrity.ExperimentCatalog.ContainsKey(request.ExperimentId))
        {
            throw new InvalidOperationException(
                "Unknown or missing --experiment id. Use: research experiment --list");
        }

        // Catalogued experiments own their holdout protocol internally.
        var timestamp = DateTimeOffset.UtcNow;
        var runDir = _store.CreateRunDirectory($"experiment-{request.ExperimentId}", timestamp);

        using var provider = ResearchServiceFactory.CreateProvider();
        var knowledgeState = provider.GetRequiredService<KnowledgeImpactExperimentState>();

        try
        {
            var (text, verdict, seasons) = await DispatchExperimentAsync(
                    provider, request.ExperimentId!, cancellationToken)
                .ConfigureAwait(false);

            _store.WriteText(runDir, "report.txt", text);
            _store.WriteText(runDir, "summary.md",
                $"# Experiment `{request.ExperimentId}`\n\n**Verdict:** {verdict ?? "n/a"}\n\n```\n{text}\n```\n");
            _store.WriteJson(runDir, "metrics.json", new ResearchRunMetrics
            {
                RunId = Path.GetFileName(runDir),
                Surface = "experiment",
                Verdict = verdict
            });

            WriteCommonManifest(request, runDir, timestamp, seasons,
                scope: ResearchScopeMode.Mixed,
                verdict: verdict,
                artifacts: new Dictionary<string, string>
                {
                    ["report"] = Path.Combine(runDir, "report.txt"),
                    ["summary"] = Path.Combine(runDir, "summary.md"),
                    ["metrics"] = Path.Combine(runDir, "metrics.json")
                },
                notes: new Dictionary<string, string>
                {
                    ["experimentId"] = request.ExperimentId!,
                    ["holdoutPolicy"] = "Experiment harness isolates 2024; fitting must not use holdout.",
                    ["catalog"] = ResearchIntegrity.ExperimentCatalog[request.ExperimentId!]
                },
                allowHoldoutOverride: true,
                usedHoldoutForFitting: false);

            Console.WriteLine(text);
            Console.WriteLine($"Artifacts: {runDir}");
            return 0;
        }
        finally
        {
            knowledgeState.ConfigurePassthrough();
            AssertProductionRestored(knowledgeState);
        }
    }

    private async Task<int> RunSimulateAsync(ResearchCliRequest request, CancellationToken cancellationToken)
    {
        var seasons = ExpandSeasonRequest(request);
        if (seasons.Count == 0)
        {
            throw new InvalidOperationException(
                "Simulate requires --seasons <list|range> (e.g. 2015,2018,2021 or 2005-2024).");
        }

        ResearchHoldoutGuard.ValidateSeasonScope(
            seasons, request.ScopeMode, request.AllowHoldout, isFittingOrParameterSelection: false);
        ResearchHoldoutGuard.ValidateExperimentNotSilentlyMutatingProduction(
            request.KnowledgeMode, request.ExperimentId);

        if (request.DryRun)
        {
            Console.WriteLine("SIMULATE dry-run (no evaluation executed):");
            Console.WriteLine($"  seasons={string.Join(",", seasons)} count={seasons.Count}");
            Console.WriteLine($"  surface={request.PredictionSurface}");
            Console.WriteLine($"  mode={request.KnowledgeMode}");
            Console.WriteLine($"  universe={request.CandidateUniverse}");
            Console.WriteLine($"  seed={request.Seed?.ToString() ?? "n/a"}");
            Console.WriteLine($"  allowHoldout={request.AllowHoldout}");
            Console.WriteLine("Re-run without --dry-run to execute. Do not casually run 20 seasons.");
            return 0;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var runDir = _store.CreateRunDirectory("simulate", timestamp);

        using var provider = ResearchServiceFactory.CreateProvider();
        var knowledgeState = provider.GetRequiredService<KnowledgeImpactExperimentState>();
        ConfigureKnowledge(knowledgeState, request.KnowledgeMode);

        try
        {
            // Seed is recorded for future stochastic components; current harness is deterministic.
            var metrics = await EvaluateSeasonsAsync(
                    provider, seasons, request.PredictionSurface, request.CandidateUniverse, cancellationToken)
                .ConfigureAwait(false);
            metrics = metrics with
            {
                RunId = Path.GetFileName(runDir)
            };

            _store.WriteJson(runDir, "metrics.json", metrics);
            var md = BuildEvalMarkdown(request, seasons, metrics);
            _store.WriteText(runDir, "summary.md", "# Simulation run\n\n" + md);
            _store.WriteText(runDir, "config.txt",
                $"seed={request.Seed?.ToString() ?? "none"}\n" +
                $"seasons={string.Join(",", seasons)}\n" +
                $"surface={request.PredictionSurface}\n" +
                $"mode={request.KnowledgeMode}\n" +
                $"universe={request.CandidateUniverse}\n");

            WriteCommonManifest(request, runDir, timestamp, seasons, request.ScopeMode,
                verdict: null,
                artifacts: new Dictionary<string, string>
                {
                    ["metrics"] = Path.Combine(runDir, "metrics.json"),
                    ["summary"] = Path.Combine(runDir, "summary.md"),
                    ["config"] = Path.Combine(runDir, "config.txt")
                },
                notes: new Dictionary<string, string>
                {
                    ["kind"] = "simulation",
                    ["seed"] = request.Seed?.ToString() ?? "none",
                    ["repeatable"] = "yes — prior run directories are never overwritten"
                });

            Console.WriteLine(md);
            Console.WriteLine($"Artifacts: {runDir}");
            return 0;
        }
        finally
        {
            knowledgeState.ConfigurePassthrough();
            AssertProductionRestored(knowledgeState);
        }
    }

    private int RunCompare(ResearchCliRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompareLeft) ||
            string.IsNullOrWhiteSpace(request.CompareRight))
        {
            throw new InvalidOperationException("compare requires --a <runDir> and --b <runDir>.");
        }

        var leftDir = ResolveRunDir(request.CompareLeft);
        var rightDir = ResolveRunDir(request.CompareRight);
        var leftManifest = ResearchRunStore.LoadManifest(leftDir);
        var rightManifest = ResearchRunStore.LoadManifest(rightDir);
        var leftMetrics = ResearchRunStore.TryLoadMetrics(leftDir);
        var rightMetrics = ResearchRunStore.TryLoadMetrics(rightDir);

        var lines = new List<string>
        {
            "# Research run comparison",
            "",
            $"- Left: `{leftManifest.RunId}` ({leftManifest.Command}) @ {leftManifest.TimestampUtc:u}",
            $"- Right: `{rightManifest.RunId}` ({rightManifest.Command}) @ {rightManifest.TimestampUtc:u}",
            $"- Left git: `{leftManifest.GitSha ?? "n/a"}`",
            $"- Right git: `{rightManifest.GitSha ?? "n/a"}`",
            "",
            "## Configuration",
            "",
            $"| Field | Left | Right |",
            $"|---|---|---|",
            $"| Seasons | {string.Join(",", leftManifest.Seasons)} | {string.Join(",", rightManifest.Seasons)} |",
            $"| Surface | {leftManifest.PredictionSurface} | {rightManifest.PredictionSurface} |",
            $"| KnowledgeMode | {leftManifest.KnowledgeMode} | {rightManifest.KnowledgeMode} |",
            $"| Universe | {leftManifest.CandidateUniverse} | {rightManifest.CandidateUniverse} |",
            $"| Experiment | {leftManifest.ExperimentId ?? "n/a"} | {rightManifest.ExperimentId ?? "n/a"} |",
            $"| Verdict | {leftManifest.Verdict ?? "n/a"} | {rightManifest.Verdict ?? "n/a"} |",
            ""
        };

        if (leftMetrics is not null && rightMetrics is not null)
        {
            lines.Add("## Metrics");
            lines.Add("");
            lines.Add("| Metric | Left | Right | Δ |");
            lines.Add("|---|---:|---:|---:|");
            AddDelta(lines, "Start/Sit accuracy %", leftMetrics.StartSitAccuracyPercent, rightMetrics.StartSitAccuracyPercent);
            AddDelta(lines, "Start/Sit total DV", leftMetrics.StartSitTotalDecisionValue, rightMetrics.StartSitTotalDecisionValue);
            AddDelta(lines, "Start/Sit MAE", leftMetrics.StartSitProjectionMae, rightMetrics.StartSitProjectionMae);
            AddDelta(lines, "Start/Sit graded", leftMetrics.StartSitGraded, rightMetrics.StartSitGraded);
            AddDelta(lines, "QP MAE", leftMetrics.QuickPickMae, rightMetrics.QuickPickMae);
            AddDelta(lines, "QP Top5 %", leftMetrics.QuickPickTop5Percent, rightMetrics.QuickPickTop5Percent);
            AddDelta(lines, "QP predictions", leftMetrics.QuickPickPredictions, rightMetrics.QuickPickPredictions);
            AddDelta(lines, "Coverage %", leftMetrics.KnowledgeCoveragePercent, rightMetrics.KnowledgeCoveragePercent);
            lines.Add("");

            if (leftMetrics.PerSeasonTotalDecisionValue is not null ||
                rightMetrics.PerSeasonTotalDecisionValue is not null)
            {
                lines.Add("## Per-season total decision value");
                lines.Add("");
                var seasons = (leftMetrics.PerSeasonTotalDecisionValue?.Keys ?? [])
                    .Concat(rightMetrics.PerSeasonTotalDecisionValue?.Keys ?? [])
                    .Distinct()
                    .OrderBy(s => s);
                foreach (var season in seasons)
                {
                    double? l = leftMetrics.PerSeasonTotalDecisionValue is not null &&
                                leftMetrics.PerSeasonTotalDecisionValue.TryGetValue(season, out var lv)
                        ? lv
                        : null;
                    double? r = rightMetrics.PerSeasonTotalDecisionValue is not null &&
                                rightMetrics.PerSeasonTotalDecisionValue.TryGetValue(season, out var rv)
                        ? rv
                        : null;
                    lines.Add($"- {season}: {Fmt(l)} → {Fmt(r)} (Δ {Fmt((r ?? 0) - (l ?? 0))})");
                }
            }
        }
        else
        {
            lines.Add("_metrics.json missing on one or both runs — compared manifests only._");
        }

        var report = new ResearchCompareReport
        {
            LeftRunId = leftManifest.RunId,
            RightRunId = rightManifest.RunId,
            GeneratedAt = DateTimeOffset.UtcNow,
            Lines = lines
        };

        var timestamp = DateTimeOffset.UtcNow;
        var outDir = _store.CreateRunDirectory("compare", timestamp);
        var md = report.ToMarkdown();
        _store.WriteText(outDir, "comparison.md", md);
        WriteCommonManifest(request, outDir, timestamp, [], ResearchScopeMode.Mixed,
            verdict: null,
            artifacts: new Dictionary<string, string>
            {
                ["comparison"] = Path.Combine(outDir, "comparison.md")
            },
            notes: new Dictionary<string, string>
            {
                ["left"] = leftDir,
                ["right"] = rightDir
            },
            allowHoldoutOverride: true);

        Console.WriteLine(md);
        Console.WriteLine($"Artifacts: {outDir}");
        return 0;
    }

    private async Task<int> RunInspectAsync(ResearchCliRequest request, CancellationToken cancellationToken)
    {
        var season = request.InspectSeason
            ?? throw new InvalidOperationException("inspect requires --season <year>.");
        var week = request.InspectWeek ?? 7;

        if (season == ResearchIntegrity.HoldoutSeason && !request.AllowHoldout)
        {
            throw new InvalidOperationException(
                $"Inspecting holdout season {ResearchIntegrity.HoldoutSeason} requires --allow-holdout.");
        }

        using var provider = ResearchServiceFactory.CreateProvider();
        var source = provider.GetRequiredService<IHistoricalSnapshotSource>();
        var builder = provider.GetRequiredService<IHistoricalSnapshotBuilder>();
        var calendar = provider.GetRequiredService<IHistoricalSeasonCalendar>();

        var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
            .ConfigureAwait(false);
        var availableWeeks = new List<int>();
        for (var w = 1; w <= end; w++)
        {
            availableWeeks.Add(w);
        }

        var raw = await source.GetRawWeekAsync(
                season, week, ScoringType.Ppr, "nflverse", request.CandidateUniverse, cancellationToken)
            .ConfigureAwait(false);
        if (raw is null)
        {
            throw new InvalidOperationException($"No historical data for {season} W{week}.");
        }

        var (snapshot, outcomes) = builder.Build(raw);
        var usable = snapshot.Players.Count(p =>
            p.OpportunityScore is not null ||
            p.UsageScore is not null ||
            p.RecentProductionScore is not null ||
            !string.IsNullOrWhiteSpace(p.RoleNote));
        var validProj = snapshot.Players.Count(p => p.ProjectedPoints is not null);
        var withOutcome = snapshot.Players.Count(p => outcomes.ByPlayerId.ContainsKey(p.PlayerId));

        var sb = new StringBuilder();
        sb.AppendLine($"# Inspect {season} W{week}");
        sb.AppendLine();
        sb.AppendLine($"- Universe: {request.CandidateUniverse}");
        sb.AppendLine($"- Available weeks in season: {availableWeeks.Count} (1–{end})");
        sb.AppendLine($"- Players: {snapshot.Players.Count}");
        sb.AppendLine($"- Start/Sit roster candidates: {snapshot.Roster.Count}");
        sb.AppendLine($"- Valid projections: {validProj}");
        sb.AppendLine($"- Week outcomes: {withOutcome}");
        sb.AppendLine($"- Usable shared-knowledge signals: {usable} ({Pct(usable, snapshot.Players.Count):0.0}%)");
        sb.AppendLine($"- Cutoff: {snapshot.InformationCutoff:u}");
        sb.AppendLine($"- Holdout season: {ResearchIntegrity.HoldoutSeason}");
        sb.AppendLine($"- Production KnowledgeMode: {ResearchIntegrity.ProductionKnowledgeMode}");
        sb.AppendLine();
        sb.AppendLine("## Unavailable / unknown categories");
        foreach (var u in snapshot.UnavailableSources.Take(12))
        {
            sb.AppendLine($"- {u}");
        }

        sb.AppendLine();
        sb.AppendLine("## Rejected knowledge transforms");
        foreach (var r in ResearchIntegrity.RejectedKnowledgeTransforms)
        {
            sb.AppendLine($"- {r}");
        }

        var text = sb.ToString();
        var timestamp = DateTimeOffset.UtcNow;
        var runDir = _store.CreateRunDirectory("inspect", timestamp);
        _store.WriteText(runDir, "inspect.md", text);
        WriteCommonManifest(request, runDir, timestamp, [season],
            season == ResearchIntegrity.HoldoutSeason ? ResearchScopeMode.Holdout : ResearchScopeMode.Development,
            verdict: null,
            artifacts: new Dictionary<string, string>
            {
                ["inspect"] = Path.Combine(runDir, "inspect.md")
            },
            notes: new Dictionary<string, string>
            {
                ["week"] = week.ToString()
            },
            allowHoldoutOverride: request.AllowHoldout);

        Console.WriteLine(text);
        Console.WriteLine($"Artifacts: {runDir}");
        return 0;
    }

    private async Task<ResearchRunMetrics> EvaluateSeasonsAsync(
        IServiceProvider provider,
        IReadOnlyList<int> seasons,
        ResearchPredictionSurface surface,
        HistoricalCandidateUniverse universe,
        CancellationToken cancellationToken)
    {
        var seasonRunner = provider.GetRequiredService<IMultiWeekHistoricalReplayRunner>();
        var calendar = provider.GetRequiredService<IHistoricalSeasonCalendar>();
        var qp = provider.GetRequiredService<IQuickPicksHistoricalEvaluationRunner>();

        var perSeasonDv = new Dictionary<int, double>();
        var perSeasonQpMae = new Dictionary<int, double>();
        var ssGraded = 0;
        var ssCorrect = 0;
        double ssTot = 0;
        var ssMae = new List<double>();
        var ssCandidates = 0;
        var qpPreds = 0;
        var qpMae = new List<double>();
        var qpTop5 = new List<double>();
        var qpTop10 = new List<double>();

        foreach (var season in seasons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = await calendar.GetRegularSeasonEndWeekAsync(season, cancellationToken)
                .ConfigureAwait(false);

            if (surface is ResearchPredictionSurface.StartSit or ResearchPredictionSurface.Both)
            {
                var card = await seasonRunner.RunAsync(
                        new MultiWeekReplayRequest
                        {
                            Season = season,
                            StartWeek = 1,
                            EndWeek = end,
                            FixtureId = "nflverse",
                            ContinueOnWeekFailure = true,
                            CandidateUniverse = universe
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                var tot = card.AllGrades
                    .Where(g => g.ActualDecisionDifferential is not null)
                    .Sum(g => g.ActualDecisionDifferential!.Value);
                perSeasonDv[season] = Math.Round(tot, 2);
                ssTot += tot;
                ssGraded += card.CorrectDecisions + card.IncorrectDecisions;
                ssCorrect += card.CorrectDecisions;
                if (card.CurrentModelMae is double mae)
                {
                    ssMae.Add(mae);
                }

                ssCandidates += card.DataQuality.PlayersEvaluated;
            }

            if (surface is ResearchPredictionSurface.QuickPicks or ResearchPredictionSurface.Both)
            {
                var qpCard = await qp.RunSeasonAsync(
                        season,
                        QuickPickMode.Baseline,
                        fixtureId: "nflverse",
                        candidateUniverse: universe,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                perSeasonQpMae[season] = Math.Round(qpCard.MeanAbsoluteError, 3);
                qpPreds += qpCard.PredictionsEvaluated;
                qpMae.Add(qpCard.MeanAbsoluteError);
                qpTop5.Add(qpCard.Top5HitRate);
                qpTop10.Add(qpCard.Top10HitRate);
            }
        }

        return new ResearchRunMetrics
        {
            RunId = "pending",
            Surface = surface.ToString(),
            StartSitCandidates = surface is ResearchPredictionSurface.QuickPicks ? null : ssCandidates,
            StartSitGraded = surface is ResearchPredictionSurface.QuickPicks ? null : ssGraded,
            StartSitAccuracyPercent = ssGraded == 0
                ? null
                : Math.Round(100.0 * ssCorrect / ssGraded, 1),
            StartSitTotalDecisionValue = surface is ResearchPredictionSurface.QuickPicks
                ? null
                : Math.Round(ssTot, 2),
            StartSitProjectionMae = ssMae.Count == 0 ? null : Math.Round(ssMae.Average(), 2),
            QuickPickPredictions = surface is ResearchPredictionSurface.StartSit ? null : qpPreds,
            QuickPickMae = qpMae.Count == 0 ? null : Math.Round(qpMae.Average(), 3),
            QuickPickTop5Percent = qpTop5.Count == 0 ? null : Math.Round(qpTop5.Average(), 1),
            QuickPickTop10Percent = qpTop10.Count == 0 ? null : Math.Round(qpTop10.Average(), 1),
            PerSeasonTotalDecisionValue = perSeasonDv.Count == 0 ? null : perSeasonDv,
            PerSeasonQuickPickMae = perSeasonQpMae.Count == 0 ? null : perSeasonQpMae
        };
    }

    private async Task<(string Text, string? Verdict, List<int> Seasons)> DispatchExperimentAsync(
        IServiceProvider provider,
        string experimentId,
        CancellationToken cancellationToken)
    {
        var id = experimentId.ToLowerInvariant();
        return id switch
        {
            "projection-calibration-v2" => await Wrap(
                HistoricalReplayCommands.RunProjectionCalibrationExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenProjectionCalibrationV2.DevelopmentSeasons.Append(FrozenProjectionCalibrationV2.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "confidence-calibration-v2" => await Wrap(
                HistoricalReplayCommands.RunConfidenceCalibrationExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenDecisionConfidenceCalibrationV2.DevelopmentSeasons
                        .Append(FrozenDecisionConfidenceCalibrationV2.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "confidence-aware-decision-policy-v1" => await Wrap(
                HistoricalReplayCommands.RunConfidenceAwareDecisionPolicyExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenConfidenceAwareDecisionPolicyV1.DevelopmentSeasons.Append(FrozenConfidenceAwareDecisionPolicyV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "knowledge-impact-v1" => await Wrap(
                HistoricalReplayCommands.RunKnowledgeImpactExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenKnowledgeImpactExperimentV1.DevelopmentSeasons.Append(FrozenKnowledgeImpactExperimentV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "recent-form-thin-margin-v1" => await Wrap(
                HistoricalReplayCommands.RunRecentFormThinMarginExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenRecentFormThinMarginExperimentV1.DevelopmentSeasons.Append(FrozenRecentFormThinMarginExperimentV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "data-sufficiency-trust-v1" => await Wrap(
                HistoricalReplayCommands.RunDataSufficiencyTrustExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenDataSufficiencyTrustExperimentV1.DevelopmentSeasons.Append(FrozenDataSufficiencyTrustExperimentV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "quick-picks-historical-v1" => await Wrap(
                HistoricalReplayCommands.RunQuickPicksHistoricalEvaluationAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenQuickPicksHistoricalEvaluationV1.DevelopmentSeasons.Append(FrozenQuickPicksHistoricalEvaluationV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "quick-picks-recent-form-v1" => await Wrap(
                HistoricalReplayCommands.RunQuickPicksRecentFormExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenQuickPicksRecentFormExperimentV1.DevelopmentSeasons.Append(FrozenQuickPicksRecentFormExperimentV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "shared-knowledge-expanded-universe-v1" => await Wrap(
                HistoricalReplayCommands.RunSharedKnowledgeExpandedUniverseExperimentAsync(provider, cancellationToken),
                r => (r.ToReportText(), r.Verdict.ToString(),
                    FrozenSharedKnowledgeExpandedUniverseExperimentV1.DevelopmentSeasons
                        .Append(FrozenSharedKnowledgeExpandedUniverseExperimentV1.HoldoutSeason).ToList()))
                .ConfigureAwait(false),
            "historical-evaluation-coverage-v1" => await Wrap(
                HistoricalReplayCommands.RunHistoricalEvaluationCoverageAsync(provider, cancellationToken),
                r => (r.ToReportText(), "COVERAGE",
                    new List<int>
                    {
                        FrozenHistoricalEvaluationCoverageV1.DevelopmentSeason,
                        FrozenHistoricalEvaluationCoverageV1.HoldoutSeason
                    }))
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Experiment not wired: {experimentId}")
        };

        static async Task<(string, string?, List<int>)> Wrap<T>(
            Task<T> task,
            Func<T, (string, string?, List<int>)> map)
        {
            var result = await task.ConfigureAwait(false);
            return map(result);
        }
    }

    private ResearchRunManifest WriteCommonManifest(
        ResearchCliRequest request,
        string runDir,
        DateTimeOffset timestamp,
        IReadOnlyList<int> seasons,
        ResearchScopeMode scope,
        string? verdict,
        IReadOnlyDictionary<string, string> artifacts,
        IReadOnlyDictionary<string, string> notes,
        bool allowHoldoutOverride = false,
        bool usedHoldoutForFitting = false)
    {
        var (sha, branch) = ResearchRunStore.CaptureGitIdentity(_workingDirectory);
        var manifest = new ResearchRunManifest
        {
            RunId = Path.GetFileName(runDir),
            Command = request.Command.ToString(),
            TimestampUtc = timestamp,
            GitSha = sha,
            GitBranch = branch,
            WorkingDirectory = _workingDirectory,
            Argv = request.RawArgv,
            Seasons = seasons.ToList(),
            ScopeMode = scope,
            AllowHoldout = allowHoldoutOverride || request.AllowHoldout,
            UsedHoldoutForFitting = usedHoldoutForFitting,
            PredictionSurface = request.PredictionSurface,
            KnowledgeMode = request.KnowledgeMode,
            CandidateUniverse = request.CandidateUniverse,
            ExperimentId = request.ExperimentId,
            Seed = request.Seed,
            OutputDirectory = runDir,
            ProductionKnowledgeMode = ResearchIntegrity.ProductionKnowledgeMode.ToString(),
            RejectedTransforms = ResearchIntegrity.RejectedKnowledgeTransforms.ToList(),
            Verdict = verdict,
            ArtifactPaths = artifacts,
            Notes = notes
        };
        return _store.WriteManifest(runDir, manifest);
    }

    private string ResolveRunDir(string pathOrId)
    {
        if (Directory.Exists(pathOrId))
        {
            return Path.GetFullPath(pathOrId);
        }

        var underRoot = Path.Combine(_store.Root, pathOrId);
        if (Directory.Exists(underRoot))
        {
            return underRoot;
        }

        throw new DirectoryNotFoundException($"Run directory not found: {pathOrId}");
    }

    private static void ConfigureKnowledge(
        KnowledgeImpactExperimentState state,
        ResearchKnowledgeModeLabel mode)
    {
        switch (mode)
        {
            case ResearchKnowledgeModeLabel.Baseline:
                state.ConfigureBaseline();
                break;
            case ResearchKnowledgeModeLabel.Enhanced:
                // Enhanced without groups is refused by guard; catalog experiments set their own state.
                state.ConfigurePassthrough();
                break;
            default:
                state.ConfigurePassthrough();
                break;
        }
    }

    private static void AssertProductionRestored(KnowledgeImpactExperimentState state)
    {
        if (state.Mode != KnowledgeMode.Passthrough)
        {
            throw new InvalidOperationException(
                $"Research run leaked KnowledgeMode={state.Mode}. Production must remain Passthrough.");
        }
    }

    private static List<int> ExpandSeasonRequest(ResearchCliRequest request)
    {
        if (request.SeasonCount is int n && n > 0 && request.Seasons.Count == 0)
        {
            // Convenience: --seasons 20 means "20 most recent regular seasons ending before holdout"
            // unless holdout explicitly allowed — then ending at holdout.
            var end = request.AllowHoldout
                ? ResearchIntegrity.HoldoutSeason
                : ResearchIntegrity.HoldoutSeason - 1;
            var start = end - n + 1;
            return Enumerable.Range(start, n).ToList();
        }

        return request.Seasons.ToList();
    }

    private static string BuildEvalMarkdown(
        ResearchCliRequest request,
        IReadOnlyList<int> seasons,
        ResearchRunMetrics metrics)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Historical evaluation");
        sb.AppendLine();
        sb.AppendLine($"- Seasons: {string.Join(", ", seasons)}");
        sb.AppendLine($"- Surface: {request.PredictionSurface}");
        sb.AppendLine($"- KnowledgeMode: {request.KnowledgeMode}");
        sb.AppendLine($"- Universe: {request.CandidateUniverse}");
        sb.AppendLine($"- Scope: {request.ScopeMode}");
        sb.AppendLine();
        if (metrics.StartSitGraded is not null)
        {
            sb.AppendLine("## Start/Sit");
            sb.AppendLine($"- Candidates (player-weeks evaluated): {metrics.StartSitCandidates}");
            sb.AppendLine($"- Graded: {metrics.StartSitGraded}");
            sb.AppendLine($"- Accuracy: {metrics.StartSitAccuracyPercent:0.0}%");
            sb.AppendLine($"- Total decision value: {metrics.StartSitTotalDecisionValue:0.00}");
            sb.AppendLine($"- Projection MAE: {metrics.StartSitProjectionMae:0.00}");
            sb.AppendLine();
        }

        if (metrics.QuickPickPredictions is not null)
        {
            sb.AppendLine("## Quick Picks");
            sb.AppendLine($"- Predictions: {metrics.QuickPickPredictions}");
            sb.AppendLine($"- MAE: {metrics.QuickPickMae:0.000}");
            sb.AppendLine($"- Top5: {metrics.QuickPickTop5Percent:0.0}%");
            sb.AppendLine($"- Top10: {metrics.QuickPickTop10Percent:0.0}%");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AddDelta(List<string> lines, string name, double? left, double? right)
    {
        lines.Add($"| {name} | {Fmt(left)} | {Fmt(right)} | {Fmt((right ?? 0) - (left ?? 0))} |");
    }

    private static void AddDelta(List<string> lines, string name, int? left, int? right)
    {
        lines.Add($"| {name} | {left?.ToString() ?? "n/a"} | {right?.ToString() ?? "n/a"} | {(right ?? 0) - (left ?? 0):+0;-0;0} |");
    }

    private static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.###");

    private static double Pct(int num, int den) => den == 0 ? 0 : 100.0 * num / den;
}

/// <summary>Parsed CLI request for the research workbench.</summary>
public sealed class ResearchCliRequest
{
    public required ResearchCommandKind Command { get; init; }

    public required IReadOnlyList<string> RawArgv { get; init; }

    public IReadOnlyList<int> Seasons { get; init; } = [];

    public int? SeasonCount { get; init; }

    public ResearchPredictionSurface PredictionSurface { get; init; } = ResearchPredictionSurface.Both;

    public ResearchKnowledgeModeLabel KnowledgeMode { get; init; } = ResearchKnowledgeModeLabel.Baseline;

    public HistoricalCandidateUniverse CandidateUniverse { get; init; } =
        HistoricalCandidateUniverse.LabRoster;

    public ResearchScopeMode ScopeMode { get; init; } = ResearchScopeMode.Development;

    public bool AllowHoldout { get; init; }

    public bool DryRun { get; init; }

    public string? ExperimentId { get; init; }

    public string? CompareLeft { get; init; }

    public string? CompareRight { get; init; }

    public int? InspectSeason { get; init; }

    public int? InspectWeek { get; init; }

    public int? Seed { get; init; }

    public string? OutputRoot { get; init; }
}
