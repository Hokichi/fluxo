using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Services.History;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.History;

public sealed class TransactionBalanceMemoryActionTests
{
    [Fact]
    public async Task TransactionBalanceMemoryAction_RevertAddedUnpostedIoU_DoesNotChangeAccountBalance()
    {
        var account = new Account { Id = 1, Name = "Checking", Balance = 100m };
        var transaction = new Transaction
        {
            Id = 2,
            Type = TransactionType.Expense,
            Account = account,
            SourceAccountId = account.Id,
            Name = "Unposted lend",
            Amount = 25m,
            IsIoU = true,
            ShouldAffectBalance = false
        };
        var transactions = Substitute.For<ITransactionRepository>();
        transactions.GetByIdAsync(transaction.Id, Arg.Any<CancellationToken>()).Returns(transaction);
        var accounts = Substitute.For<IAccountRepository>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.Transactions.Returns(transactions);
        unitOfWork.Accounts.Returns(accounts);
        var action = new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(transaction));

        await action.RevertAsync(unitOfWork);

        Assert.Equal(100m, account.Balance);
        accounts.DidNotReceive().Update(account);
        transactions.Received(1).Remove(transaction);
    }
}
