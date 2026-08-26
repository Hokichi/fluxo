using Fluxo.Core.Enums;
using Fluxo.Helpers.Settings;
using Xunit;

namespace Fluxo.Tests.Helpers.Settings;

public sealed class QuickSetupWizardFlowTests
{
    [Fact]
    public void GetNavigatorSteps_Theme_ReturnsPersonalSetupSteps()
    {
        var steps = QuickSetupWizardFlow.GetNavigatorSteps(QuickSetupWizardStep.Theme);

        Assert.Equal(
        [
            QuickSetupWizardStep.Name,
            QuickSetupWizardStep.Theme,
            QuickSetupWizardStep.Personalization,
            QuickSetupWizardStep.Notifications
        ],
        steps);
    }

    [Fact]
    public void GetNavigatorSteps_Accounts_ReturnsBudgetSetupSteps()
    {
        var steps = QuickSetupWizardFlow.GetNavigatorSteps(QuickSetupWizardStep.Accounts);

        Assert.Equal(
        [
            QuickSetupWizardStep.BudgetAllocation,
            QuickSetupWizardStep.BudgetConfiguration,
            QuickSetupWizardStep.Accounts
        ],
        steps);
    }
}
