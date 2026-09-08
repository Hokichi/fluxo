using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Persistence;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Persistence;

public sealed class CachedPersistenceServiceTests
{
    [Fact]
    public async Task TransactionRuntimeMethods_UseAppDataAndSaveOneMutationBatch()
    {
        var appData = Substitute.For<IAppDataService>();
        var runner = Substitute.For<IDataOperationRunner>();
        var transaction = new Transaction { Id = 7, Name = "Expense" };
        appData.GetTransactionByIdAsync(7, Arg.Any<CancellationToken>()).Returns(transaction);
        var service = new TransactionService(appData, runner);

        await service.DeleteAsync(7);

        Assert.True(transaction.IsForDeletion);
        appData.Received(1).UpdateTransaction(transaction);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Empty(runner.ReceivedCalls());
    }

    [Fact]
    public async Task TransactionCleanup_RemainsOnPreWarmDataRunner()
    {
        var appData = Substitute.For<IAppDataService>();
        var runner = Substitute.For<IDataOperationRunner>();
        var service = new TransactionService(appData, runner);

        await service.PostTerminationCleanupAsync();

        Assert.Single(runner.ReceivedCalls());
        Assert.Empty(appData.ReceivedCalls());
    }

    [Fact]
    public async Task DeleteMissingTransaction_DoesNotSave()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionByIdAsync(404, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Transaction?>(null));
        var service = new TransactionService(appData, Substitute.For<IDataOperationRunner>());

        await service.DeleteAsync(404);

        appData.DidNotReceive().UpdateTransaction(Arg.Any<Transaction>());
        await appData.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Analytics_UsesCachedListsAndCalculatesPeriodTotals()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([
            new Transaction
            {
                Type = TransactionType.Income,
                Amount = 500m,
                OccurredOn = new DateTime(2026, 8, 10),
                Name = "Income"
            },
            new Transaction
            {
                Type = TransactionType.Expense,
                ExpenseCategory = ExpenseCategory.Needs,
                Amount = 120m,
                OccurredOn = new DateTime(2026, 8, 11),
                Name = "Expense"
            }
        ]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>()).Returns([]);
        var service = new AnalyticsService(appData);

        var result = await service.GetAnalyticsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));

        Assert.Equal(500m, result.TotalIncome);
        Assert.Equal(120m, result.TotalExpense);
    }
}
