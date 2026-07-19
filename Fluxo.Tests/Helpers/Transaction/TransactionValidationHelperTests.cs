using Fluxo.Core.Budgeting;
using Fluxo.Core.Enums;
using Fluxo.Helpers.Transaction;
using Fluxo.ViewModels.Entities;
using Xunit;

namespace Fluxo.Tests.Helpers.Transaction;

public sealed class TransactionValidationHelperTests
{
    [Fact]
    public void Non_positive_amount_is_invalid()
    {
        var result = TransactionValidationHelper.ValidateAmount(0m, false, true, false, null);

        Assert.False(result.IsValid);
        Assert.Equal("Please enter a valid amount greater than zero.", result.ErrorMessage);
    }

    [Fact]
    public void Required_relationships_keep_existing_messages()
    {
        Assert.Equal("Please choose a account.", TransactionValidationHelper.ValidateAccount(null).ErrorMessage);
        Assert.Equal("Please choose a tag.", TransactionValidationHelper.ValidateTag(null, true).ErrorMessage);
        Assert.Equal("Please choose a goal.", TransactionValidationHelper.ValidateGoal(null, true).ErrorMessage);
    }

    [Fact]
    public void Amount_over_available_balance_is_invalid()
    {
        var account = new AccountVM { AccountType = AccountType.Checking, Balance = 20m };

        var result = TransactionValidationHelper.ValidateAmount(25m, false, true, false, account);

        Assert.False(result.IsValid);
        Assert.Equal("Amount exceeds this source's available balance.", result.ErrorMessage);
    }

    [Fact]
    public void Exhausted_category_hard_stops_expense()
    {
        var category = new BudgetAllocationCategoryState(default, 0m, 0m, 0m, 10m, 10m, 0m, 0);

        var result = TransactionValidationHelper.ValidateCategoryBudget(
            OverspendPolicy.HardStop, category, ExpenseCategory.Needs, 1m);

        Assert.False(result.IsValid);
        Assert.Equal("Needs budget is exhausted for this allocation period.", result.ErrorMessage);
    }
}
