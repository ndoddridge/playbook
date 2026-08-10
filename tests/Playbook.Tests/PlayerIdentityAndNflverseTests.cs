using Playbook.Application.Injuries;
using Playbook.Application.Injuries.Interfaces;
using Playbook.Application.Players;
using Playbook.Core.Injuries.Models;
using Playbook.Core.Players;
using Playbook.Infrastructure;
using Playbook.Infrastructure.Injuries;
using Playbook.Infrastructure.Players;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Playbook.Tests;

public class PlayerIdentityAndNflverseTests
{
    [Fact]
    public void Identity_Directory_Resolves_Sleeper_Espn_And_Gsis()
    {
        var directory = new PlayerIdentityDirectory();
        var cmcId = SleeperPlayerIds.ToPlaybookId("4034");
        directory.ReplaceAll(
        [
            new PlaybookPlayerIdentity
            {
                PlaybookId = cmcId,
                FullName = "Christian McCaffrey",
                Team = "SF",
                Position = "RB",
                SleeperId = "4034",
                EspnId = "3117251",
                GsisId = "00-0033280"
            }
        ]);

        Assert.Equal(cmcId, directory.GetBySleeperId("4034")!.PlaybookId);
        Assert.Equal(cmcId, directory.GetByEspnId("3117251")!.PlaybookId);
        Assert.Equal(cmcId, directory.GetByGsisId("00-0033280")!.PlaybookId);
        Assert.Equal(cmcId, directory.ResolveByNameTeam("Christian McCaffrey", "SF")!.PlaybookId);
        Assert.Null(directory.ResolveByNameTeam("Christian McCaffrey", "KC"));
    }

    [Fact]
    public void Multiple_Players_Resolve_To_Distinct_Playbook_Ids()
    {
        var directory = new PlayerIdentityDirectory();
        var a = SleeperPlayerIds.ToPlaybookId("4034");
        var b = SleeperPlayerIds.ToPlaybookId("4046");
        directory.ReplaceAll(
        [
            new PlaybookPlayerIdentity
            {
                PlaybookId = a,
                FullName = "Christian McCaffrey",
                Team = "SF",
                SleeperId = "4034",
                GsisId = "00-0033280",
                EspnId = "3117251"
            },
            new PlaybookPlayerIdentity
            {
                PlaybookId = b,
                FullName = "Saquon Barkley",
                Team = "PHI",
                SleeperId = "4866",
                GsisId = "00-0034844",
                EspnId = "3929630"
            }
        ]);

        Assert.NotEqual(a, b);
        Assert.Equal(a, directory.GetByGsisId("00-0033280")!.PlaybookId);
        Assert.Equal(b, directory.GetByGsisId("00-0034844")!.PlaybookId);
    }

    [Fact]
    public async Task Nflverse_Provider_Maps_Real_Historical_Rows_For_Cmc()
    {
        var directory = new PlayerIdentityDirectory();
        var cmcId = SleeperPlayerIds.ToPlaybookId("4034");
        directory.ReplaceAll(
        [
            new PlaybookPlayerIdentity
            {
                PlaybookId = cmcId,
                FullName = "Christian McCaffrey",
                Team = "SF",
                Position = "RB",
                SleeperId = "4034",
                EspnId = "3117251",
                GsisId = "00-0033280"
            }
        ]);

        var services = new ServiceCollection();
        services.AddSingleton<IPlayerIdentityDirectory>(directory);
        services.AddLogging();
        services.Configure<InjuryOptions>(o =>
        {
            o.Provider = InjuryProviderKind.Live;
            o.HistoricalProvider = HistoricalInjuryProviderKind.Nflverse;
            o.HistoricalSeasonCount = 3;
            o.TimeoutSeconds = 90;
        });
        services.AddHttpClient(NflverseHistoricalInjuryProvider.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(90);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (compatible; PlaybookTests/0.1)");
        });
        services.AddSingleton<NflverseHistoricalInjuryProvider>();
        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<NflverseHistoricalInjuryProvider>();

        var rows = await provider.GetHistoricalInjuriesAsync();
        Assert.True(rows.Count > 0, "Expected nflverse historical rows for CMC GSIS");
        Assert.All(rows, r => Assert.Equal(cmcId, r.PlayerId));
        Assert.All(rows, r => Assert.Equal(InjuryCompetitionLevel.Nfl, r.Level));
        Assert.All(rows, r => Assert.Equal(InjurySourceConfidence.Verified, r.SourceConfidence));
        Assert.All(rows, r => Assert.False(r.IsCurrent));
        Assert.Contains(rows, r =>
            string.Equals(r.BodyPart, "Achilles", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.BodyPart, "Calf", StringComparison.OrdinalIgnoreCase));
        Assert.True(provider.LastMatchedRows > 0);
    }

    [Fact]
    public async Task Live_Pipeline_Loads_Historical_Nfl_For_Known_Players()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlayerData:Provider"] = "Live",
            ["Injuries:Provider"] = "Live",
            ["Injuries:HistoricalProvider"] = "Nflverse",
            ["Injuries:HistoricalSeasonCount"] = "3",
            ["Injuries:CacheFileName"] = $"injuries-live-hist-{Guid.NewGuid():N}.json",
            ["Injuries:CacheTtlMinutes"] = "1",
            ["News:Provider"] = "Mock",
            ["PlayerStats:Provider"] = "Mock",
            ["CollegeStats:Provider"] = "Mock",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddInfrastructure(config);
        await using var sp = services.BuildServiceProvider();

        var players = sp.GetRequiredService<IPlayerService>();
        _ = players.GetAllPlayers();
        var identities = sp.GetRequiredService<IPlayerIdentityDirectory>();
        Assert.True(identities.IdentitiesWithGsisId > 100, "Expected GSIS crosswalk from Sleeper");

        var injuries = sp.GetRequiredService<IPlayerInjuryService>();
        await injuries.RefreshAsync();
        var status = sp.GetRequiredService<IInjurySyncStatus>();

        Assert.Contains("nflverse", status.InjuryProviders, StringComparison.OrdinalIgnoreCase);
        Assert.True(status.NflHistoricalRecords > 0, "Expected real NFL historical injury rows");
        Assert.Equal(0, status.CollegeHistoricalRecords);
        Assert.Contains("College history: not supported", status.ProviderCoverage, StringComparison.OrdinalIgnoreCase);

        var cmc = players.GetAllPlayers().First(p => p.FullName.Contains("McCaffrey", StringComparison.OrdinalIgnoreCase));
        var profile = injuries.GetPlayerInjuryProfile(cmc.Id);
        Assert.Equal(HistoricalDataStatus.Available, profile.NflHistoricalDataStatus);
        Assert.Equal(HistoricalDataStatus.NotSupportedByProvider, profile.CollegeHistoricalDataStatus);
        Assert.NotEmpty(profile.NflCareerHistory);
        Assert.DoesNotContain("not available from the current provider", profile.HistoricalAvailabilityMessage ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.All(profile.NflCareerHistory, e => Assert.Equal(InjurySourceConfidence.Verified, e.Record.SourceConfidence));
    }

    [Fact]
    public void Source_Confidence_Categories_Remain_Distinct()
    {
        Assert.Equal("Verified", new PlayerInjuryRecord
        {
            PlayerId = Guid.NewGuid(),
            Date = DateTimeOffset.UtcNow,
            Status = "Out",
            SourceConfidence = InjurySourceConfidence.Verified
        }.VerificationLabel);

        Assert.Equal("Reported", new UnconfirmedInjurySignal
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            Headline = "Missed practice",
            Source = "ESPN",
            Published = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            Confidence = 70,
            SourceConfidence = InjurySourceConfidence.Reported
        }.VerificationLabel);

        Assert.Equal("Unconfirmed", new UnconfirmedInjurySignal
        {
            Id = Guid.NewGuid(),
            PlayerId = Guid.NewGuid(),
            Headline = "Reportedly dealing with",
            Source = "ESPN",
            Published = DateTimeOffset.UtcNow,
            LastUpdated = DateTimeOffset.UtcNow,
            Confidence = 40,
            SourceConfidence = InjurySourceConfidence.Unconfirmed
        }.VerificationLabel);
    }
}
