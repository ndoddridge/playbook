using Playbook.Application.News;
using Playbook.Application.Players.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class NewsProviderTests
{
    [Fact]
    public void Mock_News_Returns_Articles_And_Maps_Players()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Mock);
        var news = provider.GetRequiredService<INewsProvider>();
        var status = provider.GetRequiredService<INewsSyncStatus>();

        var latest = news.GetLatest(10);

        Assert.True(latest.Count >= 5);
        Assert.Equal("Mock", status.ConfiguredProvider);
        Assert.Equal("Mock", status.ActiveProvider);
        Assert.False(status.UsedFallback);
        Assert.Contains(latest, a => a.RelatedPlayerIds.Count > 0);

        var withPlayer = latest.First(a => a.RelatedPlayerIds.Count > 0);
        var forPlayer = news.GetForPlayer(withPlayer.RelatedPlayerIds[0]);
        Assert.NotEmpty(forPlayer);
    }

    [Fact]
    public void Live_News_Loads_From_Espn()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Live);
        var news = provider.GetRequiredService<INewsProvider>();
        var status = provider.GetRequiredService<INewsSyncStatus>();

        var latest = news.GetLatest(10);

        Assert.True(latest.Count >= 5);
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.Equal("Live", status.ActiveProvider);
        Assert.False(status.UsedFallback);
        Assert.All(latest, a => Assert.False(string.IsNullOrWhiteSpace(a.Title)));
        Assert.Contains(latest, a => a.Source == "ESPN");
    }

    [Fact]
    public void Live_News_With_Bad_Url_Falls_Back_To_Mock()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            NewsProviderKind.Live,
            espnBaseUrl: "http://127.0.0.1:1/");
        var news = provider.GetRequiredService<INewsProvider>();
        var status = provider.GetRequiredService<INewsSyncStatus>();

        var latest = news.GetLatest(10);

        Assert.True(latest.Count >= 5);
        Assert.Equal("Live", status.ConfiguredProvider);
        Assert.Equal("Mock", status.ActiveProvider);
        Assert.True(status.UsedFallback);
        Assert.False(string.IsNullOrWhiteSpace(status.LastError));
    }
}
