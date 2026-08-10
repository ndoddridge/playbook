using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Playbook.Application.Injuries;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// JSON cache that preserves historical injury records across syncs.
/// </summary>
public sealed class InjuryCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly InjuryOptions _options;
    private readonly ILogger<InjuryCacheStore> _logger;
    private readonly string _cachePath;

    public InjuryCacheStore(IOptions<InjuryOptions> options, ILogger<InjuryCacheStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        var root = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(root);
        _cachePath = Path.Combine(root, string.IsNullOrWhiteSpace(_options.CacheFileName)
            ? "player-injuries-cache.json"
            : _options.CacheFileName);
    }

    public bool TryLoadFresh(out InjuryCacheDocument document)
    {
        document = new InjuryCacheDocument();
        var any = TryLoadAny();
        if (any is null)
        {
            return false;
        }

        var ttl = TimeSpan.FromMinutes(Math.Clamp(_options.CacheTtlMinutes, 5, 24 * 60));
        if (DateTimeOffset.UtcNow - any.LastUpdatedUtc > ttl)
        {
            document = any;
            return false;
        }

        document = any;
        return true;
    }

    public InjuryCacheDocument? TryLoadAny()
    {
        if (!File.Exists(_cachePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<InjuryCacheDocument>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read injury cache at {Path}", _cachePath);
            return null;
        }
    }

    public void Save(InjuryCacheDocument document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        File.WriteAllText(_cachePath, json);
        _logger.LogInformation(
            "Injury cache written ({Count} records) to {Path}",
            document.Records.Count,
            _cachePath);
    }
}

public sealed class InjuryCacheDocument
{
    public DateTimeOffset LastUpdatedUtc { get; set; }

    public string Provider { get; set; } = string.Empty;

    public List<PlayerInjuryRecord> Records { get; set; } = [];

    public string HistoricalDataStatus { get; set; } =
        nameof(Playbook.Core.Injuries.Models.HistoricalDataStatus.NotSynced);

    public bool SupportsHistorical { get; set; }
}
