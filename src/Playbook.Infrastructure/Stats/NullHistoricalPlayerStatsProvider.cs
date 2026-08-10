using Playbook.Application.Stats.Interfaces;

namespace Playbook.Infrastructure.Stats;

public sealed class NullHistoricalPlayerStatsProvider : IHistoricalPlayerStatsProvider
{
    public HistoricalPlayerStatsProviderKind Kind => HistoricalPlayerStatsProviderKind.Null;

    public string DisplayName => "None";

    public bool IsConfigured => false;

    public Task<HistoricalPlayerStatsBatch> GetHistoricalStatsAsync(
        HistoricalPlayerStatsSyncRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new HistoricalPlayerStatsBatch
        {
            Error = "Historical player stats provider is not configured."
        });
}
