using Fluxo.Core.Enums;

namespace Fluxo.Helpers.Settings;

public static class QuickSetupWizardFlow
{
    private static readonly QuickSetupWizardStep[] Steps =
    [
        QuickSetupWizardStep.Start,
        QuickSetupWizardStep.Name,
        QuickSetupWizardStep.Theme,
        QuickSetupWizardStep.Personalization,
        QuickSetupWizardStep.Notifications,
        QuickSetupWizardStep.BudgetIntroduction,
        QuickSetupWizardStep.BudgetAllocation,
        QuickSetupWizardStep.BudgetConfiguration,
        QuickSetupWizardStep.Accounts,
        QuickSetupWizardStep.Loading,
        QuickSetupWizardStep.End
    ];

    private static readonly IReadOnlyList<QuickSetupWizardStep> PersonalSetupSteps =
    [
        QuickSetupWizardStep.Name,
        QuickSetupWizardStep.Theme,
        QuickSetupWizardStep.Personalization,
        QuickSetupWizardStep.Notifications
    ];

    private static readonly IReadOnlyList<QuickSetupWizardStep> BudgetSetupSteps =
    [
        QuickSetupWizardStep.BudgetAllocation,
        QuickSetupWizardStep.BudgetConfiguration,
        QuickSetupWizardStep.Accounts
    ];

    public static QuickSetupWizardStep GetNext(QuickSetupWizardStep step)
    {
        var index = Steps.IndexOf(step);
        return index < 0 || index == Steps.Length - 1 ? step : Steps[index + 1];
    }

    public static QuickSetupWizardStep GetPrevious(QuickSetupWizardStep step)
    {
        var index = Steps.IndexOf(step);
        return index <= 0 ? step : Steps[index - 1];
    }

    public static IReadOnlyList<QuickSetupWizardStep> GetNavigatorSteps(QuickSetupWizardStep step)
    {
        return PersonalSetupSteps.Contains(step)
            ? PersonalSetupSteps
            : BudgetSetupSteps.Contains(step)
                ? BudgetSetupSteps
                : [];
    }
}
