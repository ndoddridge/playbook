using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Historical;
using Playbook.Core.Historical;

namespace Playbook.Infrastructure.Historical;

/// <summary>Small file-backed store on the same PLAYBOOK_DATA_DIR persistent volume as league settings.</summary>
public sealed class HistoricalLeagueDraftStore : IHistoricalLeagueDraftStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ILogger<HistoricalLeagueDraftStore> _logger;
    private readonly object _gate = new();
    public string StorePath { get; }

    public HistoricalLeagueDraftStore(ILogger<HistoricalLeagueDraftStore> logger, string? fileName = null)
    {
        _logger = logger;
        var root = Environment.GetEnvironmentVariable("PLAYBOOK_DATA_DIR");
        root = string.IsNullOrWhiteSpace(root) ? Path.Combine(AppContext.BaseDirectory, "data") : root;
        Directory.CreateDirectory(root);
        StorePath = Path.Combine(root, fileName ?? "historical-league-drafts.json");
    }

    public IReadOnlyList<HistoricalLeagueDraft> Load()
    {
        lock (_gate)
        {
            try { return File.Exists(StorePath) ? JsonSerializer.Deserialize<List<HistoricalLeagueDraft>>(File.ReadAllText(StorePath), Options) ?? [] : []; }
            catch (Exception ex) { _logger.LogWarning(ex, "Unable to load historical league drafts from {Path}", StorePath); return []; }
        }
    }

    public void Save(IReadOnlyList<HistoricalLeagueDraft> drafts)
    {
        lock (_gate)
        {
            try { File.WriteAllText(StorePath, JsonSerializer.Serialize(drafts, Options)); }
            catch (Exception ex) { _logger.LogWarning(ex, "Unable to persist historical league drafts to {Path}", StorePath); throw; }
        }
    }
}
