using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Core.Injuries.Models;

namespace Playbook.Infrastructure.Injuries;

/// <summary>Mock college injury seeds for demos/tests when Injuries:Provider=Mock.</summary>
public sealed class MockCollegeInjuryProvider : ICollegeInjuryProvider
{
    public CollegeInjuryProviderKind Kind => CollegeInjuryProviderKind.Mock;

    public string DisplayName => "Mock College Injuries";

    public bool IsConfigured => true;

    public Task<IReadOnlyList<PlayerInjuryRecord>> GetCollegeInjuriesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;
        var daniels = Guid.Parse("11111111-1111-1111-1111-111111111101");
        var chase = Guid.Parse("11111111-1111-1111-1111-111111111109");

        IReadOnlyList<PlayerInjuryRecord> rows =
        [
            new()
            {
                PlayerId = daniels,
                Date = now.AddYears(-3).AddDays(-40),
                Season = now.Year - 3,
                Level = InjuryCompetitionLevel.College,
                Team = "LSU",
                Status = "Out",
                BodyPart = "Shoulder",
                InjuryType = "Sprain",
                Description = "Missed college contest with shoulder sprain (mock college seed).",
                GamesMissed = 1,
                PracticeStatus = null,
                GameStatus = "Out",
                Severity = InjurySeverity.Moderate,
                Source = "Mock College",
                LastUpdated = now.AddYears(-3),
                IsCurrent = false,
                Verified = true,
                ExternalId = $"college:{daniels:N}:shoulder"
            },
            new()
            {
                PlayerId = chase,
                Date = now.AddYears(-4).AddMonths(-2),
                Season = now.Year - 4,
                Level = InjuryCompetitionLevel.College,
                Team = "LSU",
                Status = "Questionable",
                BodyPart = "Ankle",
                InjuryType = null,
                Description = "Ankle issue listed on college availability (mock college seed).",
                GamesMissed = 0,
                Severity = InjurySeverity.Minor,
                Source = "Mock College",
                LastUpdated = now.AddYears(-4),
                IsCurrent = false,
                Verified = true,
                ExternalId = $"college:{chase:N}:ankle"
            }
        ];

        return Task.FromResult(rows);
    }
}
