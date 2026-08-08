using Fluxo.Core.Enums;
using Fluxo.Helpers.Transaction;
using Fluxo.ViewModels.Entities;
using Xunit;

namespace Fluxo.Tests.Helpers.Transaction;

public sealed class TransactionModeHelperTests
{
    [Fact]
    public void Add_state_resolves_requested_entities_and_preserves_typed_values()
    {
        var account = new AccountVM { Id = 2, Name = "Checking" };
        var tag = new TagVM { Id = 3, Name = "Food" };
        var goal = new SavingGoalVM { Id = 4, Name = "Trip" };
        var input = new AddTransactionHelper.Input(
            false, true, "Goal Update", 25m, account.Id, new DateTime(2026, 7, 19), "Note",
            ExpenseCategory.Savings, tag.Id, goal.Id, false, false, true, true);

        var state = AddTransactionHelper.CreateState(input, [account], [tag], [goal]);

        Assert.Same(account, state.SelectedAccount);
        Assert.Same(tag, state.SelectedTag);
        Assert.Same(goal, state.SelectedGoal);
        Assert.Equal(input.Name, state.Name);
        Assert.Equal(input.Amount, state.Amount);
    }

    [Fact]
    public void Add_recurring_state_enables_recurring_mode_and_honors_lock()
    {
        var state = AddRecurringTransactionHelper.CreateState(isLocked: true);

        Assert.True(state.IsRecurring);
        Assert.False(state.IsInstallments);
        Assert.True(state.IsRecurringModeLocked);
        Assert.True(state.IsTransactionTypeLocked);
    }

    [Fact]
    public void Edit_recurring_state_resolves_saved_relationships()
    {
        var account = new AccountVM { Id = 2 };
        var tag = new TagVM { Id = 3 };
        var goal = new SavingGoalVM { Id = 4 };
        var input = new EditRecurringTransactionHelper.Input(
            42, RecurringTransactionType.GoalUpdate, "Goal", 10m, RecurringPeriod.Monthly, 5,
            account.Id, ExpenseCategory.Savings, tag.Id, goal.Id, true);

        var state = EditRecurringTransactionHelper.CreateState(input, [account], [tag], [goal]);

        Assert.Equal(42, state.EditingRecurringTransactionId);
        Assert.True(state.IsGoal);
        Assert.Same(account, state.SelectedAccount);
        Assert.Same(tag, state.SelectedTag);
        Assert.Same(goal, state.SelectedGoal);
        Assert.Equal("5", state.RecurringTimeText);
    }

    [Fact]
    public void View_state_keeps_loaded_identity_and_clears_pending_identity()
    {
        var account = new AccountVM { Id = 2 };
        var transaction = new TransactionVM
        {
            Id = 42,
            SourceAccountId = account.Id,
            Account = account,
            Name = "Coffee",
            Amount = 5m,
            LoggedOn = new DateTime(2026, 7, 19, 10, 0, 0)
        };

        var state = ViewTransactionHelper.CreateState(transaction, [account], [], []);

        Assert.Equal(42, state.LoadedTransaction.Id);
        Assert.Equal(0, state.PendingTransaction.Id);
        Assert.True(state.LoadedTransaction.HasSameValues(state.PendingTransaction));
        Assert.Same(account, state.SelectedAccount);
    }

    [Fact]
    public void Edit_input_maps_pending_business_values()
    {
        var pending = new TransactionVM
        {
            Name = "Lunch",
            Amount = 12m,
            SourceAccountId = 2,
            Tag = new TagVM { Id = 3 },
            ExpenseCategory = ExpenseCategory.Wants,
            OccurredOn = new DateTime(2026, 7, 19),
            Notes = "Team",
            IsPinned = true
        };

        var input = EditTransactionHelper.CreateInput(pending);

        Assert.Equal("Lunch", input.Name);
        Assert.Equal(2, input.AccountId);
        Assert.Equal(3, input.TagId);
        Assert.True(input.IsPinned);
    }

    [Fact]
    public void Processing_navigation_skips_non_pending_targets()
    {
        var states = new[]
        {
            ProcessingTransactionHelper.State.Processed,
            ProcessingTransactionHelper.State.Skipped,
            ProcessingTransactionHelper.State.Pending
        };

        Assert.Equal(2, ProcessingTransactionHelper.FindNextPendingIndex(states, 0));
        Assert.Equal(0, ProcessingTransactionHelper.FindPreviousProcessedIndex(states, 2));
    }
}
