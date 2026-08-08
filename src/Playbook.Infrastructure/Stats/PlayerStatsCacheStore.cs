using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Stats;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// Simple JSON file cache for normalized player season stats.
/// Supports initial sync, reuse, and refresh without a database.
/// </summary>
public sealed class PlayerStatsCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PlayerStatsOptions _options;
    private readonly ILogger<PlayerStatsCacheStore> _logger;
    private readonly string _cachePath;

    public PlayerStatsCacheStore(
        IOptions<PlayerStatsOptions> options,
        ILogger<PlayerStatsCacheStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        var root = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(root);
        _cachePath = Path.Combine(root, string.IsNullOrWhiteSpace(_options.CacheFileName)
            ? "player-stats-cache.json"
            : _options.CacheFileName);
    }

    public string CachePath => _cachePath;

    public bool TryLoadFresh(out PlayerStatsCacheDocument document)
    {
        document = new PlayerStatsCacheDocument();
        if (!File.Exists(_cachePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<PlayerStatsCacheDocument>(json, JsonOptions);
            if (loaded is null || loaded.Records.Count == 0)
            {
                return false;
            }

            var ttl = TimeSpan.FromMinutes(Math.Clamp(_options.CacheTtlMinutes, 5, 24 * 60));
            if (DateTimeOffset.UtcNow - loaded.LastUpdatedUtc > ttl)
            {
                document = loaded;
                return false;
            }

            document = loaded;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read player stats cache at {Path}", _cachePath);
            return false;
        }
    }

    public PlayerStatsCacheDocument? TryLoadAny()
    {
        if (!File.Exists(_cachePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<PlayerStatsCacheDocument>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read player stats cache at {Path}", _cachePath);
            return null;
        }
    }

    public void Save(PlayerStatsCacheDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_cachePath, json);
        _logger.LogInformation(
            "Player stats cache written ({Count} records) to {Path}",
            document.Records.Count,
            _cachePath);
    }
}

public sealed class PlayerStatsCacheDocument
{
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public string Provider { get; set; } = string.Empty;

    public int CurrentSeason { get; set; }

    public List<int> Seasons { get; set; } = [];

    public List<PlayerSeasonStats> Records { get; set; } = [];
}
