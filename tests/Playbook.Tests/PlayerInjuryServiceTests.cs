using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Infrastructure.Injuries;
using Playbook.Infrastructure.Projections.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

public class PlayerInjuryServiceTests
{
    [Fact]
    public void Player_With_No_Injury_History_Returns_Empty()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var chaseId = Guid.Parse("11111111-1111-1111-1111-111111111109");

        Assert.Empty(injuries.GetInjuriesForPlayer(chaseId));
        Assert.Null(injuries.GetCurrentInjury(chaseId));
    }

    [Fact]
    public void Player_With_One_Current_Injury()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var tyreekId = Guid.Parse("11111111-1111-1111-1111-111111111108");

        var current = injuries.GetCurrentInjury(tyreekId);
        Assert.NotNull(current);
        Assert.Equal("Out", current!.Status);
        Assert.True(current.IsCurrent);
        Assert.Equal("Ankle", current.BodyPart);
    }

    [Fact]
    public void Player_With_Multiple_Historical_Injuries()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var danielsId = Guid.Parse("11111111-1111-1111-1111-111111111101");

        var all = injuries.GetInjuriesForPlayer(danielsId);
        Assert.True(all.Count >= 2);
        Assert.Contains(all, r => r.IsCurrent);
        Assert.Contains(all, r => !r.IsCurrent || all.Count(x => x.IsCurrent) == 1);
        Assert.True(injuries.GetHistoricalInjuries(danielsId).Count >= 1);
    }

    [Fact]
    public void Questionable_Status_Maps_To_Intelligence_Rule()
    {
        var record = Current("Questionable", "Knee", practice: "Limited Participant");
        Assert.Equal("injury-questionable", InjuryIntelligenceMapping.ResolveRuleId(record));
    }

    [Fact]
    public void Out_Status_Maps_To_Intelligence_Rule()
    {
        var record = Current("Out", "Ankle");
        Assert.Equal("injury-out", InjuryIntelligenceMapping.ResolveRuleId(record));
        Assert.Equal(0.15m, InjuryIntelligenceMapping.ProjectionHealthMultiplier(record));
    }

    [Fact]
    public void Returned_Full_Participation_Maps_To_Positive_Rule()
    {
        var record = Current("Active", "Achilles", practice: "Full Participant", description: "Returned to full practice.");
        Assert.Equal("injury-positive", InjuryIntelligenceMapping.ResolveRuleId(record));
    }

    [Fact]
    public void Injury_Information_Affects_Intelligence()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var intelligence = provider.GetRequiredService<IIntelligenceService>();
        var tyreekId = Guid.Parse("11111111-1111-1111-1111-111111111108");

        _ = injuries.GetCurrentInjury(tyreekId);
        var profile = intelligence.GetPlayerProfile(tyreekId);
        Assert.NotNull(profile);
        Assert.True(profile!.HealthScore < 50, $"Expected health drag from Out, got {profile.HealthScore}");
        Assert.Contains(
            profile.SupportingFacts,
            f => f.Category == IntelligenceCategory.Injury &&
                 f.SupportingEvidence.Any(e => e.Contains("injury-out", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Injury_Information_Affects_Projection()
    {
        var engine = new ProjectionEngine(Options.Create(new Application.Projections.ProjectionRuleOptions()));
        var player = new Player
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111108"),
            FullName = "Tyreek Hill",
            FirstName = "Tyreek",
            LastName = "Hill",
            Position = Position.WR,
            Team = "MIA",
            Status = PlayerStatus.Out
        };
        var production = new PlayerProductionSnapshot
        {
            PlayerId = player.Id,
            PlayerName = player.FullName,
            Season = 2024,
            GamesPlayed = 16,
            Position = Position.WR,
            Targets = 130,
            Receptions = 80,
            ReceivingYards = 1000,
            ReceivingTouchdowns = 8,
            Source = ProductionDataSource.CuratedSeason,
            SourceDescription = "Test production"
        };
        var league = new ProjectionLeagueContext
        {
            LeagueId = Guid.NewGuid(),
            LeagueName = "Test",
            ScoringType = ScoringType.Ppr,
            CurrentWeek = 1,
            Season = 2025,
            NumberOfTeams = 12
        };

        var healthy = engine.Project(player, production, null, league, currentInjury: null);
        var injured = engine.Project(
            player,
            production,
            null,
            league,
            Current("Out", "Ankle"));

        Assert.True(
            injured.ProjectedFantasyPoints < healthy.ProjectedFantasyPoints,
            $"Expected Out projection {injured.ProjectedFantasyPoints} < healthy {healthy.ProjectedFantasyPoints}");
        Assert.Contains(injured.ProjectionReasoning, r => r.Contains("availability factor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Provider_Failure_Falls_Back_To_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider();
        // Force live failure path by constructing service with a throwing primary.
        var status = new InjurySyncStatus();
        status.SetConfigured(InjuryProviderKind.Live);
        var cache = new InjuryCacheStore(
            Options.Create(new InjuryOptions
            {
                Provider = InjuryProviderKind.Live,
                CacheFileName = $"injuries-fail-{Guid.NewGuid():N}.json",
                CacheTtlMinutes = 1
            }),
            NullLogger<InjuryCacheStore>.Instance);

        var service = new PlayerInjuryService(
            [new ThrowingInjuryProvider()],
            new MockPlayerInjuryProvider(),
            cache,
            status,
            Options.Create(new InjuryOptions { Provider = InjuryProviderKind.Live }),
            NullLogger<PlayerInjuryService>.Instance);

        await service.RefreshAsync();
        Assert.Equal("Mock", status.ActiveProvider);
        Assert.True(status.UsedFallback);
        Assert.True(status.InjuryRecordsLoaded > 0);
    }

    [Fact]
    public void Modal_Content_Remains_Below_Fixed_Top_Bar()
    {
        var cssPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "Playbook.Web", "wwwroot", "css", "player-overlay.css"));
        Assert.True(File.Exists(cssPath), cssPath);
        var css = File.ReadAllText(cssPath);
        Assert.True(PlayerOverlayLayoutRules.ModalClearsFixedTopBar(css));
        Assert.Contains(PlayerOverlayLayoutRules.TopBarHeightCssVariable, css);
    }

    [Fact]
    public void MergeHistory_Preserves_Prior_Records()
    {
        var id = Guid.NewGuid();
        var previous = new List<PlayerInjuryRecord>
        {
            Current("Out", "Knee", playerId: id) with
            {
                Date = DateTimeOffset.UtcNow.AddDays(-10),
                ExternalId = "old-1",
                IsCurrent = true
            }
        };
        var incoming = new List<PlayerInjuryRecord>
        {
            Current("Questionable", "Knee", playerId: id) with
            {
                Date = DateTimeOffset.UtcNow.AddDays(-1),
                ExternalId = "new-1",
                IsCurrent = true
            }
        };

        var merged = PlayerInjuryService.MergeHistory(previous, incoming);
        Assert.Equal(2, merged.Count);
        Assert.Equal(1, merged.Count(r => r.IsCurrent));
        Assert.Equal("Questionable", merged.First(r => r.IsCurrent).Status);
    }

    private static PlayerInjuryRecord Current(
        string status,
        string bodyPart,
        string? practice = null,
        string? description = null,
        Guid? playerId = null) =>
        new()
        {
            PlayerId = playerId ?? Guid.NewGuid(),
            Date = DateTimeOffset.UtcNow,
            Status = status,
            BodyPart = bodyPart,
            Description = description ?? $"{status} — {bodyPart}",
            PracticeStatus = practice,
            GameStatus = status,
            Source = "Test",
            Season = 2025,
            LastUpdated = DateTimeOffset.UtcNow,
            IsCurrent = true,
            ExternalId = Guid.NewGuid().ToString("N")
        };

    private sealed class ThrowingInjuryProvider : IPlayerInjuryProvider
    {
        public InjuryProviderKind Kind => InjuryProviderKind.Live;
        public string DisplayName => "Throwing";
        public Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated live failure");
    }
}
