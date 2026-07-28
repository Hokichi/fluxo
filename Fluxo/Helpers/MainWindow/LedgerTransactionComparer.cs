using System.Collections;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Shell.Main;

namespace Fluxo.Helpers.MainWindow;

internal sealed class LedgerTransactionComparer(
    LedgerGroupingMode groupingMode,
    LedgerAmountSortDirection sortDirection)
    : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is not LedgerTransactionItemVM left)
            return -1;
        if (y is not LedgerTransactionItemVM right)
            return 1;

        var groupComparison = CompareGroup(left, right);
        if (groupComparison != 0)
            return groupComparison;

        var loggedOnComparison = left.LoggedOn.CompareTo(right.LoggedOn);
        if (sortDirection == LedgerAmountSortDirection.Descending)
            loggedOnComparison *= -1;
        if (loggedOnComparison != 0)
            return loggedOnComparison;

        return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }

    private int CompareGroup(LedgerTransactionItemVM left, LedgerTransactionItemVM right)
    {
        return groupingMode switch
        {
            LedgerGroupingMode.Date => right.OccurredOn.Date.CompareTo(left.OccurredOn.Date),
            LedgerGroupingMode.Tags => string.Compare(left.TagGroupKey, right.TagGroupKey, StringComparison.OrdinalIgnoreCase),
            LedgerGroupingMode.Accounts => string.Compare(left.AccountGroupKey, right.AccountGroupKey, StringComparison.OrdinalIgnoreCase),
            LedgerGroupingMode.Types => 0,
            LedgerGroupingMode.Category => 0,
            _ => 0
        };
    }
}
