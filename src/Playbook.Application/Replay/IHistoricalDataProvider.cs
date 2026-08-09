using Playbook.Core.Leagues;
using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Source-agnostic historical week loader. Implementations (nflverse, etc.) stay isolated
/// from the Knowledge/Decision engines.
/// </summary>
public interface IHistoricalDataProvider
{
    string ProviderId { get; }

    bool Supports(int season, int week);

    Task<HistoricalRawWeekData?> GetWeekAsync(
        int season,
        int week,
        ScoringType scoringType,
        CancellationToken cancellationToken = default);
}

/// <summary>Validates a raw historical week before replay is allowed to proceed.</summary>
public interface IHistoricalWeekDataValidator
{
    void ValidateOrThrow(HistoricalRawWeekData raw);
}

/// <summary>Normalizes external historical IDs into <see cref="HistoricalPlayerIdentity"/>.</summary>
public interface IHistoricalPlayerIdentityNormalizer
{
    HistoricalPlayerIdentity Normalize(
        string gsisId,
        string fullName,
        string position,
        string team,
        int season,
        int week,
        string? sleeperId = null,
        string? espnId = null,
        string? yahooId = null,
        string? rosterStatus = null);

    Guid PlaybookIdFromGsis(string gsisId);
}
