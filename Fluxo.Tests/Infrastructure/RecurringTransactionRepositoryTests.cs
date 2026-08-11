using Fluxo.Core.Entities;
using Fluxo.Data.Context;
using Fluxo.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class RecurringTransactionRepositoryTests
{
    [Fact]
    public async Task RecurringTransactionRepository_Queries_ExcludeExpiredEndDates_IncludingTrackedRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FluxoDbContext>().UseSqlite(connection).Options;
        await using var context = new FluxoDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var account = new Account { Name = "Checking", IsEnabled = true };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        var repository = new RecurringTransactionRepository(context);
        var today = DateTime.Today;
        var expired = Create("Expired", today.AddDays(-1), account.Id);
        var current = Create("Current", today, account.Id);
        await repository.AddAsync(expired);
        await repository.AddAsync(current);
        await repository.AddAsync(Create("Future", today.AddDays(1), account.Id));
        await repository.AddAsync(Create("No end", null, account.Id));
        await repository.SaveChangesAsync();

        Assert.Equal(3, (await repository.GetAllAsync()).Count);
        Assert.Null(await repository.GetByIdAsync(expired.Id));
        Assert.NotNull(await repository.GetByIdAsync(current.Id));
    }

    private static RecurringTransaction Create(string name, DateTime? endDate, int sourceId) => new()
    {
        Name = name,
        Amount = 1,
        SourceId = sourceId,
        IsEnabled = true,
        EndDate = endDate
    };
}
