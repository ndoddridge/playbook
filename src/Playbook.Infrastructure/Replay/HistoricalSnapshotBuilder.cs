using Playbook.Application.Replay;
using Playbook.Core.Replay;

namespace Playbook.Infrastructure.Replay;

/// <summary>
/// Enforces the information cutoff. Future-dated observations never enter the snapshot.
/// Outcomes are returned separately and must only be consumed after decisions are recorded.
/// </summary>
public sealed class HistoricalSnapshotBuilder : IHistoricalSnapshotBuilder
{
    public (HistoricalSnapshot Snapshot, HistoricalWeekOutcomes Outcomes) Build(HistoricalRawWeekData raw)
    {
        var cutoff = raw.InformationCutoff;
        var players = new List<HistoricalPlayerState>();
        var stripped = new List<string>(raw.UnavailableSources);

        foreach (var row in raw.Players)
        {
            var unavailable = row.UnavailableSignals.ToList();

            decimal? projected = null;
            decimal? floor = null;
            decimal? ceiling = null;
            int? projectionConfidence = null;
            if (IsKnownAt(row.ProjectionObservedAt, cutoff))
            {
                projected = row.ProjectedPoints;
                floor = row.Floor;
                ceiling = row.Ceiling;
                projectionConfidence = row.ProjectionConfidence;
            }
            else if (row.ProjectedPoints is not null)
            {
                unavailable.Add("Projection (observed after information cutoff)");
                stripped.Add($"Future projection excluded for {row.PlayerName}");
            }

            string? injuryStatus = null;
            string? injuryBodyPart = null;
            DateTimeOffset? injuryObservedAt = null;
            var healthLabel = row.HealthLabel;
            if (row.InjuryStatus is not null)
            {
                if (IsKnownAt(row.InjuryObservedAt, cutoff))
                {
                    injuryStatus = row.InjuryStatus;
                    injuryBodyPart = row.InjuryBodyPart;
                    injuryObservedAt = row.InjuryObservedAt;
                }
                else
                {
                    unavailable.Add("Injury update (observed after information cutoff)");
                    stripped.Add($"Future injury excluded for {row.PlayerName}: {row.InjuryStatus}");
                    // Do not leak future designation into health label.
                    if (string.IsNullOrWhiteSpace(healthLabel) ||
                        healthLabel.Contains("out", StringComparison.OrdinalIgnoreCase))
                    {
                        healthLabel = "Healthy";
                    }
                }
            }

            string? news = null;
            DateTimeOffset? newsAt = null;
            var newsConfirmed = false;
            if (row.RecentNewsHeadline is not null)
            {
                if (IsKnownAt(row.RecentNewsObservedAt, cutoff))
                {
                    news = row.RecentNewsHeadline;
                    newsAt = row.RecentNewsObservedAt;
                    newsConfirmed = row.RecentNewsConfirmed;
                }
                else
                {
                    unavailable.Add("News (observed after information cutoff)");
                    stripped.Add($"Future news excluded for {row.PlayerName}");
                }
            }

            players.Add(new HistoricalPlayerState
            {
                PlayerId = row.PlayerId,
                PlayerName = row.PlayerName,
                Position = row.Position,
                Team = row.Team,
                ProjectedPoints = projected,
                Floor = floor,
                Ceiling = ceiling,
                ProjectionConfidence = projectionConfidence,
                OpportunityScore = row.OpportunityScore,
                UsageScore = row.UsageScore,
                HealthLabel = healthLabel,
                InjuryStatus = injuryStatus,
                InjuryBodyPart = injuryBodyPart,
                InjuryObservedAt = injuryObservedAt,
                RecentNewsHeadline = news,
                RecentNewsObservedAt = newsAt,
                RecentNewsConfirmed = newsConfirmed,
                RoleNote = row.RoleNote,
                RecentProductionScore = row.RecentProductionScore,
                UnavailableSignals = unavailable.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        var snapshot = new HistoricalSnapshot
        {
            Season = raw.Season,
            Week = raw.Week,
            InformationCutoff = cutoff,
            ScoringType = raw.ScoringType,
            LeagueName = raw.LeagueName,
            LeagueId = raw.LeagueId,
            SelectedRosterId = raw.SelectedRosterId,
            TeamName = raw.TeamName,
            Players = players,
            Roster = raw.Roster,
            OpponentRoster = raw.OpponentRoster,
            UnavailableSources = stripped.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceLabel = raw.SourceLabel
        };

        var outcomes = new HistoricalWeekOutcomes
        {
            Season = raw.Season,
            Week = raw.Week,
            ScoringType = raw.ScoringType,
            ByPlayerId = raw.Outcomes.ToDictionary(o => o.PlayerId)
        };

        return (snapshot, outcomes);
    }

    /// <summary>
    /// Null observed-at is treated as known fixture baseline (explicitly pre-cutoff catalog facts).
    /// Non-null timestamps must be &lt;= cutoff.
    /// </summary>
    internal static bool IsKnownAt(DateTimeOffset? observedAt, DateTimeOffset cutoff) =>
        observedAt is null || observedAt <= cutoff;
}
