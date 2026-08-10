using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Stats;
using Playbook.Core.Stats.Models;

namespace Playbook.Infrastructure.Stats;

public sealed class PlayerGameLogCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PlayerStatsOptions _options;
    private readonly ILogger<PlayerGameLogCacheStore> _logger;
    private readonly string _cachePath;

    public PlayerGameLogCacheStore(
        IOptions<PlayerStatsOptions> options,
        ILogger<PlayerGameLogCacheStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        var root = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(root);
        _cachePath = Path.Combine(root, string.IsNullOrWhiteSpace(_options.GameLogCacheFileName)
            ? "player-game-logs-cache.json"
            : _options.GameLogCacheFileName);
    }

    public bool TryLoad(out PlayerGameLogCacheDocument document)
    {
        document = new PlayerGameLogCacheDocument();
        if (!File.Exists(_cachePath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_cachePath);
            var loaded = JsonSerializer.Deserialize<PlayerGameLogCacheDocument>(json, JsonOptions);
            if (loaded is null)
            {
                return false;
            }

            document = loaded;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read game-log cache at {Path}", _cachePath);
            return false;
        }
    }

    public void Save(PlayerGameLogCacheDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_cachePath, json);
        _logger.LogInformation(
            "Game-log cache written ({Count} rows) to {Path}",
            document.GameLogs.Count,
            _cachePath);
    }
}

public sealed class PlayerGameLogCacheDocument
{
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public string Provider { get; set; } = string.Empty;

    public List<int> Seasons { get; set; } = [];

    public List<PlayerGameStats> GameLogs { get; set; } = [];
}
