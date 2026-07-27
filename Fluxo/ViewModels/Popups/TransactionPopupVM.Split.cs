using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Core.Entities;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Popups;

public enum TransactionPopupSidePanel
{
    History,
    Pinned,
    Split
}

public partial class TransactionPopupVM
{
    [ObservableProperty] private TransactionPopupSidePanel _selectedSidePanel = TransactionPopupSidePanel.History;

    public ObservableCollection<TransactionVM> SplitTransactions { get; } = [];
    public bool ShowSidePanelToggle => _popupPurpose is TransactionPopupPurpose.AddNewTransaction or TransactionPopupPurpose.EditTransaction;
    public bool IsHistoryPanelSelected => SelectedSidePanel == TransactionPopupSidePanel.History;
    public bool IsPinnedPanelSelected => SelectedSidePanel == TransactionPopupSidePanel.Pinned;
    public bool IsSplitPanelSelected => !ShowSidePanelToggle || SelectedSidePanel == TransactionPopupSidePanel.Split;
    public bool ShowInvalidSplitPlaceholder => _popupPurpose == TransactionPopupPurpose.AddNewTransaction && !IsCurrentInputValid();
    public decimal SplitAmount => SplitTransactions.Sum(child => child.Amount);
    public decimal SplitAmountRemaining => AmountText;
    public bool HasSplitAmountOverflow => GetSplitParentAmount(null) < SplitTransactions.Sum(child => child.Amount);
    public bool HasSplitTransactions => SplitTransactions.Count > 0;

    internal void NotifySplitDisplayChanged()
    {
        OnPropertyChanged(nameof(ShowInvalidSplitPlaceholder));
        OnPropertyChanged(nameof(SplitAmountRemaining));
    }

    partial void OnSelectedSidePanelChanged(TransactionPopupSidePanel value)
    {
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(ShowPinnedPanel));
        OnPropertyChanged(nameof(ShowSplitPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
    }

    private decimal GetSplitParentAmount(TransactionVM? parent) => parent?.Amount ?? PendingTransaction?.Amount ?? AmountText;

    public async Task LoadSplitTransactionsAsync(int parentTransactionId, CancellationToken cancellationToken)
    {
        var transactions = await _appData.GetTransactionsAsync(cancellationToken);
        var byParent = transactions
            .Where(transaction => !transaction.IsForDeletion)
            .Where(transaction => transaction.ParentTransactionId is not null)
            .GroupBy(transaction => transaction.ParentTransactionId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        SplitTransactions.Clear();
        if (!byParent.TryGetValue(parentTransactionId, out var children))
            return;

        foreach (var child in children)
        {
            var childViewModel = CreateSplitTransactionViewModel(child);
            if (byParent.TryGetValue(child.Id, out var grandchildren))
            {
                foreach (var grandchild in grandchildren)
                    childViewModel.ChildTransactions.Add(CreateSplitTransactionViewModel(grandchild));
            }

            SplitTransactions.Add(childViewModel);
        }

    }

    public async Task<TransactionPopupSubmissionResult> PersistSplitTreeAsync(int rootTransactionId)
    {
        if (!HasSplitTransactions && _removedSplitTransactionIds.Count == 0)
            return TransactionPopupSubmissionResult.Success();
        if (HasSplitOverflow())
            return TransactionPopupSubmissionResult.Failure("Split amounts cannot exceed their parent amount.");
        if (HasUnbalancedSplitAmounts())
            return TransactionPopupSubmissionResult.Failure("Split amounts must equal their parent amount.");

        var account = await _appData.GetAccountByIdAsync(PendingTransaction.SourceAccountId);
        if (account is null)
            return TransactionPopupSubmissionResult.Failure("Please select a valid account.");

        var existing = (await _appData.GetTransactionsAsync(CancellationToken.None))
            .Where(transaction => !transaction.IsForDeletion)
            .ToDictionary(transaction => transaction.Id);
        var existingRootChildIds = existing.Values
            .Where(transaction => transaction.ParentTransactionId == rootTransactionId)
            .Select(transaction => transaction.Id)
            .ToHashSet();
        var retainedIds = new HashSet<int>();

        foreach (var child in SplitTransactions)
        {
            var childEntity = await PersistSplitNodeAsync(child, rootTransactionId, account, existing, retainedIds);
            if (childEntity is null)
                continue;

            foreach (var grandchild in child.ChildTransactions)
                await PersistSplitNodeAsync(grandchild, childEntity.Id, account, existing, retainedIds);
        }

        foreach (var transaction in existing.Values.Where(transaction =>
                     transaction.ParentTransactionId is not null &&
                     !retainedIds.Contains(transaction.Id) &&
                     (transaction.ParentTransactionId == rootTransactionId ||
                      existingRootChildIds.Contains(transaction.ParentTransactionId.Value))))
        {
            transaction.IsForDeletion = true;
            _appData.UpdateTransaction(transaction);
        }

        await _appData.SaveChangesAsync();
        _removedSplitTransactionIds.Clear();
        return TransactionPopupSubmissionResult.Success();
    }

    private readonly HashSet<int> _removedSplitTransactionIds = [];

    private async Task<Transaction?> PersistSplitNodeAsync(
        TransactionVM node,
        int parentTransactionId,
        Account account,
        IReadOnlyDictionary<int, Transaction> existing,
        ISet<int> retainedIds)
    {
        if (node.Amount <= 0m)
            return null;

        var tag = node.IsLeaf && node.Tag is { Id: > 0 }
            ? await _appData.GetTagByIdAsync(node.Tag.Id)
            : null;
        var transaction = node.Id > 0 && existing.TryGetValue(node.Id, out var existingTransaction)
            ? existingTransaction
            : new Transaction();

        transaction.Type = PendingTransaction.Type;
        transaction.SourceAccountId = PendingTransaction.SourceAccountId;
        transaction.Account = account;
        transaction.Name = node.Name.Trim();
        transaction.Amount = node.Amount;
        transaction.OccurredOn = node.OccurredOn == default ? PendingTransaction.OccurredOn : node.OccurredOn;
        transaction.Notes = node.Notes;
        transaction.ExpenseCategory = node.IsLeaf ? node.ExpenseCategory : null;
        transaction.Tag = tag;
        transaction.TagId = tag?.Id;
        transaction.ParentTransactionId = parentTransactionId;
        transaction.IsIoU = node.IsIoU;
        transaction.ShouldAffectBalance = node.ShouldAffectBalance;
        transaction.IsExcludedFromBudget = node.IsExcludedFromBudget;
        transaction.IsForDeletion = false;

        if (transaction.Id > 0)
            _appData.UpdateTransaction(transaction);
        else
        {
            await _appData.AddTransactionAsync(transaction);
            await _appData.SaveChangesAsync();
        }

        if (transaction.Id > 0)
        {
            node.Id = transaction.Id;
            retainedIds.Add(transaction.Id);
        }

        return transaction;
    }

    private bool HasSplitOverflow() =>
        HasSplitAmountOverflow || SplitTransactions.Any(child => child.HasChildAmountOverflow);

    private bool HasUnbalancedSplitAmounts() =>
        SplitTransactions.Count > 0 &&
        (SplitAmount != GetSplitParentAmount(null) || SplitTransactions.Any(HasUnbalancedSplitDescendants));

    private static bool HasUnbalancedSplitDescendants(TransactionVM parent) =>
        parent.ChildTransactions.Count > 0 &&
        (parent.ChildAmountTotal != parent.Amount || parent.ChildTransactions.Any(HasUnbalancedSplitDescendants));

    private static TransactionVM CreateSplitTransactionViewModel(Transaction transaction) => new()
    {
        Id = transaction.Id,
        Type = transaction.Type,
        SourceAccountId = transaction.SourceAccountId,
        Account = transaction.Account is null ? new AccountVM() : new AccountVM { Id = transaction.Account.Id, Name = transaction.Account.Name },
        Name = transaction.Name,
        Amount = transaction.Amount,
        OccurredOn = transaction.OccurredOn,
        Notes = transaction.Notes,
        ExpenseCategory = transaction.ExpenseCategory,
        Tag = transaction.Tag is null ? null : new TagVM { Id = transaction.Tag.Id, Name = transaction.Tag.Name, HexCode = transaction.Tag.HexCode },
        ParentTransactionId = transaction.ParentTransactionId,
        IsIoU = transaction.IsIoU,
        ShouldAffectBalance = transaction.ShouldAffectBalance,
        IsExcludedFromBudget = transaction.IsExcludedFromBudget
    };

}
