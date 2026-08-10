using Playbook.Core.Replay;
using Playbook.Core.Research;

namespace Playbook.Infrastructure.Research;

/// <summary>Minimal argv parser — no extra packages.</summary>
public static class ResearchCliParser
{
    public static ResearchCliRequest Parse(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            throw new ResearchCliUsageException(HelpText());
        }

        var commandToken = args[0].Trim().ToLowerInvariant();
        var command = commandToken switch
        {
            "test" => ResearchCommandKind.Test,
            "eval" or "evaluate" => ResearchCommandKind.Eval,
            "experiment" => ResearchCommandKind.Experiment,
            "simulate" or "sim" => ResearchCommandKind.Simulate,
            "compare" => ResearchCommandKind.Compare,
            "inspect" => ResearchCommandKind.Inspect,
            "list-experiments" => ResearchCommandKind.ListExperiments,
            _ => throw new ResearchCliUsageException($"Unknown command '{args[0]}'.\n\n{HelpText()}")
        };

        var seasons = new List<int>();
        int? seasonCount = null;
        var surface = ResearchPredictionSurface.Both;
        var mode = ResearchKnowledgeModeLabel.Baseline;
        var universe = HistoricalCandidateUniverse.LabRoster;
        var scope = ResearchScopeMode.Development;
        var allowHoldout = false;
        var dryRun = false;
        string? experimentId = null;
        string? compareLeft = null;
        string? compareRight = null;
        int? inspectSeason = null;
        int? inspectWeek = null;
        int? seed = null;
        string? outputRoot = null;

        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--list" when command == ResearchCommandKind.Experiment:
                    command = ResearchCommandKind.ListExperiments;
                    break;
                case "--seasons":
                    EnsureValue(args, i, "--seasons");
                    var raw = args[++i];
                    if (int.TryParse(raw, out var countOnly) && !raw.Contains('-') && !raw.Contains(','))
                    {
                        // Ambiguous: treat bare integer as season count for simulate convenience,
                        // or as a single season for eval. Prefer single-season if command is eval/inspect.
                        if (command is ResearchCommandKind.Simulate)
                        {
                            seasonCount = countOnly;
                        }
                        else
                        {
                            seasons.Add(countOnly);
                        }
                    }
                    else
                    {
                        seasons.AddRange(ParseSeasonList(raw));
                    }

                    break;
                case "--season":
                    EnsureValue(args, i, "--season");
                    inspectSeason = int.Parse(args[++i]);
                    if (command is ResearchCommandKind.Eval or ResearchCommandKind.Simulate)
                    {
                        seasons.Add(inspectSeason.Value);
                    }

                    break;
                case "--week":
                    EnsureValue(args, i, "--week");
                    inspectWeek = int.Parse(args[++i]);
                    break;
                case "--type" or "--surface":
                    EnsureValue(args, i, "--type");
                    surface = ParseSurface(args[++i]);
                    break;
                case "--mode":
                    EnsureValue(args, i, "--mode");
                    mode = ParseMode(args[++i]);
                    break;
                case "--universe":
                    EnsureValue(args, i, "--universe");
                    universe = ParseUniverse(args[++i]);
                    break;
                case "--scope":
                    EnsureValue(args, i, "--scope");
                    scope = ParseScope(args[++i]);
                    break;
                case "--allow-holdout":
                    allowHoldout = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--experiment" or "--id":
                    EnsureValue(args, i, "--experiment");
                    experimentId = args[++i];
                    break;
                case "--a":
                    EnsureValue(args, i, "--a");
                    compareLeft = args[++i];
                    break;
                case "--b":
                    EnsureValue(args, i, "--b");
                    compareRight = args[++i];
                    break;
                case "--seed":
                    EnsureValue(args, i, "--seed");
                    seed = int.Parse(args[++i]);
                    break;
                case "--out" or "--output":
                    EnsureValue(args, i, "--out");
                    outputRoot = args[++i];
                    break;
                case "--help" or "-h":
                    throw new ResearchCliUsageException(HelpText());
                default:
                    throw new ResearchCliUsageException($"Unknown argument '{a}'.\n\n{HelpText()}");
            }
        }

        // experiment without --id but with leftover? require --id unless listing
        if (command == ResearchCommandKind.Experiment && string.IsNullOrWhiteSpace(experimentId))
        {
            throw new ResearchCliUsageException(
                "experiment requires --id <catalog-id> or --list.\n\n" + HelpText());
        }

        return new ResearchCliRequest
        {
            Command = command,
            RawArgv = args.ToList(),
            Seasons = seasons,
            SeasonCount = seasonCount,
            PredictionSurface = surface,
            KnowledgeMode = mode,
            CandidateUniverse = universe,
            ScopeMode = scope,
            AllowHoldout = allowHoldout,
            DryRun = dryRun,
            ExperimentId = experimentId,
            CompareLeft = compareLeft,
            CompareRight = compareRight,
            InspectSeason = inspectSeason,
            InspectWeek = inspectWeek,
            Seed = seed,
            OutputRoot = outputRoot
        };
    }

    public static string HelpText() =>
        """
        Playbook Research Workbench

        Usage:
          dotnet run --project src/Playbook.Research -- <command> [options]

        Commands:
          test
          eval --seasons 2015,2018,2021 --type startsit|quickpicks|both --mode baseline|passthrough --universe lab|expanded --scope development|holdout|mixed
          experiment --id <catalog-id>
          experiment --list
          simulate --seasons 2015,2018,2021 [--seasons 20] --type both --mode baseline --universe expanded [--seed N] [--dry-run] [--allow-holdout]
          compare --a <runId|path> --b <runId|path>
          inspect --season 2018 --week 7 --universe expanded

        Safety:
          Holdout season 2024 requires --allow-holdout.
          Development scope rejects 2024.
          Production KnowledgeMode remains Passthrough after every run.
          Run directories under research-runs/ are never overwritten.

        See docs/RESEARCH_WORKBENCH.md
        """;

    private static void EnsureValue(string[] args, int i, string name)
    {
        if (i + 1 >= args.Length)
        {
            throw new ResearchCliUsageException($"Missing value for {name}.");
        }
    }

    private static bool IsHelp(string token) =>
        token is "-h" or "--help" or "help";

    public static IReadOnlyList<int> ParseSeasonList(string raw)
    {
        var result = new List<int>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Contains('-', StringComparison.Ordinal))
            {
                var ends = part.Split('-', StringSplitOptions.TrimEntries);
                if (ends.Length != 2 ||
                    !int.TryParse(ends[0], out var start) ||
                    !int.TryParse(ends[1], out var end) ||
                    end < start)
                {
                    throw new ResearchCliUsageException($"Invalid season range '{part}'.");
                }

                result.AddRange(Enumerable.Range(start, end - start + 1));
            }
            else if (int.TryParse(part, out var season))
            {
                result.Add(season);
            }
            else
            {
                throw new ResearchCliUsageException($"Invalid season '{part}'.");
            }
        }

        return result.Distinct().OrderBy(s => s).ToList();
    }

    private static ResearchPredictionSurface ParseSurface(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "startsit" or "start-sit" or "ss" => ResearchPredictionSurface.StartSit,
            "quickpicks" or "quick-picks" or "qp" => ResearchPredictionSurface.QuickPicks,
            "both" => ResearchPredictionSurface.Both,
            _ => throw new ResearchCliUsageException($"Unknown --type '{raw}'.")
        };

    private static ResearchKnowledgeModeLabel ParseMode(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "baseline" => ResearchKnowledgeModeLabel.Baseline,
            "passthrough" or "pass-through" or "production" => ResearchKnowledgeModeLabel.Passthrough,
            "enhanced" => ResearchKnowledgeModeLabel.Enhanced,
            _ => throw new ResearchCliUsageException($"Unknown --mode '{raw}'.")
        };

    private static HistoricalCandidateUniverse ParseUniverse(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "lab" or "labroster" or "lab-roster" => HistoricalCandidateUniverse.LabRoster,
            "expanded" or "expandedskill" or "expanded-skill" => HistoricalCandidateUniverse.ExpandedSkillUniverse,
            _ => throw new ResearchCliUsageException($"Unknown --universe '{raw}'.")
        };

    private static ResearchScopeMode ParseScope(string raw) =>
        raw.Trim().ToLowerInvariant() switch
        {
            "development" or "dev" => ResearchScopeMode.Development,
            "holdout" => ResearchScopeMode.Holdout,
            "mixed" => ResearchScopeMode.Mixed,
            _ => throw new ResearchCliUsageException($"Unknown --scope '{raw}'.")
        };
}

public sealed class ResearchCliUsageException : Exception
{
    public ResearchCliUsageException(string message) : base(message)
    {
    }
}
