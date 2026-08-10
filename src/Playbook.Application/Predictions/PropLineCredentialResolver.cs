namespace Playbook.Application.Predictions;

/// <summary>
/// Resolves The Odds API key from nested PropLines config or common env aliases.
/// Never logs or returns the raw key to callers that only need presence checks.
/// </summary>
public static class PropLineCredentialResolver
{
    public const string PrimaryEnvVar = "PropLines__OddsApi__ApiKey";
    public const string AliasEnvVarOddsApiKey = "ODDS_API_KEY";
    public const string AliasEnvVarTheOddsApiKey = "THE_ODDS_API_KEY";

    /// <summary>
    /// Applies alias env vars onto <see cref="PropLineOptions"/> when ApiKey is empty.
    /// Primary nested binding (<c>PropLines__OddsApi__ApiKey</c>) is already applied by configuration.
    /// </summary>
    public static void ApplyAliasEnvironmentVariables(PropLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.OddsApi.ApiKey))
        {
            return;
        }

        var alias = Environment.GetEnvironmentVariable(AliasEnvVarOddsApiKey)
                    ?? Environment.GetEnvironmentVariable(AliasEnvVarTheOddsApiKey);

        if (!string.IsNullOrWhiteSpace(alias))
        {
            options.OddsApi.ApiKey = alias.Trim();
        }
    }

    public static bool HasApiKey(PropLineOptions options) =>
        !string.IsNullOrWhiteSpace(options.OddsApi.ApiKey);

    public static string DescribeMissingKeyGuidance() =>
        $"Set {PrimaryEnvVar} (preferred), or {AliasEnvVarOddsApiKey} / {AliasEnvVarTheOddsApiKey}, " +
        "or user-secret PropLines:OddsApi:ApiKey. Get a key at https://the-odds-api.com/";
}
