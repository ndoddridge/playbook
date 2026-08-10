using Playbook.Application.Players.Data;
using Playbook.Application.Predictions;
using Playbook.Application.Predictions.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Playbook.Tests;

public class PropLineCredentialResolverTests
{
    [Fact]
    public void Primary_Env_Var_Name_Is_PropLines_OddsApi_ApiKey()
    {
        Assert.Equal("PropLines__OddsApi__ApiKey", PropLineCredentialResolver.PrimaryEnvVar);
    }

    [Fact]
    public void ApplyAlias_Does_Not_Overwrite_Existing_Key()
    {
        var options = new PropLineOptions
        {
            OddsApi = { ApiKey = "already-bound" }
        };

        using var _ = TemporaryEnvironment.Set(PropLineCredentialResolver.AliasEnvVarOddsApiKey, "alias-key");
        PropLineCredentialResolver.ApplyAliasEnvironmentVariables(options);

        Assert.Equal("already-bound", options.OddsApi.ApiKey);
        Assert.True(PropLineCredentialResolver.HasApiKey(options));
    }

    [Fact]
    public void ApplyAlias_Fills_From_ODDS_API_KEY_When_Empty()
    {
        var options = new PropLineOptions();
        Assert.False(PropLineCredentialResolver.HasApiKey(options));

        using var _ = TemporaryEnvironment.Set(PropLineCredentialResolver.AliasEnvVarOddsApiKey, "from-alias");
        PropLineCredentialResolver.ApplyAliasEnvironmentVariables(options);

        Assert.Equal("from-alias", options.OddsApi.ApiKey);
        Assert.True(PropLineCredentialResolver.HasApiKey(options));
    }

    [Fact]
    public void Missing_Key_Guidance_Mentions_Primary_Env_Var()
    {
        var guidance = PropLineCredentialResolver.DescribeMissingKeyGuidance();
        Assert.Contains(PropLineCredentialResolver.PrimaryEnvVar, guidance, StringComparison.Ordinal);
        Assert.Contains("the-odds-api.com", guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Live_Without_Key_Reports_ApiKeyConfigured_False()
    {
        using var provider = TestServiceFactory.CreateProvider(
            PlayerDataProviderKind.Mock,
            propLinesProvider: "Live",
            oddsApiKey: "");

        var quickPicks = provider.GetRequiredService<IQuickPicksService>();
        quickPicks.Refresh();
        var status = provider.GetRequiredService<IQuickPicksSyncStatus>();

        Assert.False(status.ApiKeyConfigured);
        Assert.True(status.UsedFallback);
        Assert.Equal("Fallback", status.ProviderStatus);
        Assert.Equal("Mock", status.PropProvider);
    }

    private sealed class TemporaryEnvironment : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        private TemporaryEnvironment(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public static TemporaryEnvironment Set(string name, string? value) => new(name, value);

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
