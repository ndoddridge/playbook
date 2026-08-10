using Playbook.Core.Knowledge;
using Playbook.Core.Replay;
using Playbook.Core.Research;
using Playbook.Infrastructure.Research;

namespace Playbook.Tests;

public class ResearchWorkbenchTests
{
    [Fact]
    public void Parser_Parses_Eval_Args()
    {
        var req = ResearchCliParser.Parse([
            "eval",
            "--seasons", "2015,2018,2021",
            "--type", "startsit",
            "--mode", "baseline",
            "--universe", "expanded",
            "--scope", "development"
        ]);

        Assert.Equal(ResearchCommandKind.Eval, req.Command);
        Assert.Equal(new[] { 2015, 2018, 2021 }, req.Seasons);
        Assert.Equal(ResearchPredictionSurface.StartSit, req.PredictionSurface);
        Assert.Equal(ResearchKnowledgeModeLabel.Baseline, req.KnowledgeMode);
        Assert.Equal(HistoricalCandidateUniverse.ExpandedSkillUniverse, req.CandidateUniverse);
        Assert.Equal(ResearchScopeMode.Development, req.ScopeMode);
        Assert.False(req.AllowHoldout);
    }

    [Fact]
    public void Parser_Parses_Season_Ranges()
    {
        var seasons = ResearchCliParser.ParseSeasonList("2015-2017,2021");
        Assert.Equal(new[] { 2015, 2016, 2017, 2021 }, seasons);
    }

    [Fact]
    public void Holdout_Guard_Rejects_Development_Scope_With_2024()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResearchHoldoutGuard.ValidateSeasonScope(
                [2015, 2024],
                ResearchScopeMode.Development,
                allowHoldout: false,
                isFittingOrParameterSelection: false));
        Assert.Contains("2024", ex.Message);
    }

    [Fact]
    public void Holdout_Guard_Rejects_Fitting_On_Holdout()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResearchHoldoutGuard.ValidateSeasonScope(
                [2024],
                ResearchScopeMode.Holdout,
                allowHoldout: true,
                isFittingOrParameterSelection: true));
        Assert.Contains("fit/select", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Holdout_Guard_Requires_Allow_Flag()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResearchHoldoutGuard.ValidateSeasonScope(
                [2024],
                ResearchScopeMode.Holdout,
                allowHoldout: false,
                isFittingOrParameterSelection: false));
        Assert.Contains("--allow-holdout", ex.Message);
    }

    [Fact]
    public void Holdout_Guard_Allows_Explicit_Holdout()
    {
        ResearchHoldoutGuard.ValidateSeasonScope(
            [2024],
            ResearchScopeMode.Holdout,
            allowHoldout: true,
            isFittingOrParameterSelection: false);
    }

    [Fact]
    public void Holdout_Guard_Rejects_AdHoc_Enhanced()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ResearchHoldoutGuard.ValidateExperimentNotSilentlyMutatingProduction(
                ResearchKnowledgeModeLabel.Enhanced,
                experimentId: null));
        Assert.Contains("catalog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunStore_Refuses_Overwrite_And_Is_Deterministic_About_Paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ResearchRunStore(root);
            var dir = store.CreateRunDirectory("unit", DateTimeOffset.UtcNow);
            store.WriteText(dir, "a.txt", "one");
            Assert.Throws<InvalidOperationException>(() => store.WriteText(dir, "a.txt", "two"));

            var manifest = new ResearchRunManifest
            {
                RunId = Path.GetFileName(dir),
                Command = "Eval",
                TimestampUtc = DateTimeOffset.UtcNow,
                GitSha = "deadbeef",
                GitBranch = "test",
                WorkingDirectory = root,
                Argv = ["eval"],
                Seasons = [2018],
                ScopeMode = ResearchScopeMode.Development,
                AllowHoldout = false,
                UsedHoldoutForFitting = false,
                PredictionSurface = ResearchPredictionSurface.StartSit,
                KnowledgeMode = ResearchKnowledgeModeLabel.Baseline,
                CandidateUniverse = HistoricalCandidateUniverse.LabRoster,
                ExperimentId = null,
                Seed = 42,
                OutputDirectory = dir,
                ProductionKnowledgeMode = "Passthrough",
                RejectedTransforms = ResearchIntegrity.RejectedKnowledgeTransforms.ToList(),
                Verdict = null,
                ArtifactPaths = new Dictionary<string, string>(),
                Notes = new Dictionary<string, string>()
            };
            store.WriteManifest(dir, manifest);
            var loaded = ResearchRunStore.LoadManifest(dir);
            Assert.Equal("deadbeef", loaded.GitSha);
            Assert.Equal(42, loaded.Seed);
            Assert.Equal(KnowledgeMode.Passthrough.ToString(), loaded.ProductionKnowledgeMode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Inspect_Is_Cutoff_Safe_And_Restores_Passthrough()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var wb = new ResearchWorkbench(Directory.GetCurrentDirectory(), root);
            var code = await wb.ExecuteAsync(new ResearchCliRequest
            {
                Command = ResearchCommandKind.Inspect,
                RawArgv = ["inspect", "--season", "2018", "--week", "7", "--universe", "expanded"],
                InspectSeason = 2018,
                InspectWeek = 7,
                CandidateUniverse = HistoricalCandidateUniverse.ExpandedSkillUniverse
            });
            Assert.Equal(0, code);

            var runDirs = Directory.GetDirectories(root);
            Assert.Single(runDirs);
            var manifest = ResearchRunStore.LoadManifest(runDirs[0]);
            Assert.Equal("Passthrough", manifest.ProductionKnowledgeMode);
            Assert.Contains("inspect.md", Directory.GetFiles(runDirs[0]).Select(Path.GetFileName));
            Assert.False(manifest.UsedHoldoutForFitting);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Inspect_Holdout_Requires_Allow_Flag()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var wb = new ResearchWorkbench(Directory.GetCurrentDirectory(), root);
            await Assert.ThrowsAsync<InvalidOperationException>(() => wb.ExecuteAsync(new ResearchCliRequest
            {
                Command = ResearchCommandKind.Inspect,
                RawArgv = ["inspect", "--season", "2024"],
                InspectSeason = 2024,
                InspectWeek = 7,
                AllowHoldout = false
            }));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Eval_Development_Rejects_Holdout_Season()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var wb = new ResearchWorkbench(Directory.GetCurrentDirectory(), root);
            await Assert.ThrowsAsync<InvalidOperationException>(() => wb.ExecuteAsync(new ResearchCliRequest
            {
                Command = ResearchCommandKind.Eval,
                RawArgv = ["eval", "--seasons", "2024"],
                Seasons = [2024],
                ScopeMode = ResearchScopeMode.Development,
                PredictionSurface = ResearchPredictionSurface.StartSit,
                KnowledgeMode = ResearchKnowledgeModeLabel.Baseline
            }));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Simulate_DryRun_Does_Not_Create_Run_Directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var wb = new ResearchWorkbench(Directory.GetCurrentDirectory(), root);
            var code = await wb.ExecuteAsync(new ResearchCliRequest
            {
                Command = ResearchCommandKind.Simulate,
                RawArgv = ["simulate", "--seasons", "2018", "--dry-run"],
                Seasons = [2018],
                DryRun = true,
                PredictionSurface = ResearchPredictionSurface.StartSit
            });
            Assert.Equal(0, code);
            Assert.False(Directory.Exists(root) && Directory.GetDirectories(root).Length > 0);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Compare_Two_Manifests_Produces_Markdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ResearchRunStore(root);
            var a = store.CreateRunDirectory("a", DateTimeOffset.UtcNow);
            var b = store.CreateRunDirectory("b", DateTimeOffset.UtcNow);
            WriteMiniRun(store, a, "A", tot: 10, mae: 5);
            WriteMiniRun(store, b, "B", tot: 12, mae: 4);

            var wb = new ResearchWorkbench(Directory.GetCurrentDirectory(), root);
            var code = await wb.ExecuteAsync(new ResearchCliRequest
            {
                Command = ResearchCommandKind.Compare,
                RawArgv = ["compare", "--a", Path.GetFileName(a), "--b", Path.GetFileName(b)],
                CompareLeft = Path.GetFileName(a),
                CompareRight = Path.GetFileName(b)
            });
            Assert.Equal(0, code);
            var compareDirs = Directory.GetDirectories(root)
                .Where(d => Path.GetFileName(d).Contains("compare", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.NotEmpty(compareDirs);
            var md = File.ReadAllText(Path.Combine(compareDirs[0], "comparison.md"));
            Assert.Contains("Research run comparison", md);
            Assert.Contains("Start/Sit total DV", md);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Integrity_Constants_Match_Research_Conclusions()
    {
        Assert.Equal(2024, ResearchIntegrity.HoldoutSeason);
        Assert.Equal(KnowledgeMode.Passthrough, ResearchIntegrity.ProductionKnowledgeMode);
        Assert.Contains("Usage", ResearchIntegrity.RejectedKnowledgeTransforms);
        Assert.Contains("RecentForm", ResearchIntegrity.RejectedKnowledgeTransforms);
        Assert.Contains("DataSufficiencyTrust", ResearchIntegrity.RejectedKnowledgeTransforms);
        Assert.True(ResearchIntegrity.ExperimentCatalog.ContainsKey("shared-knowledge-expanded-universe-v1"));
        Assert.Equal(KnowledgeMode.Passthrough, new KnowledgeImpactExperimentState().Mode);
    }

    [Fact]
    public async Task Eval_Week_Probe_Is_Repeatable_Same_Metrics()
    {
        var root = Path.Combine(Path.GetTempPath(), "playbook-research-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var wb = new ResearchWorkbench(Directory.GetCurrentDirectory(), root);
            // Single-season eval — still full season, but deterministic across two runs.
            var req = new ResearchCliRequest
            {
                Command = ResearchCommandKind.Eval,
                RawArgv = ["eval", "--seasons", "2018", "--type", "startsit", "--universe", "lab"],
                Seasons = [2018],
                PredictionSurface = ResearchPredictionSurface.StartSit,
                KnowledgeMode = ResearchKnowledgeModeLabel.Baseline,
                CandidateUniverse = HistoricalCandidateUniverse.LabRoster,
                ScopeMode = ResearchScopeMode.Development
            };
            Assert.Equal(0, await wb.ExecuteAsync(req));
            Assert.Equal(0, await wb.ExecuteAsync(req));

            var runs = Directory.GetDirectories(root).OrderBy(d => d).ToArray();
            Assert.Equal(2, runs.Length);
            var m1 = ResearchRunStore.TryLoadMetrics(runs[0]);
            var m2 = ResearchRunStore.TryLoadMetrics(runs[1]);
            Assert.NotNull(m1);
            Assert.NotNull(m2);
            Assert.Equal(m1!.StartSitTotalDecisionValue, m2!.StartSitTotalDecisionValue);
            Assert.Equal(m1.StartSitAccuracyPercent, m2.StartSitAccuracyPercent);
            Assert.NotEqual(m1.RunId, m2.RunId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteMiniRun(ResearchRunStore store, string dir, string id, double tot, double mae)
    {
        store.WriteManifest(dir, new ResearchRunManifest
        {
            RunId = Path.GetFileName(dir),
            Command = "Eval",
            TimestampUtc = DateTimeOffset.UtcNow,
            GitSha = id,
            GitBranch = "test",
            WorkingDirectory = dir,
            Argv = ["eval"],
            Seasons = [2018],
            ScopeMode = ResearchScopeMode.Development,
            AllowHoldout = false,
            UsedHoldoutForFitting = false,
            PredictionSurface = ResearchPredictionSurface.StartSit,
            KnowledgeMode = ResearchKnowledgeModeLabel.Baseline,
            CandidateUniverse = HistoricalCandidateUniverse.LabRoster,
            ExperimentId = null,
            Seed = null,
            OutputDirectory = dir,
            ProductionKnowledgeMode = "Passthrough",
            RejectedTransforms = ResearchIntegrity.RejectedKnowledgeTransforms.ToList(),
            Verdict = null,
            ArtifactPaths = new Dictionary<string, string>(),
            Notes = new Dictionary<string, string>()
        });
        store.WriteJson(dir, "metrics.json", new ResearchRunMetrics
        {
            RunId = Path.GetFileName(dir),
            Surface = "StartSit",
            StartSitGraded = 10,
            StartSitAccuracyPercent = 50,
            StartSitTotalDecisionValue = tot,
            StartSitProjectionMae = mae,
            PerSeasonTotalDecisionValue = new Dictionary<int, double> { [2018] = tot }
        });
    }
}
