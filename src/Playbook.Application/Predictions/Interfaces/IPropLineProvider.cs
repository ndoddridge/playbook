using Playbook.Core.Predictions;

namespace Playbook.Application.Predictions.Interfaces;

public interface IPropLineProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<PropLine>> GetPropLinesAsync(CancellationToken cancellationToken = default);
}
