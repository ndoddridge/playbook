using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Deterministic mock injuries for selected catalog players. Does not invent records for unknowns.
/// </summary>
public sealed class MockPlayerInjuryProvider : IPlayerInjuryProvider
{
    public InjuryProviderKind Kind => InjuryProviderKind.Mock;

    public string DisplayName => "Mock";

    public Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var rows = new List<PlayerInjuryRecord>();

        // Jayden Daniels — current questionable + prior history
        var daniels = Guid.Parse("11111111-1111-1111-1111-111111111101");
        rows.Add(Record(daniels, now.AddDays(-10), "Out", "Knee", "Missed prior week with knee soreness.", "Did Not Practice", "Out", true, now, 2025));
        rows.Add(Record(daniels, now.AddDays(-3), "Questionable", "Knee", "Limited mid-week; game-time decision.", "Limited Participant", "Questionable", true, now, 2025));

        // CMC — IR / return history
        var cmc = Guid.Parse("11111111-1111-1111-1111-111111111106");
        rows.Add(Record(cmc, now.AddDays(-40), "Injured Reserve", "Achilles", "Placed on IR.", null, "Out", false, now.AddDays(-20), 2025));
        rows.Add(Record(cmc, now.AddDays(-5), "Active", "Achilles", "Returned to full participant status.", "Full Participant", "Active", true, now, 2025));

        // Chase — no current injury (empty for this player intentionally omitted)

        // Tyreek — Out
        var tyreek = Guid.Parse("11111111-1111-1111-1111-111111111108");
        rows.Add(Record(tyreek, now.AddDays(-1), "Out", "Ankle", "Ruled out for upcoming contest.", "Out", "Out", true, now, 2025));

        // Mark current flags properly (latest per player)
        return Task.FromResult<IReadOnlyList<PlayerInjuryRecord>>(MarkCurrent(rows));
    }

    private static List<PlayerInjuryRecord> MarkCurrent(List<PlayerInjuryRecord> rows)
    {
        var latest = rows
            .GroupBy(r => r.PlayerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Date).First().Date);

        return rows.Select(r => r with { IsCurrent = latest[r.PlayerId] == r.Date }).ToList();
    }

    private static PlayerInjuryRecord Record(
        Guid playerId,
        DateTimeOffset date,
        string status,
        string bodyPart,
        string description,
        string? practice,
        string? gameStatus,
        bool isCurrent,
        DateTimeOffset updated,
        int season) =>
        new()
        {
            PlayerId = playerId,
            Date = date,
            Status = status,
            BodyPart = bodyPart,
            Description = description,
            PracticeStatus = practice,
            GameStatus = gameStatus,
            Source = "Mock",
            SourceUrl = null,
            Season = season,
            LastUpdated = updated,
            IsCurrent = isCurrent,
            ExternalId = $"{playerId:N}:{date:yyyyMMdd}:{status}"
        };
}
