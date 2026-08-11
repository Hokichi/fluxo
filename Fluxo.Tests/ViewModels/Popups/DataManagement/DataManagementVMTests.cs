using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups.DataManagement;
using NSubstitute;
using Xunit;
using System.ComponentModel;
using System.IO;

namespace Fluxo.Tests.ViewModels.Popups.DataManagement;

public sealed class DataManagementVMTests
{
    [Fact]
    public void DataManagementVM_AccountsUnchecked_DisablesExpensesIncomesAndRecurringTransactions()
    {
        var vm = new DataManagementVM(Substitute.For<IUserBackupService>());

        vm.SetEntityChecked(DataManagementEntityKind.Accounts, false);

        Assert.False(vm.GetEntity(DataManagementEntityKind.Expenses).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Expenses).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Incomes).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Incomes).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.RecurringTransactions).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.RecurringTransactions).IsChecked);
    }

    [Fact]
    public void DataManagementVM_AccountsUncheckedFromEntityToggle_DisablesExpensesIncomesAndRecurringTransactions()
    {
        var vm = new DataManagementVM(Substitute.For<IUserBackupService>());

        vm.GetEntity(DataManagementEntityKind.Accounts).IsChecked = false;

        Assert.False(vm.GetEntity(DataManagementEntityKind.Expenses).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Expenses).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Incomes).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Incomes).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.RecurringTransactions).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.RecurringTransactions).IsChecked);
    }

    [Fact]
    public void DataManagementVM_ApplyManifest_DisablesMissingEntityGroups()
    {
        var vm = new DataManagementVM(Substitute.For<IUserBackupService>());
        var manifest = new UserBackupManifest(
            1,
            DateTime.UtcNow,
            new HashSet<DataManagementEntityKind> { DataManagementEntityKind.Tags });

        vm.ApplyManifest(manifest);

        Assert.True(vm.GetEntity(DataManagementEntityKind.Tags).IsEnabled);
        Assert.True(vm.GetEntity(DataManagementEntityKind.Tags).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Goals).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Goals).IsChecked);
    }

    [Fact]
    public void DataManagementVM_ApplyManifest_WithAccountsOnly_KeepsDependentsDisabledAndUnchecked()
    {
        var vm = new DataManagementVM(Substitute.For<IUserBackupService>());
        var manifest = new UserBackupManifest(
            1,
            DateTime.UtcNow,
            new HashSet<DataManagementEntityKind> { DataManagementEntityKind.Accounts });

        vm.ApplyManifest(manifest);

        Assert.True(vm.GetEntity(DataManagementEntityKind.Accounts).IsEnabled);
        Assert.True(vm.GetEntity(DataManagementEntityKind.Accounts).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Expenses).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Expenses).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Incomes).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.Incomes).IsChecked);
        Assert.False(vm.GetEntity(DataManagementEntityKind.RecurringTransactions).IsEnabled);
        Assert.False(vm.GetEntity(DataManagementEntityKind.RecurringTransactions).IsChecked);
    }

    [Fact]
    public void DataManagementVM_DecisionSetDirectly_RaisesSelectionPropertyNotifications()
    {
        var item = new DataManagementConflictItemVM(
            new UserBackupConflict("conflict", DataManagementEntityKind.Expenses, "Sample"));
        var changedProperties = new List<string>();

        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                changedProperties.Add(args.PropertyName);
        };

        item.Decision = DataManagementConflictDecision.Append;

        Assert.Contains(nameof(INotifyPropertyChanged.PropertyChanged).Replace("PropertyChanged", "Decision"), changedProperties);
        Assert.Contains(nameof(DataManagementConflictItemVM.IsReplaceSelected), changedProperties);
        Assert.Contains(nameof(DataManagementConflictItemVM.IsAppendSelected), changedProperties);
        Assert.Contains(nameof(DataManagementConflictItemVM.IsIgnoreSelected), changedProperties);
    }

    [Theory]
    [InlineData(DataManagementMode.Append)]
    [InlineData(DataManagementMode.Overwrite)]
    public void DataManagementVM_ChangingFromBackupToRestoreMode_ClearsFilePath(DataManagementMode mode)
    {
        var service = Substitute.For<IUserBackupService>();
        service.BuildDefaultBackupPath(Arg.Any<DateTime>()).Returns("backup.json");
        var vm = new DataManagementVM(service);

        vm.Mode = mode;

        Assert.Equal(string.Empty, vm.FilePath);
    }

    [Theory]
    [InlineData(DataManagementMode.Append)]
    [InlineData(DataManagementMode.Overwrite)]
    public void DataManagementVM_ChangingBackToBackupMode_RestoresPreviousBackupFilePath(DataManagementMode mode)
    {
        var service = Substitute.For<IUserBackupService>();
        service.BuildDefaultBackupPath(Arg.Any<DateTime>()).Returns("backup.json");
        var vm = new DataManagementVM(service)
        {
            FilePath = "custom-backup.json"
        };

        vm.Mode = mode;
        vm.FilePath = "restore.json";
        vm.Mode = DataManagementMode.Backup;

        Assert.Equal("custom-backup.json", vm.FilePath);
    }

    [Fact]
    public async Task DataManagementVM_LoadManifestAsync_InvalidJson_DisablesEntitiesAndShowsInvalidJsonFile()
    {
        var service = Substitute.For<IUserBackupService>();
        service.BuildDefaultBackupPath(Arg.Any<DateTime>()).Returns("backup.json");
        service.ReadManifestAsync("broken.json", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserBackupManifest>(new InvalidDataException("bad json")));
        var vm = new DataManagementVM(service)
        {
            Mode = DataManagementMode.Append
        };

        await vm.LoadManifestAsync("broken.json");

        Assert.Equal("broken.json", vm.FilePath);
        Assert.True(vm.IsFileInvalid);
        Assert.Equal("Invalid JSON file", vm.FileValidationMessage);
        Assert.All(vm.Entities, entity =>
        {
            Assert.False(entity.IsEnabled);
            Assert.False(entity.IsChecked);
        });
    }
}
