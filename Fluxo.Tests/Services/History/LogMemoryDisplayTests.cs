using System.Globalization;
using Fluxo.Core.Enums;
using Fluxo.Services.History;
using Xunit;

namespace Fluxo.Tests.Services.History;

public sealed class LogMemoryDisplayTests
{
    [Fact]
    public void LogMemoryDisplay_AddExpense_IdentifiesTransactionAndSummarizesSnapshot()
    {
        var action = new AddTransactionMemoryAction(Transaction("Grocery", 450m));

        Assert.Equal("Grocery Added", action.Title);
        Assert.Equal("Expense added", action.Summary);
        Assert.Contains("450", action.Details);
        Assert.Contains(new DateTime(2026, 7, 4).ToString("MMM d, yyyy", CultureInfo.CurrentCulture), action.Details);
    }

    [Fact]
    public void LogMemoryDisplay_EditExpense_ListsOnlyChangedDisplayFields()
    {
        var before = Transaction("Grocery", 400m);
        var after = before with { Amount = 450m, Notes = "Weekly shop" };
        var action = new EditTransactionMemoryAction(before, after);

        Assert.Equal("Grocery Updated", action.Title);
        Assert.Equal("Expense information updated", action.Summary);
        Assert.Contains("Amount: 400 → 450", action.Details);
        Assert.Contains("Notes: None → Weekly shop", action.Details);
        Assert.DoesNotContain("Date:", action.Details);
    }

    [Fact]
    public void LogMemoryDisplay_DeleteAccount_IdentifiesAccountAndUsefulValues()
    {
        var action = new DeleteAccountMemoryAction(new AccountMemorySnapshot(
            1, "Checking", AccountType.Checking, 0m, 0m, null, 0m, 1250m,
            null, null, null, true, true));

        Assert.Equal("Checking Deleted", action.Title);
        Assert.Equal("Account deleted", action.Summary);
        Assert.Contains("Checking", action.Details);
        Assert.Contains(1250m.ToString("#,0.##", CultureInfo.CurrentCulture), action.Details);
    }

    [Fact]
    public void LogMemoryDisplay_CompositeAction_SummarizesOperationAndChildren()
    {
        var action = new CompositeLogMemoryAction("Transfer funds",
        [
            new AddTransactionMemoryAction(Transaction("Transfer out", 100m)),
            new AddTransactionMemoryAction(Transaction("Transfer in", 100m) with
            {
                Type = TransactionType.Income
            })
        ]);

        Assert.Equal("Transfer funds Completed", action.Title);
        Assert.Equal("Transfer funds completed", action.Summary);
        Assert.Equal("Transfer out Added · Transfer in Added", action.Details);
    }

    [Fact]
    public void LogMemoryDisplay_LogEntry_ForwardsActionDisplayText()
    {
        var entry = new LogMemoryEntry(new AddTransactionMemoryAction(Transaction("Grocery", 450m)));

        Assert.Equal("Grocery Added", entry.Title);
        Assert.Equal("Expense added", entry.Summary);
        Assert.Contains("450", entry.Details);
    }

    private static TransactionMemorySnapshot Transaction(string name, decimal amount) => new(
        1, TransactionType.Expense, 2, name, amount, new DateTime(2026, 7, 4),
        string.Empty, ExpenseCategory.Needs, null, null, null, null,
        false, false, false);
}
