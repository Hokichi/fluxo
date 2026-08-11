using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.Helpers.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public class TransactionPopupVMOrderingTests
{
    [Fact]
    public void TransactionPopupVMOrdering_ProjectNonSystemTags_FiltersSystemTags_AndOrdersByName()
    {
        var tags = new[]
        {
            new Tag { Id = 1, Name = "zeta", HexCode = "#111111", IsSystemTag = false },
            new Tag { Id = 2, Name = "Alpha", HexCode = "#222222", IsSystemTag = false },
            new Tag { Id = 3, Name = "System", HexCode = "#333333", IsSystemTag = true }
        };

        var projected = TransactionCatalogProjection.ProjectNonSystemTags(tags).ToList();

        Assert.Collection(projected,
            first =>
            {
                Assert.Equal(2, first.Id);
                Assert.Equal("Alpha", first.Name);
                Assert.False(first.IsSystemTag);
            },
            second =>
            {
                Assert.Equal(1, second.Id);
                Assert.Equal("zeta", second.Name);
                Assert.False(second.IsSystemTag);
            });
    }

    [Fact]
    public void TransactionPopupVMOrdering_Accounts_AreOrderedByTypeThenConfiguredMetric()
    {
        var sources = new[]
        {
            new AccountVM { Id = 1, Name = "Checking Low", AccountType = AccountType.Checking, Balance = 100m },
            new AccountVM { Id = 2, Name = "Checking High", AccountType = AccountType.Checking, Balance = 500m },
            new AccountVM { Id = 3, Name = "Cash Low", AccountType = AccountType.Cash, Balance = 80m },
            new AccountVM { Id = 4, Name = "Cash High", AccountType = AccountType.Cash, Balance = 200m },
            new AccountVM { Id = 5, Name = "Credit Low Remaining", AccountType = AccountType.Credit, AccountLimit = 1000m, SpentAmount = 900m },
            new AccountVM { Id = 6, Name = "Credit High Remaining", AccountType = AccountType.Credit, AccountLimit = 1000m, SpentAmount = 200m },
            new AccountVM { Id = 7, Name = "Savings Low", AccountType = AccountType.Saving, Balance = 50m },
            new AccountVM { Id = 8, Name = "Savings High", AccountType = AccountType.Saving, Balance = 700m }
        };

        var ordered = sources
            .OrderBy(TransactionPopupVM.GetAccountTypeSortOrder)
            .ThenByDescending(TransactionPopupVM.GetAccountWithinTypeSortValue)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(source => source.Id)
            .ToList();

        Assert.Equal([2, 1, 4, 3, 6, 5, 8, 7], ordered);
    }

    [Fact]
    public void TransactionPopupVMOrdering_BuildTransactionNameSuggestions_MatchesExpenseNamesAnywhere_AndLoadsExpenseData()
    {
        var checking = new Account { Id = 3, Name = "Checking", IsEnabled = true };
        var groceries = new Tag { Id = 7, Name = "Groceries", HexCode = "#22C55E" };
        var logs = new[]
        {
            new Transaction
            {
                Id = 20,
                Amount = 42.50m,
                OccurredOn = new DateTime(2026, 5, 1),
                Notes = "weekly",
                SourceAccountId = checking.Id,
                Account = checking,
                Type = TransactionType.Expense,
                Name = "Market Groceries",
                ExpenseCategory = ExpenseCategory.Needs,
                TagId = groceries.Id,
                Tag = groceries
            },
            new Transaction
            {
                Id = 21,
                Amount = 12m,
                OccurredOn = new DateTime(2026, 5, 2),
                Notes = "coffee",
                SourceAccountId = checking.Id,
                Account = checking,
                Type = TransactionType.Expense,
                Name = "Coffee",
                ExpenseCategory = ExpenseCategory.Wants,
                TagId = groceries.Id,
                Tag = groceries
            }
        };

        var suggestions = TransactionPopupVM.BuildTransactionNameSuggestions(logs, [], isExpense: true, query: "gro").ToList();

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Market Groceries", suggestion.Name);
        Assert.Equal(42.50m, suggestion.Amount);
        Assert.Equal(3, suggestion.AccountId);
        Assert.Equal(ExpenseCategory.Needs, suggestion.Category);
        Assert.Equal(7, suggestion.TagId);
        Assert.Equal("weekly", suggestion.Note);
        Assert.Null(suggestion.Date);
    }

    [Fact]
    public void TransactionPopupVMOrdering_BuildTransactionNameSuggestions_MatchesIncomeNamesAnywhere_AndLoadsIncomeData()
    {
        var checking = new Account { Id = 5, Name = "Checking", IsEnabled = true };
        var logs = new[]
        {
            new Transaction
            {
                Id = 30,
                Type = TransactionType.Income,
                Name = "Monthly Salary",
                Amount = 3000m,
                OccurredOn = new DateTime(2026, 5, 3),
                Notes = "May payroll",
                SourceAccountId = checking.Id,
                Account = checking
            },
            new Transaction
            {
                Id = 31,
                Type = TransactionType.Income,
                Name = "Refund",
                Amount = 14m,
                OccurredOn = new DateTime(2026, 5, 4),
                Notes = "store",
                SourceAccountId = checking.Id,
                Account = checking
            }
        };

        var suggestions = TransactionPopupVM.BuildTransactionNameSuggestions([], logs, isExpense: false, query: "sal").ToList();

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Monthly Salary", suggestion.Name);
        Assert.Equal(3000m, suggestion.Amount);
        Assert.Equal(5, suggestion.AccountId);
        Assert.Null(suggestion.Category);
        Assert.Null(suggestion.TagId);
        Assert.Equal("May payroll", suggestion.Note);
        Assert.Null(suggestion.Date);
    }

    [Fact]
    public void TransactionPopupVMOrdering_BuildTransactionNameSuggestions_RequiresAtLeastThreeCharacters()
    {
        var logs = new[]
        {
            new Transaction
            {
                Id = 30,
                Name = "Monthly Salary",
                Amount = 3000m,
                OccurredOn = new DateTime(2026, 5, 3),
                Notes = "May payroll",
                SourceAccountId = 5,
                Account = new Account { Id = 5, Name = "Checking" }
            }
        };

        var suggestions = TransactionPopupVM.BuildTransactionNameSuggestions([], logs, isExpense: false, query: "sa");

        Assert.Empty(suggestions);
    }

    [Theory]
    [InlineData(RecurringPeriod.None, "", 0)]
    [InlineData(RecurringPeriod.Weekly, "7", 7)]
    [InlineData(RecurringPeriod.Biweekly, "7", 7)]
    [InlineData(RecurringPeriod.Monthly, "28", 28)]
    public void TransactionPopupVMOrdering_TryNormalizeRecurringTime_AcceptsValidValues(RecurringPeriod period, string text, int expected)
    {
        var result = TransactionPopupVM.TryNormalizeRecurringTime(period, text, out var recurringTime);

        Assert.True(result);
        Assert.Equal(expected, recurringTime);
    }

    [Theory]
    [InlineData(RecurringPeriod.Weekly, "8")]
    [InlineData(RecurringPeriod.Biweekly, "8")]
    [InlineData(RecurringPeriod.Monthly, "29")]
    [InlineData(RecurringPeriod.Monthly, "0")]
    public void TransactionPopupVMOrdering_TryNormalizeRecurringTime_RejectsInvalidValues(RecurringPeriod period, string text)
    {
        var result = TransactionPopupVM.TryNormalizeRecurringTime(period, text, out _);

        Assert.False(result);
    }
}
