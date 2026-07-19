using Fluxo.Core.Entities;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Popups.Helpers;

internal static class TransactionCatalogProjection
{
    internal static IEnumerable<TagVM> ProjectNonSystemTags(IEnumerable<Tag> tags) =>
        tags
            .Where(tag => !tag.IsSystemTag)
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .Select(tag => new TagVM
            {
                Id = tag.Id,
                Name = tag.Name,
                HexCode = tag.HexCode,
                IsSystemTag = false,
                SpendingLimit = tag.SpendingLimit
            });

    internal static AccountVM ProjectAccount(Account account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        AccountType = account.AccountType,
        AccountLimit = account.AccountLimit,
        MaximumSpending = account.MaximumSpending,
        MinimumPayment = account.MinimumPayment,
        SpentAmount = account.SpentAmount,
        Balance = account.Balance,
        MonthlyDueDate = account.MonthlyDueDate,
        DeductSource = account.DeductSource,
        InterestRate = account.InterestRate,
        PinnedOnUI = account.PinnedOnUI,
        IsEnabled = account.IsEnabled,
        IsDefault = account.IsDefault
    };
}
