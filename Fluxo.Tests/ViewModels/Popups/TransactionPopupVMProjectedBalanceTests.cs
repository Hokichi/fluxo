using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionPopupVMProjectedBalanceTests
{
    [Fact]
    public void Edit_amount_that_makes_checking_balance_negative_is_invalid()
    {
        RunInSta(() =>
        {
            var account = new AccountVM
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                Balance = 10m,
                IsEnabled = true
            };
            var vm = new TransactionPopupVM(CreateAppData(), new WeakReferenceMessenger());
            vm.InitializeView(new TransactionVM
            {
                Id = 1,
                Type = TransactionType.Expense,
                SourceAccountId = account.Id,
                Account = account,
                Name = "Groceries",
                Amount = 10m,
                OccurredOn = DateTime.Today,
                ExpenseCategory = ExpenseCategory.Needs,
                Tag = new TagVM { Id = 1, Name = "General" }
            });
            vm.BeginEditingViewedTransactionAsync().GetAwaiter().GetResult();
            vm.AmountText = 25m;

            vm.ValidateAmountField();

            Assert.Contains(vm.GetErrors(nameof(TransactionPopupVM.AmountText)), error =>
                error.ErrorMessage == "Amount exceeds this source's available balance.");
        });
    }

    private static IAppDataService CreateAppData()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Tag>>([new Tag { Id = 1, Name = "General", HexCode = "#22C55E" }]));
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Transaction>>([]));
        return appData;
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception caught) { exception = caught; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
