using CommunityToolkit.Mvvm.Messaging;
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
    public void Add_split_selects_and_loads_new_child()
    {
        var viewModel = CreateViewModel();

        viewModel.AddSplit(null);

        var child = Assert.Single(viewModel.SplitTransactions);
        Assert.Equal("New Sub-transaction", child.Name);
        Assert.Equal(0m, child.Amount);
        Assert.Same(child, viewModel.SelectedSplitTransaction);
        Assert.Equal(child.Name, viewModel.NameText);
    }

    [Fact]
    public void Add_split_moves_parent_metadata_to_the_new_leaf()
    {
        var viewModel = CreateViewModel();
        var tag = new TagVM { Id = 1, Name = "Food" };
        viewModel.SelectedTag = tag;

        viewModel.AddSplit(null);

        var child = Assert.Single(viewModel.SplitTransactions);
        Assert.Null(viewModel.PendingTransaction.ExpenseCategory);
        Assert.Null(viewModel.PendingTransaction.Tag);
        Assert.Same(tag, child.Tag);
        Assert.Equal(ExpenseCategory.Needs, child.ExpenseCategory);
    }

    [Fact]
    public void Split_equally_rounds_initial_children_to_integers_and_assigns_remainder_to_last()
    {
        var viewModel = CreateViewModel();
        viewModel.AmountText = 100m;
        viewModel.AddSplit(null);
        viewModel.ReturnToSplitRoot();
        viewModel.AddSplit(null);
        viewModel.ReturnToSplitRoot();
        viewModel.AddSplit(null);

        viewModel.ReturnToSplitRoot();
        viewModel.SplitEqually();

        Assert.Equal([33m, 33m, 34m], viewModel.SplitTransactions.Select(child => child.Amount));
    }

    [Fact]
    public void Overflow_disables_add_split_for_the_overflowing_parent()
    {
        var viewModel = CreateViewModel();
        viewModel.AmountText = 10m;
        viewModel.AddSplit(null);
        viewModel.SelectedSplitTransaction!.Amount = 11m;

        Assert.True(viewModel.HasSplitAmountOverflow);
        Assert.False(viewModel.CanAddSplit(null));
    }

    [Fact]
    public void Return_to_top_is_disabled_when_root_is_selected()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.CanReturnToSplitRoot);

        viewModel.AddSplit(null);

        Assert.True(viewModel.CanReturnToSplitRoot);
        viewModel.ReturnToSplitRoot();
        Assert.False(viewModel.CanReturnToSplitRoot);
    }

    private static TransactionPopupVM CreateViewModel()
    {
        var appData = Substitute.For<IAppDataService>();
        var viewModel = new TransactionPopupVM(appData, new WeakReferenceMessenger());
        viewModel.ConfigureCatalogs([], [], []);
        return viewModel;
    }
}
