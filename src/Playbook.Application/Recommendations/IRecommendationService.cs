using Playbook.Core.Recommendations;

namespace Playbook.Application.Recommendations;

/// <summary>
/// Single source of recommendations for the application.
/// Engines will feed this contract; UI only consumes it.
/// </summary>
public interface IRecommendationService
{
    IReadOnlyList<Recommendation> GetRecommendations();

    IReadOnlyList<Recommendation> GetTopRecommendations(int count = 5);
}
