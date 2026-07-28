using Fluxo.ViewModels.Entities;

namespace Fluxo.Helpers.Transactions;

public static class TransactionSplitHelper
{
    public static bool CanAddChild(TransactionVM root, TransactionVM? parent)
    {
        var parentNode = parent ?? root;
        return (parent is null || root.ChildTransactions.Any(child => ReferenceEquals(child, parent))) &&
               !parentNode.HasChildAmountOverflow;
    }

    public static bool IsValidParent(TransactionVM parent) =>
        !string.IsNullOrWhiteSpace(parent.Name) && parent.Amount > 0m;

    public static TransactionVM AddChild(TransactionVM root, TransactionVM? parent)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (!CanAddChild(root, parent))
            throw new InvalidOperationException("Sub-transactions support two levels only.");

        var child = new TransactionVM
        {
            Type = root.Type,
            SourceAccountId = root.SourceAccountId,
            Account = root.Account,
            Name = "New Sub-transaction",
            Amount = 0m,
            OccurredOn = root.OccurredOn,
            IsIoU = root.IsIoU,
            ShouldAffectBalance = root.ShouldAffectBalance,
            IsExcludedFromBudget = root.IsExcludedFromBudget
        };
        var parentNode = parent ?? root;
        parentNode.ExpenseCategory = null;
        parentNode.Tag = null;
        parentNode.ChildTransactions.Add(child);
        return child;
    }

    public static bool Remove(TransactionVM root, TransactionVM node, out TransactionVM? parent)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(node);

        parent = null;
        if (RemoveReference(root.ChildTransactions, node))
            return true;

        parent = root.ChildTransactions.FirstOrDefault(candidate => RemoveReference(candidate.ChildTransactions, node));
        return parent is not null;
    }

    public static decimal GetRemainingAmount(TransactionVM parent) => parent.Amount - parent.ChildAmountTotal;

    public static void SplitEqually(TransactionVM parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.ChildTransactions.Count < 2)
            return;

        var share = decimal.Round(parent.Amount / parent.ChildTransactions.Count, 0, MidpointRounding.AwayFromZero);
        for (var index = 0; index < parent.ChildTransactions.Count - 1; index++)
            parent.ChildTransactions[index].Amount = share;
        parent.ChildTransactions[^1].Amount = parent.Amount - share * (parent.ChildTransactions.Count - 1);
    }

    public static void Reset(TransactionVM parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        foreach (var child in parent.ChildTransactions)
            child.Amount = 0m;
    }

    public static bool HasOverflow(TransactionVM root) =>
        root.HasChildAmountOverflow || root.ChildTransactions.Any(HasOverflow);

    public static bool IsBalanced(TransactionVM root) =>
        root.ChildTransactions.Count == 0 ||
        (root.ChildAmountTotal == root.Amount && root.ChildTransactions.All(IsBalanced));

    public static bool AreTreesEqual(TransactionVM left, TransactionVM right) =>
        left.ChildTransactions.Count == right.ChildTransactions.Count &&
        left.ChildTransactions.Zip(right.ChildTransactions).All(pair =>
            pair.First.Equals(pair.Second) && AreTreesEqual(pair.First, pair.Second));

    private static bool RemoveReference(IList<TransactionVM> items, TransactionVM node)
    {
        var index = items.ToList().FindIndex(item => ReferenceEquals(item, node));
        if (index < 0)
            return false;

        items.RemoveAt(index);
        return true;
    }
}
