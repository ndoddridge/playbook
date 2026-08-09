using System.Security.Cryptography;
using System.Text;
using Playbook.Application.Replay;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// GSIS-stable historical identity mapping. Names alone never produce a PlaybookId.
/// </summary>
public sealed class HistoricalPlayerIdentityNormalizer : IHistoricalPlayerIdentityNormalizer
{
    public Guid PlaybookIdFromGsis(string gsisId)
    {
        if (string.IsNullOrWhiteSpace(gsisId))
        {
            throw new ArgumentException("GSIS id is required for historical identity.", nameof(gsisId));
        }

        var normalized = gsisId.Trim();
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"playbook:gsis:nfl:{normalized}"));
        return new Guid(bytes);
    }

    public HistoricalPlayerIdentity Normalize(
        string gsisId,
        string fullName,
        string position,
        string team,
        int season,
        int week,
        string? sleeperId = null,
        string? espnId = null,
        string? yahooId = null,
        string? rosterStatus = null)
    {
        if (string.IsNullOrWhiteSpace(gsisId))
        {
            throw new ArgumentException("Cannot normalize historical player without GSIS id.", nameof(gsisId));
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(team))
        {
            throw new ArgumentException("Team is required.", nameof(team));
        }

        return new HistoricalPlayerIdentity
        {
            PlaybookId = PlaybookIdFromGsis(gsisId),
            GsisId = gsisId.Trim(),
            FullName = fullName.Trim(),
            Position = ParsePosition(position),
            Team = team.Trim().ToUpperInvariant(),
            Season = season,
            Week = week,
            SleeperId = NullIfEmpty(sleeperId),
            EspnId = NullIfEmpty(espnId),
            YahooId = NullIfEmpty(yahooId),
            RosterStatus = NullIfEmpty(rosterStatus)
        };
    }

    public static Position ParsePosition(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException("Position is required.", nameof(raw));
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            "QB" => Position.QB,
            "RB" or "FB" or "HB" => Position.RB,
            "WR" => Position.WR,
            "TE" => Position.TE,
            "K" or "PK" => Position.K,
            "DEF" or "DST" or "D/ST" => Position.DST,
            _ => throw new ArgumentException($"Unsupported or non-skill historical position '{raw}'.", nameof(raw))
        };
    }

    public static bool IsSkillPosition(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        return raw.Trim().ToUpperInvariant() is "QB" or "RB" or "FB" or "HB" or "WR" or "TE";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
