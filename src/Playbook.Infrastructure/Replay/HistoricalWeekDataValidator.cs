using Playbook.Application.Replay;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Loud validation before a historical replay may run. Failures are exceptions, not silent fixes.
/// </summary>
public sealed class HistoricalWeekDataValidator : IHistoricalWeekDataValidator
{
    public void ValidateOrThrow(HistoricalRawWeekData raw)
    {
        if (raw.Season < 2000 || raw.Season > 2100)
        {
            throw new InvalidOperationException($"Invalid season {raw.Season}.");
        }

        if (raw.Week is < 1 or > 22)
        {
            throw new InvalidOperationException($"Invalid week {raw.Week}.");
        }

        if (raw.InformationCutoff == default)
        {
            throw new InvalidOperationException("Information cutoff is required.");
        }

        if (raw.Players.Count == 0)
        {
            throw new InvalidOperationException("Historical week has no players.");
        }

        if (raw.Roster.Count == 0)
        {
            throw new InvalidOperationException("Historical week has no roster slots.");
        }

        if (raw.Outcomes.Count == 0)
        {
            throw new InvalidOperationException("Historical week is missing segregated actual outcomes.");
        }

        var playerIds = new HashSet<Guid>();
        foreach (var player in raw.Players)
        {
            if (player.PlayerId == Guid.Empty)
            {
                throw new InvalidOperationException($"Player '{player.PlayerName}' has empty id.");
            }

            if (!playerIds.Add(player.PlayerId))
            {
                throw new InvalidOperationException($"Duplicate player identity {player.PlayerId} ({player.PlayerName}).");
            }

            if (string.IsNullOrWhiteSpace(player.PlayerName))
            {
                throw new InvalidOperationException("Player name missing.");
            }

            if (string.IsNullOrWhiteSpace(player.Team))
            {
                throw new InvalidOperationException($"Player '{player.PlayerName}' missing team.");
            }

            // Future-dated observations in raw are allowed (builder strips them),
            // but Week outcomes must never be embedded as projections.
            if (player.ProjectedPoints is not null &&
                player.UnavailableSignals.Any(s => s.Contains("projection", StringComparison.OrdinalIgnoreCase) &&
                                                   s.Contains("unavailable", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Player '{player.PlayerName}' marks projection unavailable but still has ProjectedPoints.");
            }
        }

        foreach (var slot in raw.Roster)
        {
            if (!playerIds.Contains(slot.PlayerId))
            {
                throw new InvalidOperationException($"Roster references unknown player {slot.PlayerId}.");
            }
        }

        foreach (var outcome in raw.Outcomes)
        {
            if (!playerIds.Contains(outcome.PlayerId))
            {
                throw new InvalidOperationException($"Outcome references unknown player {outcome.PlayerId}.");
            }
        }

        // Determinism helper: sorted player ids must be unique and stable ordering is caller's job.
        if (raw.Players.Select(p => p.PlayerId).Distinct().Count() != raw.Players.Count)
        {
            throw new InvalidOperationException("Player id set is not unique.");
        }
    }
}
