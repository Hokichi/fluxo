using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMSplitTests
{
    [Fact]
    public void SelectSplit_saves_current_form_then_loads_root()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.NameText = "Edited child";

        vm.SelectSplitCommand.Execute(null);

        Assert.Equal("Edited child", child.Name);
        Assert.Equal("Root", vm.NameText);
        Assert.Null(vm.SelectedSplitTransaction);
    }

    [Fact]
    public void DeleteSplit_selects_parent_after_grandchild_removal()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        var child = vm.PendingTransaction.ChildTransactions.Single();
        vm.AddSplitCommand.Execute(child);
        var grandchild = child.ChildTransactions.Single();

        vm.DeleteSplitCommand.Execute(grandchild);

        Assert.Equal("New Sub-transaction", vm.SelectedSplitTransaction!.Name);
        Assert.Empty(child.ChildTransactions);
    }

    [Fact]
    public void SplitCommands_keep_root_remaining_amount_in_sync()
    {
        var vm = CreateVm();
        vm.AddSplitCommand.Execute(null);
        vm.AddSplitCommand.Execute(null);

        vm.SplitEquallyCommand.Execute(null);

        Assert.Equal(0m, vm.SplitAmountRemaining);
        Assert.False(vm.HasSplitAmountOverflow);

        vm.ResetSplitCommand.Execute(null);

        Assert.Equal(100m, vm.SplitAmountRemaining);
    }

    [Fact]
    public void AddSplit_keeps_placeholder_hidden_when_root_is_valid()
    {
        var vm = CreateVm();

        vm.AddSplitCommand.Execute(null);

        Assert.False(vm.ShowInvalidSplitPlaceholder);
    }

    [Fact]
    public void AddSplit_shows_placeholder_when_root_is_invalid()
    {
        var vm = CreateVm();
        vm.NameText = string.Empty;

        vm.AddSplitCommand.Execute(null);

        Assert.True(vm.ShowInvalidSplitPlaceholder);
    }

    private static TransactionPopupVM CreateVm()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Account?>(new Account
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                Balance = 500m
            }));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));

        var vm = new TransactionPopupVM(appData, new WeakReferenceMessenger())
        {
            IsExpense = true,
            SelectedAccount = new AccountVM { Id = 1, Name = "Checking", AccountType = AccountType.Checking, Balance = 500m },
            SelectedTag = new TagVM { Id = 1, Name = "General" },
            NameText = "Root",
            AmountText = 100m,
            SelectedDate = new DateTime(2026, 7, 27)
        };
        return vm;
    }
}
