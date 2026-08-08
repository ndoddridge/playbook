using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>
/// Mock NFL historical rows for selected catalog players. Used only when Injuries:Provider=Mock.
/// </summary>
public sealed class MockHistoricalInjuryProvider : IHistoricalInjuryProvider
{
    public HistoricalInjuryProviderKind Kind => HistoricalInjuryProviderKind.Mock;

    public string DisplayName => "Mock NFL Historical";

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
                Season = 2025,
                Level = InjuryCompetitionLevel.Nfl,
                Team = "WAS",
                Status = "Out",
                BodyPart = "Knee",
                InjuryType = "Soreness",
                Description = "Missed prior week with knee soreness.",
                GamesMissed = 1,
                PracticeStatus = "Did Not Practice",
                GameStatus = "Out",
                Severity = InjurySeverity.Significant,
                Source = "Mock",
                LastUpdated = now,
                IsCurrent = false,
                Verified = true,
                ExternalId = $"hist:{daniels:N}:knee-out"
            },
            new()
            {
                PlayerId = daniels,
                Date = now.AddDays(-400),
                Season = 2024,
                Level = InjuryCompetitionLevel.Nfl,
                Team = "WAS",
                Status = "Questionable",
                BodyPart = "Ribs",
                Description = "Rib contusion earlier in career (mock NFL seed).",
                GamesMissed = 0,
                Severity = InjurySeverity.Minor,
                Source = "Mock",
                LastUpdated = now.AddDays(-380),
                IsCurrent = false,
                Verified = true,
                ExternalId = $"hist:{daniels:N}:ribs"
            },
            new()
            {
                PlayerId = cmc,
                Date = now.AddDays(-40),
                Season = 2025,
                Level = InjuryCompetitionLevel.Nfl,
                Team = "SF",
                Status = "Injured Reserve",
                BodyPart = "Achilles",
                InjuryType = "Tear",
                Description = "Prior IR designation (mock historical seed).",
                GamesMissed = 8,
                PracticeStatus = null,
                GameStatus = "Out",
                Severity = InjurySeverity.Major,
                Source = "Mock",
                LastUpdated = now.AddDays(-20),
                IsCurrent = false,
                Verified = true,
                ExternalId = $"hist:{cmc:N}:achilles-ir"
            },
            new()
            {
                PlayerId = cmc,
                Date = now.AddDays(-500),
                Season = 2024,
                Level = InjuryCompetitionLevel.Nfl,
                Team = "SF",
                Status = "Out",
                BodyPart = "Achilles",
                Description = "Earlier Achilles-related absence (mock repeated body-part seed).",
                GamesMissed = 3,
                Severity = InjurySeverity.Significant,
                Source = "Mock",
                LastUpdated = now.AddDays(-480),
                IsCurrent = false,
                Verified = true,
                ExternalId = $"hist:{cmc:N}:achilles-prior"
            }
        ];

        return Task.FromResult(rows);
    }
}
