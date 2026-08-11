using Fluxo.Core.Enums;
using Fluxo.DataModels.Shell.QuickSetupWizard;
using Xunit;

namespace Fluxo.Tests.DataModels.Shell;

public sealed class QuickSetupWizardRecurringTransactionItemVMTests
{
    [Fact]
    public void QuickSetupWizardRecurringTransactionItemVM_ExcludedCategory_UsesExcludedLabel()
    {
        var draft = new QuickSetupWizardDraftRecurringTransaction(
            1,
            RecurringTransactionType.Income,
            "Salary",
            100m,
            ExpenseCategory.Excluded,
            2,
            RecurringPeriod.Monthly,
            1,
            0,
            "General",
            true);

        var item = new QuickSetupWizardRecurringTransactionItemVM(draft, "Checking");

        Assert.Equal("Excluded", item.CategoryLabel);
    }
}
