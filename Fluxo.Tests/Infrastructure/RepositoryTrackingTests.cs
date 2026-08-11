using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Data.Context;
using Fluxo.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class RepositoryTrackingTests
{
    [Fact]
    public async Task RepositoryTracking_AddTransactionAsync_TracksOnlyRoot_WhenNavigationsAreDetached()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var account = new Account { Id = 1, Name = "Main" };
        var tag = new Tag { Id = 2, Name = "Food", HexCode = "#fff" };
        var transaction = new Transaction
        {
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            TagId = tag.Id,
            Account = account,
            Tag = tag,
            Name = "Lunch",
            Amount = 10m,
            OccurredOn = DateTime.Today
        };

        await new TransactionRepository(fixture.Context).AddAsync(transaction);

        Assert.Equal(EntityState.Added, fixture.Context.Entry(transaction).State);
        Assert.Equal(EntityState.Detached, fixture.Context.Entry(account).State);
        Assert.Equal(EntityState.Detached, fixture.Context.Entry(tag).State);
    }

    [Fact]
    public async Task RepositoryTracking_AddRecurringTransactionAsync_TracksOnlyRoot_WhenNavigationsAreDetached()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var account = new Account { Id = 1, Name = "Main" };
        var tag = new Tag { Id = 2, Name = "Food", HexCode = "#fff" };
        var goal = new SavingGoal { Id = 3, Name = "Trip" };
        var recurring = new RecurringTransaction
        {
            Name = "Rent",
            Amount = 10m,
            SourceId = account.Id,
            TagId = tag.Id,
            GoalId = goal.Id,
            Source = account,
            Tag = tag,
            Goal = goal
        };

        await new RecurringTransactionRepository(fixture.Context).AddAsync(recurring);

        Assert.Equal(EntityState.Added, fixture.Context.Entry(recurring).State);
        Assert.Equal(EntityState.Detached, fixture.Context.Entry(account).State);
        Assert.Equal(EntityState.Detached, fixture.Context.Entry(tag).State);
        Assert.Equal(EntityState.Detached, fixture.Context.Entry(goal).State);
    }

    [Fact]
    public async Task RepositoryTracking_Update_MergesDifferentInstanceWithSameKey()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var tracked = new Account { Id = 7, Name = "Old" };
        fixture.Context.Attach(tracked);

        new AccountRepository(fixture.Context).Update(new Account { Id = 7, Name = "New" });

        Assert.Single(fixture.Context.Accounts.Local);
        Assert.Same(tracked, fixture.Context.Accounts.Local.Single());
        Assert.Equal("New", tracked.Name);
    }

    [Fact]
    public async Task RepositoryTracking_SpecializedRepositories_MergeDifferentInstancesWithSameKey()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var settings = new UserSettings { Name = "theme", Value = "light" };
        var allocation = new BudgetAllocation { Id = 1, NeedsThreshold = 50 };
        fixture.Context.AttachRange(settings, allocation);

        new UserSettingsRepository(fixture.Context).Update(new UserSettings { Name = "theme", Value = "dark" });
        new BudgetAllocationRepository(fixture.Context).Update(new BudgetAllocation { Id = 1, NeedsThreshold = 60 });

        Assert.Single(fixture.Context.UserSettings.Local);
        Assert.Single(fixture.Context.BudgetAllocation.Local);
        Assert.Equal("dark", settings.Value);
        Assert.Equal(60, allocation.NeedsThreshold);
    }

    private sealed class DatabaseFixture(SqliteConnection connection, FluxoDbContext context) : IAsyncDisposable
    {
        public FluxoDbContext Context { get; } = context;

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new FluxoDbContext(
                new DbContextOptionsBuilder<FluxoDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new DatabaseFixture(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
