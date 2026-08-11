using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Services.History;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.History;

public sealed class TransactionHistoryActionTests
{
    [Fact]
    public async Task TransactionHistoryAction_DeleteGoalUpdate_RevertRestoresGoalAndSourceTotals()
    {
        var source = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking, Balance = 150m };
        var goal = new SavingGoal { Id = 2, Name = "Emergency", CurrentAmount = 25m };
        var snapshot = GoalUpdateSnapshot(source, goal);
        var (unitOfWork, transactions) = CreateUnitOfWork(source, goal);
        transactions.GetByIdAsync(snapshot.TransactionId, Arg.Any<CancellationToken>())
            .Returns((Transaction?)null);

        await new DeleteTransactionMemoryAction(snapshot).RevertAsync(unitOfWork);

        Assert.Equal(125m, source.Balance);
        Assert.Equal(50m, goal.CurrentAmount);
        await transactions.Received(1).AddAsync(
            Arg.Is<Transaction>(transaction => transaction.GoalId == goal.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransactionHistoryAction_DeleteGoalUpdate_ReapplyReversesGoalAndSourceTotals()
    {
        var source = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking, Balance = 125m };
        var goal = new SavingGoal { Id = 2, Name = "Emergency", CurrentAmount = 50m };
        var snapshot = GoalUpdateSnapshot(source, goal);
        var transaction = AddTransactionMemoryAction.CreateTransaction(snapshot, source);
        var (unitOfWork, transactions) = CreateUnitOfWork(source, goal);
        transactions.GetByIdAsync(snapshot.TransactionId, Arg.Any<CancellationToken>())
            .Returns(transaction);

        await new DeleteTransactionMemoryAction(snapshot).ReapplyAsync(unitOfWork);

        Assert.Equal(150m, source.Balance);
        Assert.Equal(25m, goal.CurrentAmount);
        transactions.Received(1).Remove(transaction);
    }

    private static TransactionMemorySnapshot GoalUpdateSnapshot(Account source, SavingGoal goal) =>
        TransactionMemorySnapshot.Create(new Transaction
        {
            Id = 3,
            Type = TransactionType.Expense,
            SourceAccountId = source.Id,
            Account = source,
            GoalId = goal.Id,
            Name = "Goal Update",
            Amount = 25m,
            OccurredOn = DateTime.Today
        });

    private static (IUnitOfWork UnitOfWork, ITransactionRepository Transactions) CreateUnitOfWork(
        Account source,
        SavingGoal goal)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transactions = Substitute.For<ITransactionRepository>();
        var accounts = Substitute.For<IAccountRepository>();
        var goals = Substitute.For<ISavingGoalRepository>();
        unitOfWork.Transactions.Returns(transactions);
        unitOfWork.Accounts.Returns(accounts);
        unitOfWork.SavingGoals.Returns(goals);
        accounts.GetByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
        goals.GetByIdAsync(goal.Id, Arg.Any<CancellationToken>()).Returns(goal);
        return (unitOfWork, transactions);
    }
}
