using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Mock historical rows for young/selected catalog players. Used only when Injuries:Provider=Mock.
/// </summary>
public sealed class MockHistoricalInjuryProvider : IHistoricalInjuryProvider
{
    public HistoricalInjuryProviderKind Kind => HistoricalInjuryProviderKind.Mock;

    public string DisplayName => "Mock Historical";

    public bool IsConfigured => true;

    public Task<IReadOnlyList<PlayerInjuryRecord>> GetHistoricalInjuriesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var daniels = Guid.Parse("11111111-1111-1111-1111-111111111101");
        var cmc = Guid.Parse("11111111-1111-1111-1111-111111111106");

        IReadOnlyList<PlayerInjuryRecord> rows =
        [
            new()
            {
                PlayerId = daniels,
                Date = now.AddDays(-10),
                Status = "Out",
                BodyPart = "Knee",
                Description = "Missed prior week with knee soreness.",
                PracticeStatus = "Did Not Practice",
                GameStatus = "Out",
                Source = "Mock",
                Season = 2025,
                LastUpdated = now,
                IsCurrent = false,
                ExternalId = $"hist:{daniels:N}:knee-out"
            },
            new()
            {
                PlayerId = cmc,
                Date = now.AddDays(-40),
                Status = "Injured Reserve",
                BodyPart = "Achilles",
                Description = "Prior IR designation (mock historical seed).",
                PracticeStatus = null,
                GameStatus = "Out",
                Source = "Mock",
                Season = 2025,
                LastUpdated = now.AddDays(-20),
                IsCurrent = false,
                ExternalId = $"hist:{cmc:N}:achilles-ir"
            }
        ];

        return Task.FromResult(rows);
    }
}
