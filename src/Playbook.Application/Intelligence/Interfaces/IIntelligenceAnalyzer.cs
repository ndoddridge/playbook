using Playbook.Core.Intelligence.Models;
using Playbook.Core.News;
using Playbook.Core.Players;

namespace Playbook.Application.Intelligence.Interfaces;

/// <summary>
/// Deterministic rule engine: NewsArticle + Player catalog → IntelligenceFact.
/// </summary>
public interface IIntelligenceAnalyzer
{
    IReadOnlyList<IntelligenceFact> Analyze(
        IReadOnlyList<NewsArticle> articles,
        IReadOnlyList<Player> players);
}
