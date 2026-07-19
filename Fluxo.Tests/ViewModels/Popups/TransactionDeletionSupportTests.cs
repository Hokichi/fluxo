using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Entities;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Resources.Resources.Messages;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransactionDeletionSupportTests
{
    [Fact]
    public void PublishRepaymentReversalNotification_PublishesOneSingularNotification()
    {
        var messenger = new WeakReferenceMessenger();
        var recipient = new object();
        var requests = new List<FloatingNotificationRequest>();
        messenger.Register<ShowFloatingNotificationMessage>(recipient, (_, message) => requests.Add(message.Value));
        TransactionSplitVM.PublishRepaymentReversalNotification(messenger, "Visa");

        var request = Assert.Single(requests);
        Assert.Equal("Repayment for Visa reversed.", request.Header);
        Assert.Equal(string.Empty, request.Message);
    }

    [Fact]
    public void GetDeleteConfirmationMessage_RepaymentWarnsThatBothTransactionsAreDeleted()
    {
        var transaction = new TransactionVM
        {
            Type = TransactionType.Expense,
            RepaymentAccountId = 2,
            Tag = new TagVM { Name = "Balance Update" }
        };

        Assert.Equal(
            "Reverse this repayment? Both repayment transactions will be deleted.",
            TransactionSplitVM.GetDeleteConfirmationMessage(transaction));
    }

    [Fact]
    public async Task BuildDeletionPlanAsync_GoalUpdateResolvesLinkedGoal()
    {
        var goal = new SavingGoal { Id = 4, Name = "Emergency", CurrentAmount = 100m };
        var transaction = new Transaction
        {
            Id = 5,
            Type = TransactionType.Expense,
            GoalId = goal.Id,
            Tag = new Tag { Name = GoalUpdateTransactionSupport.GoalUpdateTagName, HexCode = "#fff" }
        };
        var appData = Substitute.For<IAppDataService>();
        appData.GetSavingGoalByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(goal);

        var plan = await TransactionSplitVM.BuildDeletionPlanAsync(appData, transaction, CancellationToken.None);

        Assert.True(plan.IsSuccess);
        Assert.Same(goal, plan.Goal);
        Assert.Same(transaction, Assert.Single(plan.Transactions));
    }

    [Fact]
    public async Task BuildDeletionPlanAsync_RepaymentFailsWithoutMatchingIncome()
    {
        var expense = new Transaction
        {
            Type = TransactionType.Expense,
            Account = new Account { Id = 1, Name = "Checking" },
            RepaymentAccountId = 2,
            Tag = new Tag { Name = "Balance Update", HexCode = "#fff" }
        };
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var plan = await TransactionSplitVM.BuildDeletionPlanAsync(appData, expense, CancellationToken.None);

        Assert.False(plan.IsSuccess);
        Assert.Empty(plan.Transactions);
    }

    [Fact]
    public async Task BuildDeletionPlanAsync_RepaymentSelectsNewestMatchingIncome()
    {
        var checking = new Account { Id = 1, Name = "Checking" };
        var credit = new Account { Id = 2, Name = "Visa", AccountType = AccountType.Credit };
        var expense = new Transaction
        {
            Id = 1,
            Type = TransactionType.Expense,
            Name = "Repayment to Visa",
            Account = checking,
            SourceAccountId = checking.Id,
            RepaymentAccountId = credit.Id,
            RepaymentAccount = credit,
            Amount = 75m,
            OccurredOn = new DateTime(2026, 7, 2),
            Tag = new Tag { Id = 3, Name = "Balance Update", HexCode = "#fff" }
        };
        var older = Income(10, credit, new DateTime(2026, 7, 2, 9, 0, 0));
        var newer = Income(11, credit, new DateTime(2026, 7, 2, 10, 0, 0));
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns([older, newer]);

        var plan = await TransactionSplitVM.BuildDeletionPlanAsync(
            appData,
            expense,
            CancellationToken.None);

        Assert.True(plan.IsSuccess);
        Assert.Equal([expense.Id, newer.Id], plan.Transactions.Select(item => item.Id));
    }

    private static Transaction Income(int id, Account credit, DateTime loggedOn) => new()
    {
        Id = id,
        Type = TransactionType.Income,
        Name = "Repayment from Checking",
        Account = credit,
        SourceAccountId = credit.Id,
        RepaymentAccountId = credit.Id,
        Amount = 75m,
        OccurredOn = new DateTime(2026, 7, 2),
        LoggedOn = loggedOn
    };
}
