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
    private static readonly Guid ChaseId = Guid.Parse("11111111-1111-1111-1111-111111111109");
    private static readonly Guid TyreekId = Guid.Parse("11111111-1111-1111-1111-111111111108");
    private static readonly Guid DanielsId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    private static readonly Guid CmcId = Guid.Parse("11111111-1111-1111-1111-111111111106");

    [Fact]
    public void Current_Injury_Available()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();

        var profile = injuries.GetPlayerInjuryProfile(TyreekId);
        Assert.Equal(CurrentInjuryDataStatus.Available, profile.CurrentDataStatus);
        Assert.NotNull(profile.CurrentInjury);
        Assert.Equal("Out", profile.CurrentInjury!.Status);
        Assert.Equal("Ankle", profile.CurrentInjury.BodyPart);
        Assert.True(profile.CurrentInjury.IsCurrent);
    }

    [Fact]
    public void Current_Injury_Unavailable_Means_No_Current_Designation()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();

        var profile = injuries.GetPlayerInjuryProfile(ChaseId);
        Assert.Equal(CurrentInjuryDataStatus.NoCurrentInjury, profile.CurrentDataStatus);
        Assert.Null(profile.CurrentInjury);
        Assert.DoesNotContain(injuries.GetInjuriesForPlayer(ChaseId), r => r.IsCurrent);
    }

    [Fact]
    public void Historical_Data_Available_Via_Mock_Historical_Provider()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();

        var profile = injuries.GetPlayerInjuryProfile(DanielsId);
        Assert.Equal(HistoricalDataStatus.Available, profile.HistoricalDataStatus);
        Assert.NotEmpty(profile.HistoricalRecords);
        Assert.All(profile.HistoricalRecords, r => Assert.False(r.IsCurrent));
        Assert.True(injuries.GetHistoricalInjuries(DanielsId).Count >= 1);
    }

    [Fact]
    public async Task Historical_Data_Unavailable_When_Historical_Provider_Fails()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([Current(TyreekId, "Out", "Ankle")]),
            new ThrowingHistoricalInjuryProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        var profile = service.GetPlayerInjuryProfile(TyreekId);

        Assert.Equal(CurrentInjuryDataStatus.Available, profile.CurrentDataStatus);
        Assert.Equal(HistoricalDataStatus.Unavailable, profile.HistoricalDataStatus);
        Assert.Contains("temporarily unavailable", profile.HistoricalAvailabilityMessage!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HistoricalDataStatus.Unavailable.ToString(), status.HistoricalDataAvailability);
    }

    [Fact]
    public async Task Historical_No_Records_Found_When_Provider_Returns_Empty()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([Current(TyreekId, "Out", "Ankle")]),
            new EmptyHistoricalInjuryProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        var profile = service.GetPlayerInjuryProfile(TyreekId);

        Assert.Equal(HistoricalDataStatus.NoRecordsFound, profile.HistoricalDataStatus);
        Assert.Empty(profile.HistoricalRecords);
        Assert.Contains(
            "No historical injury records were returned",
            profile.HistoricalAvailabilityMessage!,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("never", profile.RiskSummary ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_Does_Not_Support_History()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider(
                [Current(TyreekId, "Questionable", "Knee")],
                InjuryProviderCapabilities.CurrentOnlyEspnSleeper),
            new NullHistoricalInjuryProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();

        Assert.False(service.ActiveCapabilities.SupportsHistoricalInjuries);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, service.GlobalHistoricalDataStatus);
        Assert.Equal(
            HistoricalDataStatus.NotSupportedByProvider.ToString(),
            status.HistoricalDataAvailability);
        Assert.False(status.SupportsHistoricalInjuries);

        var profile = service.GetPlayerInjuryProfile(TyreekId);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, profile.HistoricalDataStatus);
        Assert.Equal(
            "Historical injury data is not available from the current provider.",
            profile.HistoricalAvailabilityMessage);
    }

    [Fact]
    public async Task Player_Id_Mapping_Failure_Does_Not_Attach_To_Catalog_Player()
    {
        var unknownId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([Current(unknownId, "Out", "Hamstring")]),
            new NullHistoricalInjuryProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();

        var cmc = service.GetPlayerInjuryProfile(CmcId);
        Assert.Equal(CurrentInjuryDataStatus.NoCurrentInjury, cmc.CurrentDataStatus);
        Assert.Null(cmc.CurrentInjury);

        // Orphan row is indexed by its own id only — never remapped onto CMC/Chase.
        Assert.Empty(service.GetInjuriesForPlayer(CmcId));
        Assert.Empty(service.GetInjuriesForPlayer(ChaseId));
        Assert.NotNull(service.GetCurrentInjury(unknownId));

        Assert.Equal(
            "Mapping failed",
            InjuryAvailabilityPresentation.CurrentStatusLabel(CurrentInjuryDataStatus.MappingFailed));
    }

    [Fact]
    public async Task Intelligence_Does_Not_Treat_Unknown_Historical_As_Healthy()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([], InjuryProviderCapabilities.CurrentOnlyEspnSleeper),
            new NullHistoricalInjuryProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        var profile = service.GetPlayerInjuryProfile(CmcId);

        Assert.Equal(CurrentInjuryDataStatus.NoCurrentInjury, profile.CurrentDataStatus);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, profile.HistoricalDataStatus);

        var facts = InjuryFactBuilder.BuildForProfile(profile);
        Assert.Empty(facts);
        Assert.Contains("does not imply a clean injury history", profile.RiskSummary!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("healthy", profile.RiskSummary!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Injury_Information_Affects_Intelligence_For_Current_Designation()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var intelligence = provider.GetRequiredService<IIntelligenceService>();

        _ = injuries.GetCurrentInjury(TyreekId);
        var profile = intelligence.GetPlayerProfile(TyreekId);
        Assert.NotNull(profile);
        Assert.True(profile!.HealthScore < 50, $"Expected health drag from Out, got {profile.HealthScore}");
        Assert.Contains(
            profile.SupportingFacts,
            f => f.Category == IntelligenceCategory.Injury &&
                 f.SupportingEvidence.Any(e => e.Contains("injury-out", StringComparison.OrdinalIgnoreCase)) &&
                 f.SupportingEvidence.Any(e => e.Contains("Scope: Current", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Projection_Handling_Current_Injury()
    {
        var engine = new ProjectionEngine(Options.Create(new Application.Projections.ProjectionRuleOptions()));
        var player = new Player
        {
            Id = TyreekId,
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
            Current(TyreekId, "Out", "Ankle"));

        Assert.True(
            injured.ProjectedFantasyPoints < healthy.ProjectedFantasyPoints,
            $"Expected Out projection {injured.ProjectedFantasyPoints} < healthy {healthy.ProjectedFantasyPoints}");
        Assert.Contains(injured.ProjectionReasoning, r => r.Contains("availability factor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cmc_Style_Player_Reflects_Current_Only_Provider_Limitations()
    {
        // Mirrors Live ESPN/Sleeper: CMC may have no current designation, and history is not supported.
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([], InjuryProviderCapabilities.CurrentOnlyEspnSleeper),
            new NullHistoricalInjuryProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        var profile = service.GetPlayerInjuryProfile(CmcId);

        Assert.Equal(CurrentInjuryDataStatus.NoCurrentInjury, profile.CurrentDataStatus);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, profile.HistoricalDataStatus);
        Assert.Empty(profile.HistoricalRecords);
        Assert.Equal(
            "Historical injury data is not available from the current provider.",
            profile.HistoricalAvailabilityMessage);
        Assert.DoesNotContain("No injury history available", profile.HistoricalAvailabilityMessage);
        Assert.Contains("not supported by the configured provider", profile.RiskSummary!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(InjuryFactBuilder.BuildForProfile(profile));
        Assert.Equal(0, status.HistoricalInjuryRecords);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider.ToString(), status.HistoricalDataAvailability);
    }

    [Fact]
    public void Questionable_Status_Maps_To_Intelligence_Rule()
    {
        var record = Current(Guid.NewGuid(), "Questionable", "Knee", practice: "Limited Participant");
        Assert.Equal("injury-questionable", InjuryIntelligenceMapping.ResolveRuleId(record));
    }

    [Fact]
    public void Out_Status_Maps_To_Intelligence_Rule()
    {
        var record = Current(Guid.NewGuid(), "Out", "Ankle");
        Assert.Equal("injury-out", InjuryIntelligenceMapping.ResolveRuleId(record));
        Assert.Equal(0.15m, InjuryIntelligenceMapping.ProjectionHealthMultiplier(record));
    }

    [Fact]
    public void Returned_Full_Participation_Maps_To_Positive_Rule()
    {
        var record = Current(
            Guid.NewGuid(),
            "Active",
            "Achilles",
            practice: "Full Participant",
            description: "Returned to full practice.");
        Assert.Equal("injury-positive", InjuryIntelligenceMapping.ResolveRuleId(record));
    }

    [Fact]
    public void Live_Capabilities_Declare_Current_Only()
    {
        Assert.True(InjuryProviderCapabilities.CurrentOnlyEspnSleeper.SupportsCurrentInjuries);
        Assert.False(InjuryProviderCapabilities.CurrentOnlyEspnSleeper.SupportsHistoricalInjuries);
        Assert.Contains(
            "does not supply career historical",
            InjuryProviderCapabilities.CurrentOnlyEspnSleeper.Notes!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Provider_Failure_Falls_Back_To_Mock()
    {
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
            new MockHistoricalInjuryProvider(),
            cache,
            status,
            Options.Create(new InjuryOptions { Provider = InjuryProviderKind.Live }),
            NullLogger<PlayerInjuryService>.Instance);

        await service.RefreshAsync();
        Assert.Equal("Mock", status.ActiveProvider);
        Assert.True(status.UsedFallback);
        Assert.True(status.InjuryRecordsLoaded > 0);
        Assert.True(status.SupportsHistoricalInjuries);
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
    public void Availability_Presentation_Is_Explicit()
    {
        Assert.Equal(
            "Historical injury data is not available from the current provider.",
            InjuryAvailabilityPresentation.HistoricalMessage(HistoricalDataStatus.NotSupportedByProvider));
        Assert.Equal(
            "No historical injury records were returned for this player by the historical provider.",
            InjuryAvailabilityPresentation.HistoricalMessage(HistoricalDataStatus.NoRecordsFound));
        Assert.Equal(
            "No current designation",
            InjuryAvailabilityPresentation.CurrentStatusLabel(CurrentInjuryDataStatus.NoCurrentInjury));
    }

    private static PlayerInjuryService CreateService(
        IPlayerInjuryProvider current,
        IHistoricalInjuryProvider historical,
        InjurySyncStatus status,
        InjuryProviderKind configured)
    {
        status.SetConfigured(configured);
        var cache = new InjuryCacheStore(
            Options.Create(new InjuryOptions
            {
                Provider = configured,
                CacheFileName = $"injuries-test-{Guid.NewGuid():N}.json",
                CacheTtlMinutes = 1
            }),
            NullLogger<InjuryCacheStore>.Instance);

        return new PlayerInjuryService(
            [current],
            new MockPlayerInjuryProvider(),
            historical,
            cache,
            status,
            Options.Create(new InjuryOptions { Provider = configured }),
            NullLogger<PlayerInjuryService>.Instance);
    }

    private static PlayerInjuryRecord Current(
        Guid playerId,
        string status,
        string bodyPart,
        string? practice = null,
        string? description = null) =>
        new()
        {
            PlayerId = playerId,
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

    private sealed class StubCurrentInjuryProvider : IPlayerInjuryProvider
    {
        private readonly IReadOnlyList<PlayerInjuryRecord> _rows;

        public StubCurrentInjuryProvider(
            IReadOnlyList<PlayerInjuryRecord> rows,
            InjuryProviderCapabilities? capabilities = null)
        {
            _rows = rows;
            Capabilities = capabilities ?? InjuryProviderCapabilities.CurrentOnlyEspnSleeper;
        }

        public InjuryProviderKind Kind => InjuryProviderKind.Live;

        public string DisplayName => "Stub Current";

        public InjuryProviderCapabilities Capabilities { get; }

        public Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_rows);
    }

    private sealed class EmptyHistoricalInjuryProvider : IHistoricalInjuryProvider
    {
        public HistoricalInjuryProviderKind Kind => HistoricalInjuryProviderKind.Mock;

        public string DisplayName => "Empty Historical";

        public bool IsConfigured => true;

        public Task<IReadOnlyList<PlayerInjuryRecord>> GetHistoricalInjuriesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlayerInjuryRecord>>([]);
    }

    private sealed class ThrowingHistoricalInjuryProvider : IHistoricalInjuryProvider
    {
        public HistoricalInjuryProviderKind Kind => HistoricalInjuryProviderKind.Mock;

        public string DisplayName => "Throwing Historical";

        public bool IsConfigured => true;

        public Task<IReadOnlyList<PlayerInjuryRecord>> GetHistoricalInjuriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated historical failure");
    }

    private sealed class ThrowingInjuryProvider : IPlayerInjuryProvider
    {
        public InjuryProviderKind Kind => InjuryProviderKind.Live;

        public string DisplayName => "Throwing";

        public InjuryProviderCapabilities Capabilities => InjuryProviderCapabilities.CurrentOnlyEspnSleeper;

        public Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated live failure");
    }
}
