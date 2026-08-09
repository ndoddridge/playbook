using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.News;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Stats;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Intelligence.Models;
using Playbook.Core.Leagues;
using Playbook.Core.News;
using Playbook.Core.Players;
using Playbook.Core.Projections.Models;
using Playbook.Application.Players;
using Playbook.Infrastructure.Injuries;
using Playbook.Infrastructure.Players;
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
    public void Current_Confirmed_Injury()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();

        var profile = injuries.GetPlayerInjuryProfile(TyreekId);
        Assert.Equal(CurrentInjuryDataStatus.Available, profile.CurrentDataStatus);
        Assert.NotNull(profile.CurrentInjury);
        Assert.True(profile.CurrentInjury!.Verified);
        Assert.Equal("Out", profile.CurrentInjury.Status);
        Assert.Equal(InjuryCompetitionLevel.Nfl, profile.CurrentInjury.Level);
    }

    [Fact]
    public void Historical_Nfl_Injury_Available_In_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();

        var profile = injuries.GetPlayerInjuryProfile(DanielsId);
        Assert.Equal(HistoricalDataStatus.Available, profile.NflHistoricalDataStatus);
        Assert.NotEmpty(profile.NflCareerHistory);
        Assert.All(profile.NflCareerHistory, e => Assert.NotEqual(InjuryCompetitionLevel.College, e.Record.Level));
    }

    [Fact]
    public void Historical_College_Injury_Available_In_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();

        var profile = injuries.GetPlayerInjuryProfile(DanielsId);
        Assert.Equal(HistoricalDataStatus.Available, profile.CollegeHistoricalDataStatus);
        Assert.NotEmpty(profile.CollegeHistory);
        Assert.All(profile.CollegeHistory, e => Assert.Equal(InjuryCompetitionLevel.College, e.Record.Level));
    }

    [Fact]
    public void Old_Low_Relevance_Injury_Still_Accessible()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var profile = injuries.GetPlayerInjuryProfile(DanielsId);

        var old = profile.HistoricalEntries
            .Where(e => e.Band is InjuryRelevanceBand.Low or InjuryRelevanceBand.Minimal)
            .ToList();
        Assert.NotEmpty(old);
        Assert.Contains(profile.NflCareerHistory.Concat(profile.CollegeHistory), e =>
            e.Band is InjuryRelevanceBand.Low or InjuryRelevanceBand.Minimal);
    }

    [Fact]
    public void Recent_High_Relevance_Injury()
    {
        using var provider = TestServiceFactory.CreateProvider();
        var injuries = provider.GetRequiredService<IPlayerInjuryService>();
        var profile = injuries.GetPlayerInjuryProfile(DanielsId);

        Assert.Contains(profile.RecentHistory, e => e.Band is InjuryRelevanceBand.High or InjuryRelevanceBand.Moderate);
        Assert.Contains(profile.HistoricalEntries, e => e.RelevanceScore >= 45);
    }

    [Fact]
    public void Repeated_Body_Part_History_Boosts_Relevance()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var rows = new List<PlayerInjuryRecord>
        {
            Record(id, now.AddDays(-20), "Out", "Achilles", gamesMissed: 3, severity: InjurySeverity.Significant),
            Record(id, now.AddDays(-400), "Out", "Achilles", gamesMissed: 2, severity: InjurySeverity.Significant)
        };

        var scored = InjuryRelevanceCalculator.ScoreAll(rows, now);
        var recent = scored.First(e => e.Record.Date == rows[0].Date);
        Assert.Contains("Achilles", recent.RelevanceReason!, StringComparison.OrdinalIgnoreCase);
        Assert.True(recent.RelevanceScore >= 45);
    }

    [Fact]
    public void Unconfirmed_Injury_News_Is_Separate()
    {
        var playerId = Guid.NewGuid();
        var articles = new List<NewsArticle>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Star RB reportedly dealing with hamstring tightness",
                Summary = "Team is monitoring the situation after he appeared limited.",
                Published = DateTimeOffset.UtcNow.AddMinutes(-12),
                Source = "ESPN",
                Category = NewsCategory.Injury,
                Priority = NewsPriority.Normal,
                RelatedPlayerIds = [playerId],
                RelatedTeamIds = [],
                RelatedPlayerNames = []
            }
        };

        var signals = UnconfirmedInjurySignalExtractor.ExtractForPlayer(playerId, articles, false);
        Assert.NotEmpty(signals);
        Assert.Equal("Unconfirmed", signals[0].VerificationLabel);
        Assert.DoesNotContain(signals, s => s.VerificationLabel == "Verified");
    }

    [Fact]
    public void Confirmed_Vs_Unconfirmed_Distinction_In_Facts()
    {
        var profile = new PlayerInjuryProfile
        {
            PlayerId = TyreekId,
            CurrentDataStatus = CurrentInjuryDataStatus.Available,
            CurrentInjury = Record(TyreekId, DateTimeOffset.UtcNow, "Out", "Ankle", isCurrent: true),
            UnconfirmedSignals =
            [
                new UnconfirmedInjurySignal
                {
                    Id = Guid.NewGuid(),
                    PlayerId = TyreekId,
                    Headline = "Reportedly dealing with ankle",
                    Source = "ESPN",
                    Published = DateTimeOffset.UtcNow,
                    LastUpdated = DateTimeOffset.UtcNow,
                    Confidence = 55
                }
            ]
        };

        var facts = InjuryFactBuilder.BuildForProfile(profile);
        Assert.Contains(facts, f => f.Tags.Contains("verified") && f.Tags.Contains("current"));
        Assert.Contains(facts, f => f.Tags.Contains("unconfirmed"));
        Assert.Contains(facts, f => f.Title.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Conflicting_Injury_Reports_Reduce_Confidence()
    {
        var playerId = Guid.NewGuid();
        var articles = new List<NewsArticle>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Player reportedly dealing with ankle",
                Summary = "Monitoring after limited work.",
                Published = DateTimeOffset.UtcNow.AddHours(-2),
                Source = "ESPN",
                Category = NewsCategory.Injury,
                Priority = NewsPriority.Normal,
                RelatedPlayerIds = [playerId],
                RelatedTeamIds = [],
                RelatedPlayerNames = []
            },
            new()
            {
                Id = Guid.NewGuid(),
                Title = "Player reportedly dealing with ankle — cleared",
                Summary = "Returned to full practice and looks healthy.",
                Published = DateTimeOffset.UtcNow.AddHours(-1),
                Source = "NFL.com",
                Category = NewsCategory.Injury,
                Priority = NewsPriority.Normal,
                RelatedPlayerIds = [playerId],
                RelatedTeamIds = [],
                RelatedPlayerNames = []
            }
        };

        var signals = UnconfirmedInjurySignalExtractor.ExtractForPlayer(playerId, articles, false);
        Assert.NotEmpty(signals);
        Assert.True(signals[0].SourceCount >= 1);
        Assert.True(signals.Any(s => s.IsContradicted) || signals[0].Confidence < 70);
    }

    [Fact]
    public async Task Missing_Provider_Data_Is_Explicit()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([]),
            new NullHistoricalInjuryProvider(),
            new NullCollegeInjuryProvider(),
            new EmptyNewsProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        var profile = service.GetPlayerInjuryProfile(CmcId);
        Assert.Equal(CurrentInjuryDataStatus.NoCurrentInjury, profile.CurrentDataStatus);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, profile.NflHistoricalDataStatus);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, profile.CollegeHistoricalDataStatus);
    }

    [Fact]
    public async Task Provider_Does_Not_Support_Historical_Data()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([Record(TyreekId, DateTimeOffset.UtcNow, "Questionable", "Knee", isCurrent: true)]),
            new NullHistoricalInjuryProvider(),
            new NullCollegeInjuryProvider(),
            new EmptyNewsProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        Assert.False(service.ActiveCapabilities.SupportsHistoricalInjuries);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, service.GlobalHistoricalDataStatus);
        Assert.Contains("not supported", status.ProviderCoverage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, status.NflHistoricalRecords);
        Assert.Equal(0, status.CollegeHistoricalRecords);
    }

    [Fact]
    public async Task Multiple_Injury_Sources_Combine_Into_Profile()
    {
        var status = new InjurySyncStatus();
        var service = CreateService(
            new StubCurrentInjuryProvider([Record(DanielsId, DateTimeOffset.UtcNow, "Questionable", "Knee", isCurrent: true)]),
            new MockHistoricalInjuryProvider(),
            new MockCollegeInjuryProvider(),
            new EmptyNewsProvider(),
            status,
            InjuryProviderKind.Live);

        await service.RefreshAsync();
        var profile = service.GetPlayerInjuryProfile(DanielsId);
        Assert.NotNull(profile.CurrentInjury);
        Assert.NotEmpty(profile.NflCareerHistory);
        Assert.NotEmpty(profile.CollegeHistory);
        Assert.True(status.NflHistoricalRecords > 0);
        Assert.True(status.CollegeHistoricalRecords > 0);
        Assert.Contains("Mock", status.InjuryProviders, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Intelligence_Weights_Injury_Signal_Types()
    {
        var profile = new PlayerInjuryProfile
        {
            PlayerId = CmcId,
            CurrentDataStatus = CurrentInjuryDataStatus.NoCurrentInjury,
            HistoricalEntries =
            [
                InjuryRelevanceCalculator.Score(
                    Record(CmcId, DateTimeOffset.UtcNow.AddDays(-20), "Injured Reserve", "Achilles",
                        gamesMissed: 8, severity: InjurySeverity.Major),
                    DateTimeOffset.UtcNow)
            ],
            UnconfirmedSignals =
            [
                new UnconfirmedInjurySignal
                {
                    Id = Guid.NewGuid(),
                    PlayerId = CmcId,
                    Headline = "Reportedly limited at practice",
                    Source = "ESPN",
                    Published = DateTimeOffset.UtcNow,
                    LastUpdated = DateTimeOffset.UtcNow,
                    Confidence = 60
                }
            ]
        };

        var facts = InjuryFactBuilder.BuildForProfile(profile);
        Assert.Contains(facts, f => f.SupportingEvidence.Any(e => e.Contains("Historical Risk")));
        Assert.Contains(facts, f => f.SupportingEvidence.Any(e => e.Contains("Unconfirmed Injury Concern")));
        Assert.DoesNotContain(facts, f => f.Tags.Contains("current") && f.Tags.Contains("verified"));
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
                 f.SupportingEvidence.Any(e => e.Contains("injury-out", StringComparison.OrdinalIgnoreCase)));
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
            Record(TyreekId, DateTimeOffset.UtcNow, "Out", "Ankle", isCurrent: true));

        Assert.True(injured.ProjectedFantasyPoints < healthy.ProjectedFantasyPoints);
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
            new MockCollegeInjuryProvider(),
            new EmptyNewsProvider(),
            new PlayerIdentityDirectory(),
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
    public void Questionable_Status_Maps_To_Intelligence_Rule()
    {
        var record = Record(Guid.NewGuid(), DateTimeOffset.UtcNow, "Questionable", "Knee",
            practice: "Limited Participant", isCurrent: true);
        Assert.Equal("injury-questionable", InjuryIntelligenceMapping.ResolveRuleId(record));
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
        Assert.Contains("z-index: 100", css, StringComparison.Ordinal);
        Assert.Contains("safe-area-inset-top", css, StringComparison.Ordinal);
    }

    private static PlayerInjuryService CreateService(
        IPlayerInjuryProvider current,
        IHistoricalInjuryProvider historical,
        ICollegeInjuryProvider college,
        INewsProvider news,
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
            college,
            news,
            new PlayerIdentityDirectory(),
            cache,
            status,
            Options.Create(new InjuryOptions { Provider = configured }),
            NullLogger<PlayerInjuryService>.Instance);
    }

    private static PlayerInjuryRecord Record(
        Guid playerId,
        DateTimeOffset date,
        string status,
        string bodyPart,
        string? practice = null,
        int? gamesMissed = null,
        InjurySeverity? severity = null,
        bool isCurrent = false) =>
        new()
        {
            PlayerId = playerId,
            Date = date,
            Season = date.Year,
            Level = InjuryCompetitionLevel.Nfl,
            Status = status,
            BodyPart = bodyPart,
            Description = $"{status} — {bodyPart}",
            PracticeStatus = practice,
            GameStatus = status,
            GamesMissed = gamesMissed,
            Severity = severity ?? InjurySeverityInference.FromStatus(status, gamesMissed),
            Source = "Test",
            Verified = true,
            LastUpdated = date,
            IsCurrent = isCurrent,
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

    private sealed class ThrowingInjuryProvider : IPlayerInjuryProvider
    {
        public InjuryProviderKind Kind => InjuryProviderKind.Live;
        public string DisplayName => "Throwing";
        public InjuryProviderCapabilities Capabilities => InjuryProviderCapabilities.CurrentOnlyEspnSleeper;

        public Task<IReadOnlyList<PlayerInjuryRecord>> GetInjuriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated live failure");
    }

    private sealed class EmptyNewsProvider : INewsProvider
    {
        public string DisplayName => "Empty";
        public IReadOnlyList<NewsArticle> GetLatest(int count = 12) => [];
        public IReadOnlyList<NewsArticle> GetForPlayer(Guid playerId, int count = 8) => [];
        public NewsArticle? GetById(Guid articleId) => null;
        public IReadOnlyList<NewsArticle> GetByIds(IEnumerable<Guid> articleIds) => [];
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
