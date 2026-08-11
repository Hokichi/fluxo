using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Data.Context;
using Fluxo.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class TransactionRepositoryTests
{
    [Fact]
    public async Task TransactionRepository_GetAllAsync_ReturnsActiveTransactionsWithNavigations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FluxoDbContext>().UseSqlite(connection).Options;

        await using (var setup = new FluxoDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            var account = new Account { Name = "Main", IsEnabled = true };
            var tag = new Tag { Name = "Food", HexCode = "#fff" };
            var parent = new Transaction
            {
                Type = TransactionType.Expense, Account = account, Tag = tag, Name = "Lunch",
                Amount = 10m, OccurredOn = DateTime.Today, Notes = "", IsExcludedFromBudget = true
            };
            setup.Transactions.AddRange(parent, new Transaction
            {
                Type = TransactionType.Expense, Account = account, Tag = tag, ParentTransaction = parent,
                Name = "Split", Amount = 5m, OccurredOn = DateTime.Today, Notes = ""
            }, new Transaction
            {
                Type = TransactionType.Income, Account = account, Name = "Deleted", Amount = 1m,
                OccurredOn = DateTime.Today, Notes = "", IsForDeletion = true
            });
            await setup.SaveChangesAsync();
        }

        await using var query = new FluxoDbContext(options);
        var transactions = await new TransactionRepository(query).GetAllAsync();

        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, transaction => Assert.Equal("Main", transaction.Account.Name));
        Assert.Contains(transactions, transaction => transaction.Tag?.Name == "Food");
        Assert.Contains(transactions, transaction => transaction.ParentTransactionId is not null);
    }
}
