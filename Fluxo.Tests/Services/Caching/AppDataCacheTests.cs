using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Exceptions;
using Fluxo.Core.Filters;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Fluxo.Services.Caching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Fluxo.Tests.Services.Caching;

public sealed class AppDataCacheTests
{
    [Fact]
    public async Task ReadBeforeInitialization_RejectsPartialCacheUse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CountingDbCommandInterceptor();
        await using var provider = CacheTestServiceProviderFactory.Create(connection, interceptor);
        var cache = CreateCache(provider);

        await Assert.ThrowsAsync<AppDataCacheNotInitializedException>(() => cache.GetTransactionsAsync());
    }

    [Fact]
    public async Task Initialize_LoadsAllRowsAndRuntimeReadsIssueNoSql()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CountingDbCommandInterceptor();
        await using var provider = CacheTestServiceProviderFactory.Create(connection, interceptor);
        await SeedAsync(provider);
        var cache = CreateCache(provider);

        await cache.InitializeAsync();
        interceptor.Reset();

        var transactions = await cache.GetTransactionsAsync();
        var marked = await cache.GetMarkedTransactionsForDeletionAsync();
        var recurring = await cache.GetRecurringTransactionsAsync();
        var enabledAccounts = await cache.SearchAccountsAsync(new AccountFilter { EnabledOnly = true });
        var tagCounts = await cache.GetTagsByCountDescendingAsync();
        var setting = await cache.GetUserSettingByNameAsync("theme");

        Assert.Single(transactions);
        Assert.Equal("Newest", transactions[0].Name);
        Assert.Single(marked);
        Assert.Equal("Deleted", marked[0].Name);
        Assert.Single(recurring);
        Assert.Equal("Current", recurring[0].Name);
        Assert.Single(enabledAccounts);
        Assert.Equal(1, tagCounts.Single().Count);
        Assert.Equal("dark", setting?.Value);
        Assert.Equal(0, interceptor.CommandCount);
    }

    [Fact]
    public async Task ReadResults_AreDetachedFromSubsequentReads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new CountingDbCommandInterceptor();
        await using var provider = CacheTestServiceProviderFactory.Create(connection, interceptor);
        await SeedAsync(provider);
        var cache = CreateCache(provider);
        await cache.InitializeAsync();

        var first = (await cache.GetTransactionsAsync()).Single();
        first.Name = "Mutated";
        first.Account.Name = "Mutated";

        var second = (await cache.GetTransactionsAsync()).Single();

        Assert.Equal("Newest", second.Name);
        Assert.Equal("Main", second.Account.Name);
    }

    private static AppDataCache CreateCache(IServiceProvider provider)
    {
        var log = provider.GetRequiredService<ILogService>();
        var hydrator = new AppDataCacheHydrator(provider.GetRequiredService<IDataOperationRunner>(), log);
        return new AppDataCache(hydrator, log);
    }

    private static async Task SeedAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
        await db.Database.EnsureCreatedAsync();
        var account = new Account { Name = "Main", IsEnabled = true };
        var disabled = new Account { Name = "Disabled", IsEnabled = false };
        var tag = new Tag { Name = "Food", HexCode = "#ffffff" };
        db.AddRange(account, disabled, tag);
        await db.SaveChangesAsync();
        db.Transactions.AddRange(
            new Transaction
            {
                Type = TransactionType.Expense,
                SourceAccountId = account.Id,
                Account = account,
                Name = "Newest",
                Amount = 10,
                OccurredOn = new DateTime(2026, 8, 21),
                Notes = string.Empty,
                TagId = tag.Id,
                Tag = tag
            },
            new Transaction
            {
                Type = TransactionType.Expense,
                SourceAccountId = account.Id,
                Account = account,
                Name = "Deleted",
                Amount = 5,
                OccurredOn = new DateTime(2026, 8, 20),
                Notes = string.Empty,
                IsForDeletion = true
            });
        db.RecurringTransactions.AddRange(
            new RecurringTransaction
            {
                Name = "Current",
                SourceId = account.Id,
                Source = account,
                EndDate = DateTime.Today.AddDays(1)
            },
            new RecurringTransaction
            {
                Name = "Expired",
                SourceId = account.Id,
                Source = account,
                EndDate = DateTime.Today.AddDays(-1)
            });
        db.UserSettings.Add(new UserSettings { Name = "theme", Value = "dark" });
        db.BudgetAllocation.Add(new BudgetAllocation());
        await db.SaveChangesAsync();
    }
}
