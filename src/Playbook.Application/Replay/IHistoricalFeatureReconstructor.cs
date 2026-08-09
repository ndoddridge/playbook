using Playbook.Core.Leagues;
using Playbook.Core.Players;
using Playbook.Core.Replay;

namespace Playbook.Application.Replay;

/// <summary>
/// Builds cutoff-safe player features from prior-game observations only.
/// Source-agnostic: callers supply already-filtered prior games.
/// </summary>
public interface IHistoricalFeatureReconstructor
{
    HistoricalPlayerFeatures Reconstruct(
        Guid playerId,
        string playerName,
        Position position,
        string team,
        int season,
        int targetWeek,
        DateTimeOffset informationCutoff,
        IReadOnlyList<HistoricalGameObservation> priorGames,
        string? roleNote = null);
}

/// <summary>
/// Transparent historical baseline projection model.
/// Must never receive target-week actual outcomes.
/// </summary>
public interface IHistoricalProjectionEngine
{
    string ModelId { get; }

    string ModelLabel { get; }

    HistoricalProjection Project(HistoricalPlayerFeatures features, ScoringType scoringType);
}

/// <summary>
/// Orchestrates feature reconstruction + baseline projections for a player.
/// </summary>
public interface IHistoricalExpectationService
{
    HistoricalProjectionBundle BuildExpectations(
        Guid playerId,
        string playerName,
        Position position,
        string team,
        int season,
        int targetWeek,
        DateTimeOffset informationCutoff,
        IReadOnlyList<HistoricalGameObservation> priorGames,
        ScoringType scoringType,
        string? roleNote = null);
}
