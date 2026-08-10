using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Stats;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

/// <summary>
/// JSON file cache for college season statistics (separate from NFL stats cache).
/// </summary>
public sealed class CollegeStatsCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CollegeStatsOptions _options;
    private readonly ILogger<CollegeStatsCacheStore> _logger;
    private readonly string _cachePath;

    public CollegeStatsCacheStore(
        IOptions<CollegeStatsOptions> options,
        ILogger<CollegeStatsCacheStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        var root = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(root);
        _cachePath = Path.Combine(root, string.IsNullOrWhiteSpace(_options.CacheFileName)
            ? "college-stats-cache.json"
            : _options.CacheFileName);
    }

    public string CachePath => _cachePath;

    public bool TryLoadFresh(out CollegeStatsCacheDocument document)
    {
        document = new CollegeStatsCacheDocument();
        if (!File.Exists(_cachePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<CollegeStatsCacheDocument>(json, JsonOptions);
            if (loaded is null)
            {
                return false;
            }

            var ttl = TimeSpan.FromMinutes(Math.Clamp(_options.CacheTtlMinutes, 5, 14 * 24 * 60));
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
            _logger.LogWarning(ex, "Failed to read college stats cache at {Path}", _cachePath);
            return false;
        }
    }

    public void Save(CollegeStatsCacheDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_cachePath, json);
        _logger.LogInformation(
            "College stats cache written ({Count} records) to {Path}",
            document.Records.Count,
            _cachePath);
    }
}

public sealed class CollegeStatsCacheDocument
{
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public string Provider { get; set; } = string.Empty;

    public List<PlayerSeasonStats> Records { get; set; } = [];
}
