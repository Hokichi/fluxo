using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data;
using Fluxo.Data.Context;
using Fluxo.Data.Operations;
using Fluxo.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class AppDatabaseMigrationTests
{
    [Fact]
    public async Task AppDatabaseMigration_ShouldAffectBalanceMigration_BackfillsOnlyExistingIoUs()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            await App.MigrateDatabaseAsync(
                services.GetRequiredService<IDataOperationRunner>(),
                () => databasePath);

            await using (var scope = services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync("20260702131707_AddTransactionLinks");
                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Accounts
                        (Id, Name, AccountType, Balance, IsDefault, IsEnabled, IsForDeletion,
                         MaximumSpending, PinnedOnUI, SpentAmount, AccountLimit)
                    VALUES (7, 'Checking', 0, 100, 0, 1, 0, 0, 0, 0, 0);

                    INSERT INTO Transactions
                        (Id, Type, SourceAccountId, Name, Amount, OccurredOn, LoggedOn, Notes,
                         IsPinned, IsForDeletion, IsIoU, IsExcludedFromBudget)
                    VALUES
                        (41, 0, 7, 'Posted', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 1, 0),
                        (42, 0, 7, 'Regular', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 0, 0);
                    """);
                await migrator.MigrateAsync();
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, ShouldAffectBalance FROM Transactions ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(41, reader.GetInt32(0));
            Assert.True(reader.GetBoolean(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(42, reader.GetInt32(0));
            Assert.False(reader.GetBoolean(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AppDatabaseMigration_AddTransactionLinks_PreservesExistingSourceAccountId()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            await App.MigrateDatabaseAsync(
                services.GetRequiredService<IDataOperationRunner>(),
                () => databasePath);

            await using (var scope = services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync("20260630032006_AddRecurringTransactionEndDate");
                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Accounts
                        (Id, Name, AccountType, Balance, IsDefault, IsEnabled, IsForDeletion,
                         MaximumSpending, PinnedOnUI, SpentAmount, AccountLimit)
                    VALUES (7, 'Checking', 0, 100, 0, 1, 0, 0, 0, 0, 0);

                    INSERT INTO Transactions
                        (Id, Type, AccountId, Name, Amount, OccurredOn, LoggedOn, Notes,
                         IsPinned, IsForDeletion, IsIoU, IsExcludedFromBudget)
                    VALUES (42, 0, 7, 'Existing', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 0, 0);
                    """);
                await migrator.MigrateAsync();
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT SourceAccountId, GoalId, RepaymentAccountId FROM Transactions WHERE Id = 42";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(7, reader.GetInt32(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AppDatabaseMigration_MigrateDatabaseAsync_CreatesCurrentTransactionSchema()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            await App.MigrateDatabaseAsync(services.GetRequiredService<IDataOperationRunner>(), () => databasePath);

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            Assert.True(await ColumnExistsAsync(connection, "Transactions", "LoggedOn"));
            Assert.True(await ColumnExistsAsync(connection, "Transactions", "SourceAccountId"));
            Assert.True(await ColumnExistsAsync(connection, "Transactions", "GoalId"));
            Assert.True(await ColumnExistsAsync(connection, "Transactions", "RepaymentAccountId"));
            Assert.True(await ColumnExistsAsync(connection, "Transactions", "RelatedRecurringTransactionId"));
            Assert.False(await ColumnExistsAsync(connection, "Transactions", "IsExcludedFromBudget"));
            Assert.False(await ColumnExistsAsync(connection, "Transactions", "AccountId"));
            Assert.True(await ColumnExistsAsync(connection, "RecurringTransactions", "EndDate"));
            Assert.False(await ColumnExistsAsync(connection, "RecurringTransactions", "IsExcludedFromBudget"));
            Assert.False(await TableExistsAsync(connection, "Expenses"));
            Assert.False(await TableExistsAsync(connection, "ExpenseLogs"));
            Assert.False(await TableExistsAsync(connection, "IncomeLogs"));
            Assert.False(await TableExistsAsync(connection, "Notifications"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AppDatabaseMigration_MigrateDatabaseAsync_RepairsMissingHistory_ForDatabaseWithoutNotificationsTable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            var runner = services.GetRequiredService<IDataOperationRunner>();
            await App.MigrateDatabaseAsync(runner, () => databasePath);

            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var clearHistory = connection.CreateCommand();
                clearHistory.CommandText = "DELETE FROM \"__EFMigrationsHistory\"";
                await clearHistory.ExecuteNonQueryAsync();
            }

            await App.MigrateDatabaseAsync(runner, () => databasePath);

            await using var verifiedConnection = new SqliteConnection($"Data Source={databasePath}");
            await verifiedConnection.OpenAsync();
            Assert.True(await ColumnExistsAsync(verifiedConnection, "Transactions", "RelatedRecurringTransactionId"));
            Assert.False(await TableExistsAsync(verifiedConnection, "Notifications"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AppDatabaseMigration_ClearParentTransactionCategoriesMigration_ClearsParentsButPreservesChildren()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            await App.MigrateDatabaseAsync(
                services.GetRequiredService<IDataOperationRunner>(),
                () => databasePath);

            await using (var scope = services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync("20260713093244_AddRecurringBudgetExclusionAndRepairTransactionTags");
                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Accounts
                        (Id, Name, AccountType, Balance, IsDefault, IsEnabled, IsForDeletion,
                         MaximumSpending, PinnedOnUI, SpentAmount, AccountLimit)
                    VALUES (7, 'Checking', 0, 100, 0, 1, 0, 0, 0, 0, 0);

                    INSERT INTO Transactions
                        (Id, Type, SourceAccountId, Name, Amount, OccurredOn, LoggedOn, Notes,
                         ExpenseCategory, IsPinned, IsForDeletion, IsIoU, ShouldAffectBalance,
                         IsExcludedFromBudget, ParentTransactionId)
                    VALUES
                        (41, 0, 7, 'Parent', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 0, 0, 0, 0, NULL),
                        (42, 0, 7, 'Child', 10, '2026-07-01', '2026-07-01 12:00:00', '', 1, 0, 0, 0, 0, 0, 41);
                    """);
                await migrator.MigrateAsync("20260713144114_ClearParentTransactionCategories");
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, ExpenseCategory FROM Transactions ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(41, reader.GetInt32(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(42, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AppDatabaseMigration_ExcludeIoUAndIncomeFromBudgetMigration_BackfillsAllRows()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            await App.MigrateDatabaseAsync(
                services.GetRequiredService<IDataOperationRunner>(),
                () => databasePath);

            await using (var scope = services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync("20260713144114_ClearParentTransactionCategories");
                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Accounts
                        (Id, Name, AccountType, Balance, IsDefault, IsEnabled, IsForDeletion,
                         MaximumSpending, PinnedOnUI, SpentAmount, AccountLimit)
                    VALUES (7, 'Checking', 0, 100, 0, 1, 0, 0, 0, 0, 0);

                    INSERT INTO Transactions
                        (Id, Type, SourceAccountId, Name, Amount, OccurredOn, LoggedOn, Notes,
                         IsPinned, IsForDeletion, IsIoU, ShouldAffectBalance, IsExcludedFromBudget)
                    VALUES
                        (41, 0, 7, 'Expense', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 0, 0, 0),
                        (42, 0, 7, 'Posted IoU', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 1, 1, 0),
                        (43, 1, 7, 'Income', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 0, 0, 0),
                        (44, 1, 7, 'Deleted income', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 1, 0, 0, 0);
                    """);
                await migrator.MigrateAsync("20260713151010_ExcludeIoUAndIncomeFromBudget");
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, IsExcludedFromBudget FROM Transactions ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(41, reader.GetInt32(0));
            Assert.False(reader.GetBoolean(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(42, reader.GetInt32(0));
            Assert.True(reader.GetBoolean(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(43, reader.GetInt32(0));
            Assert.True(reader.GetBoolean(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(44, reader.GetInt32(0));
            Assert.True(reader.GetBoolean(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task AppDatabaseMigration_ExcludedCategoryMigration_MapsFlagsAndKeepsSplitParentsCategoryless()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "fluxo.db");
        Directory.CreateDirectory(directory);

        try
        {
            using var services = CreateServiceProvider(databasePath);
            await App.MigrateDatabaseAsync(
                services.GetRequiredService<IDataOperationRunner>(),
                () => databasePath);
            await using (var scope = services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<FluxoDbContext>();
                var migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync("20260713151010_ExcludeIoUAndIncomeFromBudget");
                await context.Database.ExecuteSqlRawAsync("""
                    INSERT INTO Accounts
                        (Id, Name, AccountType, Balance, IsDefault, IsEnabled, IsForDeletion,
                         MaximumSpending, PinnedOnUI, SpentAmount, AccountLimit)
                    VALUES (7, 'Checking', 0, 100, 0, 1, 0, 0, 0, 0, 0);

                    INSERT INTO SavingGoals
                        (Id, Name, TargetAmount, CurrentAmount, CreatedOn)
                    VALUES (9, 'Trip', 100, 10, '2026-07-01');

                    INSERT INTO Transactions
                        (Id, Type, SourceAccountId, Name, Amount, OccurredOn, LoggedOn, Notes,
                         ExpenseCategory, IsPinned, IsForDeletion, IsIoU, ShouldAffectBalance,
                         IsExcludedFromBudget, ParentTransactionId, GoalId, RepaymentAccountId)
                    VALUES
                        (41, 0, 7, 'Parent', 20, '2026-07-01', '2026-07-01 12:00:00', '', 2, 0, 0, 0, 0, 1, NULL, NULL, NULL),
                        (42, 0, 7, 'Child', 10, '2026-07-01', '2026-07-01 12:00:00', '', 2, 0, 0, 0, 0, 1, 41, NULL, NULL),
                        (43, 0, 7, 'Regular', 10, '2026-07-01', '2026-07-01 12:00:00', '', 1, 0, 0, 0, 0, 0, NULL, NULL, NULL),
                        (44, 1, 7, 'Income', 10, '2026-07-01', '2026-07-01 12:00:00', '', NULL, 0, 0, 0, 0, 0, NULL, NULL, NULL),
                        (45, 0, 7, 'IoU', 10, '2026-07-01', '2026-07-01 12:00:00', '', 2, 0, 0, 1, 1, 0, NULL, NULL, NULL),
                        (46, 0, 7, 'Goal', 10, '2026-07-01', '2026-07-01 12:00:00', '', 3, 0, 0, 0, 0, 0, NULL, 9, NULL),
                        (47, 0, 7, 'Repayment', 10, '2026-07-01', '2026-07-01 12:00:00', '', 3, 0, 0, 0, 0, 0, NULL, NULL, 7);

                    INSERT INTO RecurringTransactions
                        (Id, Name, Amount, RecurringPeriod, RecurringTime, Type, Category, SourceId,
                         IsEnabled, IsExcludedFromBudget)
                    VALUES
                        (51, 'Excluded recurring', 10, 1, 1, 1, 1, 7, 1, 1),
                        (52, 'Regular recurring', 10, 1, 1, 1, 2, 7, 1, 0);
                    """);
                await migrator.MigrateAsync();
            }

            await using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync();
            Assert.False(await ColumnExistsAsync(connection, "Transactions", "IsExcludedFromBudget"));
            Assert.False(await ColumnExistsAsync(connection, "RecurringTransactions", "IsExcludedFromBudget"));

            await using var transactionCommand = connection.CreateCommand();
            transactionCommand.CommandText = "SELECT Id, ExpenseCategory FROM Transactions ORDER BY Id";
            await using var transactionReader = await transactionCommand.ExecuteReaderAsync();
            Assert.True(await transactionReader.ReadAsync());
            Assert.True(transactionReader.IsDBNull(1));
            Assert.True(await transactionReader.ReadAsync());
            Assert.Equal(4, transactionReader.GetInt32(1));
            Assert.True(await transactionReader.ReadAsync());
            Assert.Equal(1, transactionReader.GetInt32(1));
            for (var id = 44; id <= 47; id++)
            {
                Assert.True(await transactionReader.ReadAsync());
                Assert.Equal(id, transactionReader.GetInt32(0));
                Assert.Equal(4, transactionReader.GetInt32(1));
            }

            await transactionReader.DisposeAsync();
            await using var recurringCommand = connection.CreateCommand();
            recurringCommand.CommandText = "SELECT Id, Category FROM RecurringTransactions ORDER BY Id";
            await using var recurringReader = await recurringCommand.ExecuteReaderAsync();
            Assert.True(await recurringReader.ReadAsync());
            Assert.Equal(4, recurringReader.GetInt32(1));
            Assert.True(await recurringReader.ReadAsync());
            Assert.Equal(2, recurringReader.GetInt32(1));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    private static ServiceProvider CreateServiceProvider(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILogService>());
        services.AddDbContext<FluxoDbContext>(options => options.UseSqlite(
            $"Data Source={databasePath}", sqlite => sqlite.MigrationsAssembly("fluxo")));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ISavingGoalRepository, SavingGoalRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IRecurringTransactionRepository, RecurringTransactionRepository>();
        services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
        services.AddScoped<IBudgetAllocationRepository, BudgetAllocationRepository>();
        services.AddSingleton<IDataOperationScopeFactory, DataOperationScopeFactory>();
        services.AddSingleton<IDataOperationRunner, DataOperationRunner>();
        return services.BuildServiceProvider();
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
