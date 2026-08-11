using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Data.Context;
using Fluxo.ViewModels.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class ModelSchemaTests
{
    [Fact]
    public void ModelSchema_FluxoDbContext_ModelIncludesRequestedSchemaChanges()
    {
        using var dbContext = CreateDbContext();
        var model = dbContext.Model;

        var account = model.FindEntityType(typeof(Account))!;
        Assert.NotNull(account.FindProperty(nameof(Account.PinnedOnUI)));
        Assert.Null(account.FindProperty("ShowOnUI"));
        Assert.Equal("NUMERIC", account.FindProperty(nameof(Account.MaximumSpending))!.GetColumnType());
        Assert.False(account.FindProperty(nameof(Account.MaximumSpending))!.IsNullable);
        Assert.Equal("NUMERIC", account.FindProperty(nameof(Account.MinimumPayment))!.GetColumnType());
        Assert.True(account.FindProperty(nameof(Account.MinimumPayment))!.IsNullable);
        Assert.Equal(false, account.FindProperty(nameof(Account.IsDefault))!.GetDefaultValue());
        Assert.Contains(account.GetIndexes(), index =>
            index.IsUnique &&
            index.GetFilter() == "IsDefault = 1" &&
            index.Properties.Single().Name == nameof(Account.IsDefault));

        var tag = model.FindEntityType(typeof(Tag))!;
        Assert.Equal("Tags", tag.GetTableName());
        Assert.Equal("NUMERIC", tag.FindProperty(nameof(Tag.SpendingLimit))!.GetColumnType());
        Assert.True(tag.FindProperty(nameof(Tag.SpendingLimit))!.IsNullable);

        var transaction = model.FindEntityType(typeof(Transaction))!;
        Assert.Equal("Transactions", transaction.GetTableName());
        Assert.Equal(false, transaction.FindProperty(nameof(Transaction.IsIoU))!.GetDefaultValue());
        Assert.Equal(false, transaction.FindProperty(nameof(Transaction.ShouldAffectBalance))!.GetDefaultValue());
        Assert.Null(transaction.FindProperty("IsExcludedFromBudget"));
        Assert.True(transaction.FindProperty(nameof(Transaction.ParentTransactionId))!.IsNullable);
        var relatedRecurringTransaction = transaction.FindProperty(nameof(Transaction.RelatedRecurringTransactionId));
        Assert.NotNull(relatedRecurringTransaction);
        Assert.True(relatedRecurringTransaction!.IsNullable);
        Assert.Contains(transaction.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Any(property => property.Name == nameof(Transaction.RelatedRecurringTransactionId)) &&
            foreignKey.PrincipalEntityType.ClrType == typeof(RecurringTransaction) &&
            foreignKey.DeleteBehavior == DeleteBehavior.SetNull);
        Assert.NotNull(transaction.FindProperty(nameof(Transaction.LoggedOn)));
        Assert.Contains(transaction.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Any(property => property.Name == nameof(Transaction.ParentTransactionId)) &&
            foreignKey.PrincipalEntityType.ClrType == typeof(Transaction) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);
        var savingGoal = model.FindEntityType(typeof(SavingGoal))!;
        Assert.True(savingGoal.FindProperty(nameof(SavingGoal.SavingEndDate))!.IsNullable);

        Assert.Null(savingGoal.FindProperty("RecurringPeriod"));

        var recurringTransaction = model.FindEntityType(typeof(RecurringTransaction))!;
        Assert.False(recurringTransaction.FindProperty("RecurringPeriod")!.IsNullable);
        Assert.False(recurringTransaction.FindProperty("RecurringTime")!.IsNullable);
        Assert.True(recurringTransaction.FindProperty(nameof(RecurringTransaction.Category))!.IsNullable);
        Assert.Null(recurringTransaction.FindProperty("IsExcludedFromBudget"));
        Assert.Null(recurringTransaction.FindProperty("RecurringDate"));

        var budgetAllocation = model.FindEntityType(typeof(BudgetAllocation))!;
        Assert.Equal("BudgetAllocation", budgetAllocation.GetTableName());
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.NeedsThreshold))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.WantsThreshold))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.InvestThreshold))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.AllocationPeriod))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.PeriodStart))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.CurrentPeriodIndex))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.LastRolloverPeriodStart))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.RolloverPolicy))!.IsNullable);
        Assert.False(budgetAllocation.FindProperty(nameof(BudgetAllocation.OverspendPolicy))!.IsNullable);
        Assert.Equal("NUMERIC", budgetAllocation.FindProperty(nameof(BudgetAllocation.AllocationLimit))!.GetColumnType());
        Assert.Equal("NUMERIC", budgetAllocation.FindProperty(nameof(BudgetAllocation.NeedsDebt))!.GetColumnType());
        Assert.Equal("NUMERIC", budgetAllocation.FindProperty(nameof(BudgetAllocation.WantsDebt))!.GetColumnType());
        Assert.Equal("NUMERIC", budgetAllocation.FindProperty(nameof(BudgetAllocation.InvestDebt))!.GetColumnType());

        var singletonKey = budgetAllocation.FindProperty("SingletonKey")!;
        Assert.False(singletonKey.IsNullable);
        Assert.Equal(1, singletonKey.GetDefaultValue());
        Assert.Contains(budgetAllocation.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Count == 1 &&
            string.Equals(index.Properties[0].Name, "SingletonKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelSchema_SaveChanges_ClearsShouldAffectBalanceWhenTransactionIsNotIoU()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();
        var account = new Account { Name = "Checking" };
        var transaction = new Transaction
        {
            Type = TransactionType.Expense,
            Account = account,
            Name = "Regular",
            Amount = 10m,
            OccurredOn = DateTime.Today,
            IsIoU = false,
            ShouldAffectBalance = true
        };
        dbContext.Add(transaction);

        await dbContext.SaveChangesAsync();

        Assert.False(transaction.ShouldAffectBalance);
    }

    [Fact]
    public async Task ModelSchema_DatabaseRejectsBalanceAffectingTransactionThatIsNotIoU_ReturnsVerifiedOutcome()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await dbContext.Database.ExecuteSqlRawAsync("""
            INSERT INTO Accounts
                (Id, Name, AccountType, Balance, IsDefault, IsEnabled, IsForDeletion,
                 MaximumSpending, PinnedOnUI, SpentAmount, AccountLimit)
            VALUES (7, 'Checking', 0, 100, 0, 1, 0, 0, 0, 0, 0);
            """);

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            dbContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO Transactions
                    (Type, SourceAccountId, Name, Amount, OccurredOn, LoggedOn, Notes,
                     IsPinned, IsForDeletion, IsIoU, ShouldAffectBalance)
                VALUES (0, 7, 'Invalid', 10, '2026-07-01', '2026-07-01 12:00:00', '', 0, 0, 0, 1);
                """));
    }

    [Theory]
    [InlineData(TransactionType.Expense)]
    [InlineData(TransactionType.Income)]
    public async Task ModelSchema_SaveChanges_NormalizesOccurredOnAndStampsLoggedOn(TransactionType type)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.OpenConnectionAsync();
        await dbContext.Database.EnsureCreatedAsync();
        var account = new Account { Name = "Checking" };
        dbContext.Accounts.Add(account);
        await dbContext.SaveChangesAsync();

        var transaction = new Transaction
        {
            Type = type,
            Account = account,
            Name = "Test",
            Amount = 1m,
            OccurredOn = new DateTime(2026, 6, 27, 14, 30, 0)
        };
        dbContext.Transactions.Add(transaction);

        var before = DateTime.Now;
        await dbContext.SaveChangesAsync();
        var after = DateTime.Now;

        Assert.Equal(new DateTime(2026, 6, 27), transaction.OccurredOn);
        Assert.InRange(transaction.LoggedOn, before, after);
    }

    [Fact]
    public void ModelSchema_EntitiesAndViewModels_ExposeRequestedProperties()
    {
        var source = new Account
        {
            MaximumSpending = 500m,
            MinimumPayment = 25m,
            PinnedOnUI = true,
            IsDefault = true
        };
        var sourceVm = new AccountVM
        {
            MaximumSpending = source.MaximumSpending,
            MinimumPayment = source.MinimumPayment,
            PinnedOnUI = source.PinnedOnUI,
            IsDefault = source.IsDefault
        };

        Assert.Equal(500m, sourceVm.MaximumSpending);
        Assert.Equal(25m, sourceVm.MinimumPayment);
        Assert.True(sourceVm.PinnedOnUI);
        Assert.True(sourceVm.IsDefault);

        var tag = new Tag { SpendingLimit = 250m };
        var tagVm = new TagVM { SpendingLimit = tag.SpendingLimit };
        Assert.Equal(250m, tagVm.SpendingLimit);

        var transaction = new Transaction { IsPinned = true, ParentTransactionId = 10, IsIoU = true };
        var transactionDto = new TransactionDto
        {
            IsPinned = transaction.IsPinned,
            ParentTransactionId = transaction.ParentTransactionId,
            IsIoU = transaction.IsIoU
        };
        var transactionVm = new TransactionVM
        {
            IsPinned = transaction.IsPinned,
            ParentTransactionId = transactionDto.ParentTransactionId,
            IsIoU = transactionDto.IsIoU
        };
        Assert.True(transactionDto.IsPinned);
        Assert.True(transactionVm.IsPinned);
        Assert.True(transactionDto.IsIoU);
        Assert.True(transactionVm.IsIoU);
        Assert.Equal(10, transactionDto.ParentTransactionId);
        Assert.Equal(10, transactionVm.ParentTransactionId);

        var goal = new SavingGoal { SavingEndDate = null };
        var goalVm = new SavingGoalVM
        {
            SavingEndDate = goal.SavingEndDate
        };

        Assert.Null(goalVm.SavingEndDate);

        var recurringTransaction = new RecurringTransaction
        {
            RecurringPeriod = RecurringPeriod.Biweekly,
            RecurringTime = 5,
            Category = ExpenseCategory.Wants
        };
        var recurringTransactionDto = new RecurringTransactionDto
        {
            Category = recurringTransaction.Category
        };
        var recurringTransactionVm = new RecurringTransactionVM
        {
            RecurringPeriod = recurringTransaction.RecurringPeriod,
            RecurringTime = recurringTransaction.RecurringTime,
            Category = recurringTransaction.Category
        };

        Assert.Null(typeof(SavingGoal).GetProperty("RecurringPeriod"));
        Assert.Null(typeof(SavingGoalVM).GetProperty("RecurringPeriod"));
        Assert.Equal(ExpenseCategory.Wants, recurringTransactionDto.Category);
        Assert.Equal(RecurringPeriod.Biweekly, recurringTransactionVm.RecurringPeriod);
        Assert.Equal(5, recurringTransactionVm.RecurringTime);
        Assert.Equal(ExpenseCategory.Wants, recurringTransactionVm.Category);
    }

    [Fact]
    public void ModelSchema_PeriodEnums_ContainAllowedValues()
    {
        Assert.Equal(
            ["Weekly", "Biweekly", "Monthly", "Quarterly", "Yearly"],
            Enum.GetNames<AllocationPeriod>());

        Assert.Equal(
            ["None", "Weekly", "Biweekly", "Monthly"],
            Enum.GetNames<RecurringPeriod>());

        Assert.Equal(
            ["None", "Matching", "Pooled"],
            Enum.GetNames<RolloverPolicy>());

        Assert.Equal(
            ["Ignore", "SoftDebt", "HardStop"],
            Enum.GetNames<OverspendPolicy>());
    }

    private static FluxoDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FluxoDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        return new FluxoDbContext(options);
    }
}
