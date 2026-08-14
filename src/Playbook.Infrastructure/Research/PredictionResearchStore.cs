using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Research;
using Playbook.Core.Research;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// File-backed permanent research memory. Two append-only JSON documents (snapshots, assessments)
/// under the same persistent-volume convention as <c>LeagueUserTeamStore</c> — survives redeploys.
/// Never overwrites an existing record; a duplicate save for the same id is silently ignored so
/// history can never be rewritten.
/// </summary>
public sealed class PredictionResearchStore : IPredictionResearchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<PredictionResearchStore> _logger;
    private readonly string _snapshotsPath;
    private readonly string _assessmentsPath;
    private readonly object _gate = new();

    public PredictionResearchStore(ILogger<PredictionResearchStore> logger, string? rootOverride = null)
    {
        _logger = logger;
        // PLAYBOOK_DATA_DIR points at a mounted persistent volume in production (see fly.toml) so
        // permanent research memory survives redeploys, unlike the short-TTL caches under
        // AppContext.BaseDirectory/data.
        var configuredRoot = rootOverride ?? Environment.GetEnvironmentVariable("PLAYBOOK_DATA_DIR");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : configuredRoot;
        var researchRoot = Path.Combine(root, "research");
        Directory.CreateDirectory(researchRoot);
        _snapshotsPath = Path.Combine(researchRoot, "prediction-snapshots.json");
        _assessmentsPath = Path.Combine(researchRoot, "prediction-assessments.json");
    }

    public void SaveSnapshot(PredictionSnapshot snapshot)
    {
        lock (_gate)
        {
            var all = LoadList<PredictionSnapshot>(_snapshotsPath);
            if (all.Any(s => s.SnapshotId == snapshot.SnapshotId))
            {
                return;
            }

            all.Add(snapshot);
            SaveList(_snapshotsPath, all);
        }
    }

    public IReadOnlyList<PredictionSnapshot> GetAllSnapshots()
    {
        lock (_gate)
        {
            return LoadList<PredictionSnapshot>(_snapshotsPath);
        }
    }

    public IReadOnlyList<PredictionSnapshot> GetSnapshotsPendingGrading(DateTimeOffset asOf, TimeSpan gradingBuffer)
    {
        lock (_gate)
        {
            var snapshots = LoadList<PredictionSnapshot>(_snapshotsPath);
            var graded = LoadList<PredictionOutcomeAssessment>(_assessmentsPath)
                .Select(a => a.SnapshotId)
                .ToHashSet();

            return snapshots
                .Where(s => !graded.Contains(s.SnapshotId) && asOf - s.CommenceTime >= gradingBuffer)
                .ToList();
        }
    }

    public void SaveAssessment(PredictionOutcomeAssessment assessment)
    {
        lock (_gate)
        {
            var all = LoadList<PredictionOutcomeAssessment>(_assessmentsPath);
            if (all.Any(a => a.SnapshotId == assessment.SnapshotId))
            {
                return;
            }

            all.Add(assessment);
            SaveList(_assessmentsPath, all);
        }
    }

    public IReadOnlyList<PredictionOutcomeAssessment> GetAllAssessments()
    {
        lock (_gate)
        {
            return LoadList<PredictionOutcomeAssessment>(_assessmentsPath);
        }
    }

    public bool HasAssessment(Guid snapshotId)
    {
        lock (_gate)
        {
            return LoadList<PredictionOutcomeAssessment>(_assessmentsPath).Any(a => a.SnapshotId == snapshotId);
        }
    }

    private List<T> LoadList<T>(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read research store at {Path}", path);
            return [];
        }
    }

    private void SaveList<T>(string path, List<T> items)
    {
        var json = JsonSerializer.Serialize(items, JsonOptions);
        File.WriteAllText(path, json);
    }
}
