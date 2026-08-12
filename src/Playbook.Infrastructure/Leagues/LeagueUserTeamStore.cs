using System.Text.Json;
using Microsoft.Extensions.Logging;
using Playbook.Application.Leagues;

namespace Playbook.Infrastructure.Leagues;

/// <summary>
/// JSON file store for the user's selected roster id per league key.
/// </summary>
public sealed class LeagueUserTeamStore : ILeagueUserTeamStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<LeagueUserTeamStore> _logger;
    private readonly string _path;
    private readonly object _gate = new();

    public LeagueUserTeamStore(ILogger<LeagueUserTeamStore> logger, string? fileName = null)
    {
        _logger = logger;
        // PLAYBOOK_DATA_DIR points at a mounted persistent volume in production (see fly.toml) so
        // connected leagues survive redeploys; falls back to the app's own directory locally.
        var configuredRoot = Environment.GetEnvironmentVariable("PLAYBOOK_DATA_DIR");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : configuredRoot;
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, string.IsNullOrWhiteSpace(fileName)
            ? "league-user-teams.json"
            : fileName);
    }

    public string StorePath => _path;

    public bool TryGetSelectedRosterId(string leagueKey, out int rosterId)
    {
        rosterId = 0;
        if (string.IsNullOrWhiteSpace(leagueKey))
        {
            return false;
        }

        lock (_gate)
        {
            var doc = Load();
            if (doc.Selections.TryGetValue(leagueKey.Trim(), out var saved) && saved > 0)
            {
                rosterId = saved;
                return true;
            }
        }

        return false;
    }

    public void SaveSelectedRosterId(string leagueKey, int rosterId)
    {
        if (string.IsNullOrWhiteSpace(leagueKey) || rosterId <= 0)
        {
            return;
        }

        lock (_gate)
        {
            var doc = Load();
            doc.Selections[leagueKey.Trim()] = rosterId;
            doc.LastUpdatedUtc = DateTimeOffset.UtcNow;
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(doc, JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist league user-team selection at {Path}", _path);
            }
        }
    }

    public IReadOnlyList<string> GetConnectedExternalLeagueIds()
    {
        lock (_gate)
        {
            return Load().ConnectedExternalLeagueIds.ToList();
        }
    }

    public void SaveConnectedExternalLeagueId(string externalLeagueId)
    {
        if (string.IsNullOrWhiteSpace(externalLeagueId))
        {
            return;
        }

        var trimmed = externalLeagueId.Trim();
        lock (_gate)
        {
            var doc = Load();
            if (!doc.ConnectedExternalLeagueIds.Contains(trimmed, StringComparer.Ordinal))
            {
                doc.ConnectedExternalLeagueIds.Add(trimmed);
            }

            doc.LastUpdatedUtc = DateTimeOffset.UtcNow;
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(doc, JsonOptions));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist connected league id at {Path}", _path);
            }
        }
    }

    private LeagueUserTeamDocument Load()
    {
        if (!File.Exists(_path))
        {
            return new LeagueUserTeamDocument();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<LeagueUserTeamDocument>(json, JsonOptions)
                   ?? new LeagueUserTeamDocument();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read league user-team store at {Path}", _path);
            return new LeagueUserTeamDocument();
        }
    }

    private sealed class LeagueUserTeamDocument
    {
        public Dictionary<string, int> Selections { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> ConnectedExternalLeagueIds { get; set; } = [];

        public DateTimeOffset LastUpdatedUtc { get; set; }
    }
}
