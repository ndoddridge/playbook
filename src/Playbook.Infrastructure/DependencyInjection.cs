using Playbook.Application.Abstractions;
using Playbook.Application.Draft;
using Playbook.Application.Replay;
using Playbook.Application.Injuries;
using Playbook.Application.Historical;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Intelligence;
using Playbook.Application.Intelligence.Interfaces;
using Playbook.Application.Leagues;
using Playbook.Application.Leagues.Sleeper;
using Playbook.Application.News;
using Playbook.Application.Players;
using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Playbook.Application.Projections;
using Playbook.Application.Projections.Interfaces;
using Playbook.Application.Recommendations;
using Playbook.Application.Stats;
using Playbook.Application.Stats.Interfaces;
using Playbook.Application.Knowledge;
using Playbook.Core.Knowledge;
using Playbook.Core.Replay;
using Playbook.Infrastructure.Decisions;
using Playbook.Infrastructure.Draft;
using Playbook.Infrastructure.Knowledge;
using Playbook.Infrastructure.Replay;
using Playbook.Infrastructure.Replay.Calibration;
using Playbook.Infrastructure.Replay.Nflverse;
using Playbook.Infrastructure.Replay.Reconstruction;
using Playbook.Infrastructure.Hosting;
using Playbook.Infrastructure.Injuries;
using Playbook.Infrastructure.Historical;
using Playbook.Infrastructure.Intelligence.Services;
using Playbook.Infrastructure.Leagues;
using Playbook.Infrastructure.News;
using Playbook.Infrastructure.Players;
using Playbook.Infrastructure.Predictions;
using Playbook.Infrastructure.Projections.Services;
using Playbook.Infrastructure.Recommendations;
using Playbook.Infrastructure.Stats;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Infrastructure;

/// <summary>
/// Registers infrastructure implementations (persistence, external clients, adapters).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.Configure<PlayerDataOptions>(configuration.GetSection(PlayerDataOptions.SectionName));
            services.Configure<NewsOptions>(configuration.GetSection(NewsOptions.SectionName));
            services.Configure<BackgroundRefreshOptions>(configuration.GetSection(BackgroundRefreshOptions.SectionName));
            services.Configure<IntelligenceScoringOptions>(configuration.GetSection(IntelligenceScoringOptions.SectionName));
            services.Configure<ProjectionRuleOptions>(configuration.GetSection(ProjectionRuleOptions.SectionName));
            services.Configure<QuickPicksScoringOptions>(configuration.GetSection(QuickPicksScoringOptions.SectionName));
            services.Configure<PlayerStatsOptions>(configuration.GetSection(PlayerStatsOptions.SectionName));
            services.Configure<CollegeStatsOptions>(configuration.GetSection(CollegeStatsOptions.SectionName));
            services.Configure<InjuryOptions>(configuration.GetSection(InjuryOptions.SectionName));
            services.Configure<PropLineOptions>(configuration.GetSection(PropLineOptions.SectionName));
            services.PostConfigure<PropLineOptions>(PropLineCredentialResolver.ApplyAliasEnvironmentVariables);
            services.Configure<LeagueOptions>(configuration.GetSection(LeagueOptions.SectionName));
            services.Configure<AnthropicVisionOptions>(configuration.GetSection(AnthropicVisionOptions.SectionName));
        }
        else
        {
            services.Configure<PlayerDataOptions>(_ => { });
            services.Configure<NewsOptions>(_ => { });
            services.Configure<BackgroundRefreshOptions>(_ => { });
            services.Configure<IntelligenceScoringOptions>(_ => { });
            services.Configure<ProjectionRuleOptions>(_ => { });
            services.Configure<QuickPicksScoringOptions>(_ => { });
            services.Configure<PlayerStatsOptions>(_ => { });
            services.Configure<CollegeStatsOptions>(_ => { });
            services.Configure<InjuryOptions>(_ => { });
            services.Configure<PropLineOptions>(_ => { });
            services.PostConfigure<PropLineOptions>(PropLineCredentialResolver.ApplyAliasEnvironmentVariables);
            services.Configure<LeagueOptions>(_ => { });
            services.Configure<AnthropicVisionOptions>(_ => { });
        }

        RegisterPlayerData(services);
        RegisterPlayerStats(services);
        RegisterCollegeStats(services);
        RegisterInjuries(services);
        RegisterNews(services);
        RegisterIntelligence(services);
        RegisterProjections(services);
        RegisterResearch(services);
        RegisterQuickPicks(services);

        services.AddSingleton<IPlayerContextService, MockPlayerContextService>();
        RegisterLeagues(services);
        services.AddSingleton<IHistoricalLeagueDraftStore, HistoricalLeagueDraftStore>();
        services.AddSingleton<IHistoricalLeagueIntelligenceService, HistoricalLeagueIntelligenceService>();
        services.AddSingleton<IRecommendationService, MockRecommendationService>();
        // Bye weeks from the real published schedule (same nflverse cache, no new dependency).
        services.AddSingleton<IByeWeekProvider, NflverseByeWeekProvider>();
        services.AddSingleton<IDraftAssistantService, DraftAssistantService>();

        // Draft screenshot ingestion — quietly unavailable (throws only when actually invoked)
        // until DraftImageIngestion__ApiKey is configured; no Mock/Live switch since there's no
        // meaningful mock vision fallback.
        services.AddHttpClient(AnthropicDraftImageIngestionService.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnthropicVisionOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.DefaultRequestHeaders.Add("x-api-key", options.ApiKey);
        });
        services.AddSingleton<IDraftImageIngestionService, AnthropicDraftImageIngestionService>();

        services.AddHostedService<DataRefreshBackgroundService>();
        services.AddHostedService<LeagueAutoReconnectHostedService>();
        return services;
    }

    private static void RegisterLeagues(IServiceCollection services)
    {
        services.AddSingleton<LeagueSyncStatus>();
        services.AddSingleton<ILeagueSyncStatus>(sp => sp.GetRequiredService<LeagueSyncStatus>());
        services.AddSingleton<MockLeagueService>();
        services.AddSingleton<ILeagueUserTeamStore, LeagueUserTeamStore>();

        services.AddHttpClient(SleeperLeagueClient.HttpClientName, (sp, client) =>
        {
            var sleeper = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerDataOptions>>().Value.Sleeper;
            var baseUrl = string.IsNullOrWhiteSpace(sleeper.BaseUrl)
                ? "https://api.sleeper.app/v1/"
                : sleeper.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sleeper.TimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(sleeper.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", sleeper.ApiKey);
            }
        });

        services.AddSingleton<ISleeperLeagueClient, SleeperLeagueClient>();
        services.AddSingleton<ILeagueService, CompositeLeagueService>();
    }

    private static void RegisterIntelligence(IServiceCollection services)
    {
        services.AddSingleton<IntelligenceSyncStatus>();
        services.AddSingleton<IIntelligenceSyncStatus>(sp => sp.GetRequiredService<IntelligenceSyncStatus>());
        services.AddSingleton<IIntelligenceAnalyzer, IntelligenceAnalyzer>();
        services.AddSingleton<IIntelligenceAggregator, IntelligenceAggregator>();
        services.AddSingleton<IIntelligenceService, IntelligenceService>();
        services.AddSingleton<IPlayerIntelligenceAssessmentService, PlayerIntelligenceAssessmentService>();
        services.AddSingleton<IDecisionRecordStore, InMemoryDecisionRecordStore>();
        services.AddSingleton<IPlayerKnowledgeComposer, PlayerKnowledgeComposer>();
        services.AddSingleton<ConfidenceAwareDecisionPolicyState>();
        services.AddSingleton<IConfidenceAwareDecisionPolicy, ConfidenceAwareDecisionPolicyApplicator>();
        services.AddSingleton<IDecisionEngine, DecisionEngine>();
        services.AddSingleton<IFantasyTeamIntelligenceService, FantasyTeamIntelligenceService>();
        services.AddSingleton<IWeeklyMatchupGamePlanService, WeeklyMatchupGamePlanService>();
        services.AddSingleton<IDropPickupService, DropPickupService>();
        RegisterHistoricalReplay(services);
    }

    private static void RegisterHistoricalReplay(IServiceCollection services)
    {
        services.AddSingleton<IHistoricalPlayerIdentityNormalizer, HistoricalPlayerIdentityNormalizer>();
        services.AddSingleton<IHistoricalWeekDataValidator, HistoricalWeekDataValidator>();
        services.AddSingleton<IHistoricalFeatureReconstructor, HistoricalFeatureReconstructor>();
        services.AddSingleton<RecentAverageProjectionEngine>();
        services.AddSingleton<OpportunityAwareProjectionEngine>();
        services.AddSingleton<CalibratedOpportunityAwareProjectionEngine>();
        services.AddSingleton<PositionSegmentedCalibrationState>();
        services.AddSingleton<PositionSegmentedCalibratedProjectionEngine>();
        services.AddSingleton<HistoricalProjectionExperimentState>();
        services.AddSingleton<IHistoricalExpectationService, HistoricalExpectationService>();
        services.AddSingleton<ProjectionCalibrationExperimentRunner>();
        services.AddSingleton<PositionSegmentedCalibrationExperimentRunner>();
        services.AddSingleton<ConfidenceCalibrationExperimentRunner>();
        services.AddSingleton<ConfidenceAwareDecisionPolicyExperimentRunner>();
        services.AddSingleton<NflverseCsvCache>();
        services.AddHttpClient(NflverseCsvCache.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        });
        services.AddSingleton<IHistoricalDataProvider, NflverseHistoricalDataProvider>();
        services.AddSingleton<IHistoricalSnapshotSource, CompositeHistoricalSnapshotSource>();
        services.AddSingleton<IHistoricalSnapshotBuilder, HistoricalSnapshotBuilder>();
        services.AddSingleton<IHistoricalKnowledgeFactory, HistoricalKnowledgeFactory>();
        services.AddSingleton<ISharedKnowledgeModel, SharedKnowledgeModel>();
        services.AddSingleton<KnowledgeImpactExperimentState>();
        services.AddSingleton<IKnowledgeImpactApplicator, KnowledgeImpactApplicator>();
        services.AddSingleton<KnowledgeImpactExperimentRunner>();
        services.AddSingleton<SharedKnowledgeExpandedUniverseExperimentRunner>();
        services.AddSingleton<HistoricalQuickPickGenerator>();
        services.AddSingleton<IQuickPicksHistoricalEvaluationRunner, QuickPicksHistoricalEvaluationRunner>();
        services.AddSingleton<IDecisionOutcomeEvaluator, StartSitOutcomeEvaluator>();
        services.AddSingleton<IHistoricalReplayRunner, HistoricalReplayRunner>();
        services.AddSingleton<IMultiWeekHistoricalReplayRunner, MultiWeekHistoricalReplayRunner>();
        services.AddSingleton<IHistoricalSeasonCalendar, NflverseHistoricalSeasonCalendar>();
        services.AddSingleton<IMultiSeasonHistoricalBenchmarkRunner, MultiSeasonHistoricalBenchmarkRunner>();
        services.AddSingleton<HistoricalEvaluationCoverageRunner>();
    }

    private static void RegisterProjections(IServiceCollection services)
    {
        services.AddSingleton<ProjectionSyncStatus>();
        services.AddSingleton<IProjectionSyncStatus>(sp => sp.GetRequiredService<ProjectionSyncStatus>());
        services.AddSingleton<IPlayerProductionProvider, PlayerProductionProvider>();
        services.AddSingleton<IMatchupContextProvider, UnavailableMatchupContextProvider>();
        services.AddSingleton<IGameEnvironmentProvider, UnavailableGameEnvironmentProvider>();
        services.AddSingleton<IProjectionEngine, ProjectionEngine>();
        services.AddSingleton<IProjectionService, ProjectionService>();
    }

    private static void RegisterResearch(IServiceCollection services)
    {
        services.AddSingleton<Application.Research.IPredictionResearchStore, Research.PredictionResearchStore>();
        services.AddSingleton<Application.Research.IPredictionOutcomeClassifier, Research.PredictionOutcomeClassifier>();
        services.AddSingleton<Application.Research.IPostEventReconciliationService, Research.PostEventReconciliationService>();
        services.AddSingleton<Application.Research.ISharedEvidenceService, Research.SharedEvidenceService>();
    }

    private static void RegisterQuickPicks(IServiceCollection services)
    {
        services.AddSingleton<QuickPicksSyncStatus>();
        services.AddSingleton<IQuickPicksSyncStatus>(sp => sp.GetRequiredService<QuickPicksSyncStatus>());
        services.AddSingleton<IQuickPicksEngine, QuickPicksEngine>();
        services.AddSingleton<INflCalendarService, NflCalendarService>();
        // Real NFL final scores — the only source of team points. Reuses the nflverse CSV cache
        // already registered for historical replay, so no new external dependency or API key.
        services.AddSingleton<NflverseGameScoreProvider>();
        services.AddSingleton<IHistoricalGameScoreProvider>(sp =>
            sp.GetRequiredService<NflverseGameScoreProvider>());
        // Player-level evidence for the team-points model. Same nflverse cache, no new dependency.
        services.AddSingleton<NflverseQuarterbackFormProvider>();
        services.AddSingleton<IQuarterbackFormProvider>(sp =>
            sp.GetRequiredService<NflverseQuarterbackFormProvider>());
        services.AddSingleton<ITeamGameProjectionService, TeamGameProjectionService>();

        services.AddSingleton<MockPropLineProvider>();
        services.AddSingleton<IPropLineProvider>(sp => sp.GetRequiredService<MockPropLineProvider>());

        services.AddHttpClient(LivePropLineProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PropLineOptions>>().Value.OddsApi;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://api.the-odds-api.com/v4/"
                : options.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });
        services.AddSingleton<LivePropLineProvider>();
        services.AddSingleton<IPropLineProvider>(sp => sp.GetRequiredService<LivePropLineProvider>());

        services.AddSingleton<IQuickPicksService, QuickPicksService>();
    }

    private static void RegisterPlayerData(IServiceCollection services)
    {
        services.AddSingleton<PlayerDataSyncStatus>();
        services.AddSingleton<IPlayerDataSyncStatus>(sp => sp.GetRequiredService<PlayerDataSyncStatus>());
        services.AddSingleton<PlayerIdentityDirectory>();
        services.AddSingleton<IPlayerIdentityDirectory>(sp => sp.GetRequiredService<PlayerIdentityDirectory>());

        services.AddSingleton<MockPlayerDataProvider>();
        services.AddSingleton<IPlayerDataProvider>(sp => sp.GetRequiredService<MockPlayerDataProvider>());

        services.AddHttpClient(LivePlayerDataProvider.HttpClientName, (sp, client) =>
        {
            var sleeper = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerDataOptions>>().Value.Sleeper;
            var baseUrl = string.IsNullOrWhiteSpace(sleeper.BaseUrl)
                ? "https://api.sleeper.app/v1/"
                : sleeper.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(sleeper.TimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(sleeper.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", sleeper.ApiKey);
            }
        });

        services.AddSingleton<LivePlayerDataProvider>();
        services.AddSingleton<IPlayerDataProvider>(sp => sp.GetRequiredService<LivePlayerDataProvider>());
        services.AddSingleton<IPlayerService, PlayerService>();
    }

    private static void RegisterPlayerStats(IServiceCollection services)
    {
        services.AddSingleton<PlayerStatsSyncStatus>();
        services.AddSingleton<IPlayerStatsSyncStatus>(sp => sp.GetRequiredService<PlayerStatsSyncStatus>());
        services.AddSingleton<PlayerStatsCacheStore>();
        services.AddSingleton<PlayerGameLogCacheStore>();

        services.AddSingleton<MockPlayerStatsProvider>();
        services.AddSingleton<IPlayerStatsProvider>(sp => sp.GetRequiredService<MockPlayerStatsProvider>());

        services.AddHttpClient(LivePlayerStatsProvider.HttpClientName, (sp, client) =>
        {
            var sleeper = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerDataOptions>>().Value.Sleeper;
            var stats = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerStatsOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(sleeper.BaseUrl)
                ? "https://api.sleeper.app/v1/"
                : sleeper.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(stats.TimeoutSeconds, 10, 180));
            if (!string.IsNullOrWhiteSpace(sleeper.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", sleeper.ApiKey);
            }
        });

        services.AddSingleton<LivePlayerStatsProvider>();
        services.AddSingleton<IPlayerStatsProvider>(sp => sp.GetRequiredService<LivePlayerStatsProvider>());

        services.AddSingleton<NullHistoricalPlayerStatsProvider>();
        services.AddSingleton<MockHistoricalPlayerStatsProvider>();
        services.AddHttpClient(NflversePlayerStatsProvider.HttpClientName, (sp, client) =>
        {
            var stats = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerStatsOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(stats.TimeoutSeconds, 30, 300));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/gzip,application/octet-stream,*/*");
        });
        services.AddSingleton<NflversePlayerStatsProvider>();
        services.AddSingleton<IHistoricalPlayerStatsProvider>(sp =>
        {
            var kind = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerStatsOptions>>()
                .Value.HistoricalProvider;
            return kind switch
            {
                HistoricalPlayerStatsProviderKind.Mock => sp.GetRequiredService<MockHistoricalPlayerStatsProvider>(),
                HistoricalPlayerStatsProviderKind.Nflverse => sp.GetRequiredService<NflversePlayerStatsProvider>(),
                _ => sp.GetRequiredService<NullHistoricalPlayerStatsProvider>()
            };
        });

        services.AddSingleton<IPlayerStatsService, PlayerStatsService>();
        services.AddSingleton<IPlayerGameLogStore>(sp => sp.GetRequiredService<IPlayerStatsService>());
        services.AddSingleton<IPlayerStatisticalContextService, PlayerStatisticalContextService>();

        services.AddSingleton<NullPreseasonPlayerGameLogProvider>();
        services.AddHttpClient(EspnPreseasonGameLogProvider.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://site.api.espn.com/apis/site/v2/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });
        services.AddSingleton<EspnPreseasonGameLogProvider>();
        services.AddSingleton<IPreseasonPlayerGameLogProvider>(sp =>
        {
            var provider = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PlayerStatsOptions>>()
                .Value.Provider;
            return provider == PlayerStatsProviderKind.Mock
                ? sp.GetRequiredService<NullPreseasonPlayerGameLogProvider>()
                : sp.GetRequiredService<EspnPreseasonGameLogProvider>();
        });
    }

    private static void RegisterCollegeStats(IServiceCollection services)
    {
        services.AddSingleton<CollegeStatsSyncStatus>();
        services.AddSingleton<ICollegeStatsSyncStatus>(sp => sp.GetRequiredService<CollegeStatsSyncStatus>());
        services.AddSingleton<CollegeStatsCacheStore>();

        services.AddSingleton<MockCollegeStatsProvider>();
        services.AddSingleton<ICollegeStatsProvider>(sp => sp.GetRequiredService<MockCollegeStatsProvider>());

        services.AddHttpClient(LiveCollegeStatsProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CollegeStatsOptions>>().Value;
            client.BaseAddress = new Uri("https://site.web.api.espn.com/apis/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 15, 180));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddSingleton<LiveCollegeStatsProvider>();
        services.AddSingleton<ICollegeStatsProvider>(sp => sp.GetRequiredService<LiveCollegeStatsProvider>());
        services.AddSingleton<CollegeStatsService>();
    }

    private static void RegisterInjuries(IServiceCollection services)
    {
        services.AddSingleton<InjurySyncStatus>();
        services.AddSingleton<IInjurySyncStatus>(sp => sp.GetRequiredService<InjurySyncStatus>());
        services.AddSingleton<InjuryCacheStore>();

        services.AddSingleton<MockPlayerInjuryProvider>();
        services.AddSingleton<IPlayerInjuryProvider>(sp => sp.GetRequiredService<MockPlayerInjuryProvider>());

        services.AddHttpClient(LivePlayerInjuryProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InjuryOptions>>().Value;
            client.BaseAddress = new Uri("https://site.web.api.espn.com/apis/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 15, 180));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddSingleton<LivePlayerInjuryProvider>();
        services.AddSingleton<IPlayerInjuryProvider>(sp => sp.GetRequiredService<LivePlayerInjuryProvider>());

        // Historical / college injury sources are separate from current ESPN/Sleeper designations.
        services.AddSingleton<NullHistoricalInjuryProvider>();
        services.AddSingleton<MockHistoricalInjuryProvider>();
        services.AddHttpClient(NflverseHistoricalInjuryProvider.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InjuryOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 30, 180));
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/csv,*/*");
        });
        services.AddSingleton<NflverseHistoricalInjuryProvider>();
        services.AddSingleton<IHistoricalInjuryProvider>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InjuryOptions>>().Value;
            // Mock current catalog uses fixed GUIDs — keep mock historical seeds.
            if (options.Provider == InjuryProviderKind.Mock)
            {
                return sp.GetRequiredService<MockHistoricalInjuryProvider>();
            }

            return options.HistoricalProvider switch
            {
                HistoricalInjuryProviderKind.Nflverse =>
                    sp.GetRequiredService<NflverseHistoricalInjuryProvider>(),
                HistoricalInjuryProviderKind.Mock =>
                    sp.GetRequiredService<MockHistoricalInjuryProvider>(),
                _ => sp.GetRequiredService<NullHistoricalInjuryProvider>()
            };
        });

        services.AddSingleton<NullCollegeInjuryProvider>();
        services.AddSingleton<MockCollegeInjuryProvider>();
        services.AddSingleton<ICollegeInjuryProvider>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<InjuryOptions>>().Value;
            return options.Provider == InjuryProviderKind.Mock
                ? sp.GetRequiredService<MockCollegeInjuryProvider>()
                : sp.GetRequiredService<NullCollegeInjuryProvider>();
        });

        services.AddSingleton<IPlayerInjuryService, PlayerInjuryService>();
    }

    private static void RegisterNews(IServiceCollection services)
    {
        services.AddSingleton<NewsSyncStatus>();
        services.AddSingleton<INewsSyncStatus>(sp => sp.GetRequiredService<NewsSyncStatus>());

        services.AddSingleton<MockNewsProvider>();
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<MockNewsProvider>());

        services.AddHttpClient(LiveNewsProvider.HttpClientName, (sp, client) =>
        {
            var espn = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NewsOptions>>().Value.Espn;
            var baseUrl = string.IsNullOrWhiteSpace(espn.BaseUrl)
                ? "https://site.api.espn.com/apis/site/v2/"
                : espn.BaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(espn.TimeoutSeconds, 5, 120));
            if (!string.IsNullOrWhiteSpace(espn.ApiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", espn.ApiKey);
            }

            // ESPN blocks unknown/custom agents; use a standard browser UA for public reads.
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; Playbook/0.1; +https://github.com/ndoddridge/playbook)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddSingleton<LiveNewsProvider>();
        services.AddSingleton<INewsSource>(sp => sp.GetRequiredService<LiveNewsProvider>());
        services.AddSingleton<INewsProvider, NewsProvider>();
    }
}
