using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Backups;
using Xunit;

namespace Fluxo.Tests.Services.Backups;

public sealed class UserBackupServiceOverwriteTests
{
    [Fact]
    public void UserBackupServiceOverwrite_OverwriteRemovalOrder_RemovesDependentsBeforePrincipals()
    {
        var order = UserBackupService.BuildOverwriteRemovalLabels(
            new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Accounts,
                DataManagementEntityKind.Expenses,
                DataManagementEntityKind.Incomes,
                DataManagementEntityKind.RecurringTransactions
            }));

        Assert.True(order.IndexOf("RecurringTransactions") < order.IndexOf("Accounts"));
        Assert.True(order.IndexOf("ExpenseLogs") < order.IndexOf("Accounts"));
        Assert.True(order.IndexOf("Expenses") < order.IndexOf("Accounts"));
        Assert.True(order.IndexOf("IncomeLogs") < order.IndexOf("Accounts"));
    }

    [Fact]
    public void UserBackupServiceOverwrite_OverwriteRemovalOrder_SelectingTagsAlsoRemovesRecurringTransactionsFirst()
    {
        var order = UserBackupService.BuildOverwriteRemovalLabels(
            new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Tags
            }));

        Assert.Equal(["RecurringTransactions", "Tags"], order);
    }

    [Fact]
    public void UserBackupServiceOverwrite_OverwriteRemovalOrder_SelectingGoalsAlsoRemovesRecurringTransactionsFirst()
    {
        var order = UserBackupService.BuildOverwriteRemovalLabels(
            new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Goals
            }));

        Assert.Equal(["RecurringTransactions", "SavingGoals"], order);
    }

    [Fact]
    public void UserBackupServiceOverwrite_OverwriteRemovalOrder_SelectingAccountsIncludesDependentRemovals()
    {
        var order = UserBackupService.BuildOverwriteRemovalLabels(
            new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Accounts
            }));

        Assert.Equal(
            ["RecurringTransactions", "ExpenseLogs", "Expenses", "IncomeLogs", "Accounts"],
            order);
    }

    [Fact]
    public void UserBackupServiceOverwrite_OverwriteRemovalOrder_UserSettingsOnly_RemainsIsolated()
    {
        var order = UserBackupService.BuildOverwriteRemovalLabels(
            new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.UserSettings
            }));

        Assert.Equal(["UserSettings"], order);
    }
}
