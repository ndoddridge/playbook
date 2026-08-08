using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Deterministic mock CURRENT injury designations. Historical rows come from
/// <see cref="MockHistoricalInjuryProvider"/> when configured.
/// </summary>
public sealed class MockPlayerInjuryProvider : IPlayerInjuryProvider
{
    public InjuryProviderKind Kind => InjuryProviderKind.Mock;

    public string DisplayName => "Mock";

    // History is supplied separately by MockHistoricalInjuryProvider — do not claim this feed returns career rows.
    public InjuryProviderCapabilities Capabilities => InjuryProviderCapabilities.MockCurrentOnly;

    public Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var rows = new List<PlayerInjuryRecord>
        {
            // Jayden Daniels — current questionable
            Record(
                Guid.Parse("11111111-1111-1111-1111-111111111101"),
                now.AddDays(-3),
                "Questionable",
                "Knee",
                "Limited mid-week; game-time decision.",
                "Limited Participant",
                "Questionable",
                now,
                2025),
            // CMC — returned / full participant (current)
            Record(
                Guid.Parse("11111111-1111-1111-1111-111111111106"),
                now.AddDays(-5),
                "Active",
                "Achilles",
                "Returned to full participant status.",
                "Full Participant",
                "Active",
                now,
                2025),
            // Tyreek — Out
            Record(
                Guid.Parse("11111111-1111-1111-1111-111111111108"),
                now.AddDays(-1),
                "Out",
                "Ankle",
                "Ruled out for upcoming contest.",
                "Out",
                "Out",
                now,
                2025)
        };

        return Task.FromResult<IReadOnlyList<PlayerInjuryRecord>>(rows);
    }

    private static PlayerInjuryRecord Record(
        Guid playerId,
        DateTimeOffset date,
        string status,
        string bodyPart,
        string description,
        string? practice,
        string? gameStatus,
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
            IsCurrent = true,
            ExternalId = $"{playerId:N}:{date:yyyyMMdd}:{status}"
        };
}
