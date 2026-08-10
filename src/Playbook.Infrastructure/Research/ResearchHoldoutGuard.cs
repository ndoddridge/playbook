using Playbook.Core.Research;

namespace Playbook.Infrastructure.Research;

/// <summary>
/// Refuses unsafe research configurations that would fit on the 2024 holdout
/// or silently include holdout seasons without an explicit allow flag.
/// </summary>
public static class ResearchHoldoutGuard
{
    public static void ValidateSeasonScope(
        IReadOnlyList<int> seasons,
        ResearchScopeMode scopeMode,
        bool allowHoldout,
        bool isFittingOrParameterSelection)
    {
        if (seasons.Count == 0)
        {
            throw new InvalidOperationException("At least one season is required.");
        }

        if (seasons.Any(s => s < 2002 || s > 2100))
        {
            throw new InvalidOperationException("Season out of supported range (2002+).");
        }

        var hasHoldout = ResearchIntegrity.IncludesHoldout(seasons);

        if (isFittingOrParameterSelection && hasHoldout)
        {
            throw new InvalidOperationException(
                $"Refusing to fit/select parameters using holdout season {ResearchIntegrity.HoldoutSeason}. " +
                "Use development seasons only (default 2015/2018/2021), freeze, then evaluate holdout once " +
                "with --scope holdout --allow-holdout.");
        }

        if (scopeMode == ResearchScopeMode.Development && hasHoldout)
        {
            throw new InvalidOperationException(
                $"Development scope cannot include holdout season {ResearchIntegrity.HoldoutSeason}. " +
                "Pass --scope holdout --allow-holdout for an explicit one-shot holdout evaluation.");
        }

        if (hasHoldout && !allowHoldout)
        {
            throw new InvalidOperationException(
                $"Season list includes frozen holdout {ResearchIntegrity.HoldoutSeason}. " +
                "Pass --allow-holdout to acknowledge a one-shot holdout evaluation " +
                "(never use holdout for fitting).");
        }

        if (scopeMode == ResearchScopeMode.Holdout &&
            seasons.Any(s => s != ResearchIntegrity.HoldoutSeason))
        {
            throw new InvalidOperationException(
                $"Holdout scope must contain only season {ResearchIntegrity.HoldoutSeason}.");
        }

        if (scopeMode == ResearchScopeMode.Holdout && !allowHoldout)
        {
            throw new InvalidOperationException(
                "Holdout scope requires --allow-holdout.");
        }
    }

    public static void ValidateExperimentNotSilentlyMutatingProduction(
        ResearchKnowledgeModeLabel mode,
        string? experimentId)
    {
        // Ad-hoc Enhanced outside a catalogued experiment is refused.
        if (mode == ResearchKnowledgeModeLabel.Enhanced &&
            string.IsNullOrWhiteSpace(experimentId))
        {
            throw new InvalidOperationException(
                "Enhanced knowledge mode requires --experiment <id> from the frozen catalog. " +
                "Rejected transforms must not be re-enabled ad hoc. " +
                "Production remains KnowledgeMode.Passthrough.");
        }
    }
}
