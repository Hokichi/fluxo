using AutoMapper;
using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Mappings;
using Fluxo.Services.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Persistence;

public sealed class CachedPersistenceServiceTests
{
    private static readonly IMapper Mapper = new MapperConfiguration(
        configuration => configuration.AddProfile<EntityDtoProfile>(),
        NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public async Task TransactionRuntimeMethods_UseAppDataAndSaveOneMutationBatch()
    {
        var appData = Substitute.For<IAppDataService>();
        var runner = Substitute.For<IDataOperationRunner>();
        var transaction = new Transaction { Id = 7, Name = "Expense" };
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([transaction]);
        appData.GetTransactionByIdAsync(7, Arg.Any<CancellationToken>()).Returns(transaction);
        var service = new TransactionService(appData, runner, Mapper);

        var items = await service.GetAllAsync();
        await service.DeleteAsync(7);

        Assert.Equal("Expense", Assert.Single(items).Name);
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
        var service = new TransactionService(appData, runner, Mapper);

        await service.PostTerminationCleanupAsync();

        Assert.Single(runner.ReceivedCalls());
        Assert.Empty(appData.ReceivedCalls());
    }

    [Fact]
    public async Task AccountSearchAndAdd_UseAppDataWithMappedEntities()
    {
        var appData = Substitute.For<IAppDataService>();
        var filter = new AccountFilter { EnabledOnly = true };
        appData.SearchAccountsAsync(filter, Arg.Any<CancellationToken>()).Returns([
            new Account { Id = 3, Name = "Checking", IsEnabled = true }
        ]);
        var service = new AccountService(appData, Mapper);

        var results = await service.SearchAsync(filter);
        await service.AddAsync(new AccountDto { Id = 99, Name = "Savings" });

        Assert.Equal("Checking", Assert.Single(results).Name);
        await appData.Received(1).AddAccountAsync(
            Arg.Is<Account>(account => account.Id == 0 && account.Name == "Savings"),
            Arg.Any<CancellationToken>());
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TagUpdateAndRemove_UseCachedLookupAndSaveEachBoundary()
    {
        var appData = Substitute.For<IAppDataService>();
        var tag = new Tag { Id = 4, Name = "Old", HexCode = "#000000" };
        appData.GetTagByIdAsync(4, Arg.Any<CancellationToken>()).Returns(tag);
        var service = new TagService(appData, Mapper);

        await service.UpdateAsync(new TagDto { Id = 4, Name = "New", HexCode = "#FFFFFF" });
        await service.RemoveAsync(4);

        Assert.Equal("New", tag.Name);
        appData.Received(1).UpdateTag(tag);
        appData.Received(1).RemoveTag(tag);
        await appData.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
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
