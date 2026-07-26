using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Popups;

public enum TransactionPopupSidePanel
{
    History,
    Split
}

public partial class TransactionPopupVM
{
    private bool _isLoadingSplitTransaction;
    [ObservableProperty] private TransactionVM? _selectedSplitTransaction;
    [ObservableProperty] private TransactionPopupSidePanel _selectedSidePanel = TransactionPopupSidePanel.History;

    public ObservableCollection<TransactionVM> SplitTransactions { get; } = [];
    public bool IsSplitRootSelected => SelectedSplitTransaction is null;
    public bool CanReturnToSplitRoot => !IsSplitRootSelected;
    public bool CanSplitSelectedTransaction => SelectedSplitChildren.Count > 0;
    public bool CanSelectSplitAccount => SelectedSplitTransaction is null;
    public bool IsSelectedSplitLeaf => SelectedSplitTransaction?.IsLeaf ?? SplitTransactions.Count == 0;
    public bool CanAddSplitAtRoot => CanAddSplit(null);
    public bool ShowSidePanelToggle => _popupPurpose is TransactionPopupPurpose.AddNewTransaction or TransactionPopupPurpose.EditTransaction;
    public bool IsHistoryPanelSelected => SelectedSidePanel == TransactionPopupSidePanel.History;
    public bool IsSplitPanelSelected => !ShowSidePanelToggle || SelectedSidePanel == TransactionPopupSidePanel.Split;
    public decimal SplitAmount => SplitTransactions.Sum(child => child.Amount);
    public bool HasSplitAmountOverflow => GetSplitParentAmount(null) < SplitTransactions.Sum(child => child.Amount);
    public bool HasSplitTransactions => SplitTransactions.Count > 0;

    private IList<TransactionVM> SelectedSplitChildren =>
        SelectedSplitTransaction?.ChildTransactions ?? SplitTransactions;

    [RelayCommand(CanExecute = nameof(CanReturnToSplitRoot))]
    public void ReturnToSplitRoot() => SelectSplitTransaction(null);

    [RelayCommand(CanExecute = nameof(CanAddSplit))]
    public void AddSplit(TransactionVM? parent)
    {
        EnsureTransactionState();
        if (!CanAddSplit(parent))
            return;

        SyncCurrentSplitTransaction();
        var source = parent ?? PendingTransaction;
        var child = new TransactionVM
        {
            Type = PendingTransaction.Type,
            SourceAccountId = PendingTransaction.SourceAccountId,
            Account = PendingTransaction.Account,
            Name = "New Sub-transaction",
            OccurredOn = PendingTransaction.OccurredOn,
            ExpenseCategory = source.ExpenseCategory,
            Tag = source.Tag,
            IsIoU = source.IsIoU,
            ShouldAffectBalance = source.ShouldAffectBalance,
            IsExcludedFromBudget = source.IsExcludedFromBudget
        };

        if (parent is null)
        {
            PendingTransaction.ExpenseCategory = null;
            PendingTransaction.Tag = null;
            SplitTransactions.Add(child);
        }
        else
        {
            parent.ExpenseCategory = null;
            parent.Tag = null;
            parent.ChildTransactions.Add(child);
        }

        SelectedSplitTransaction = child;
        LoadSplitTransactionIntoForm(child);
        NotifySplitStateChanged();
    }

    public bool CanAddSplit(TransactionVM? parent)
    {
        if (parent is not null && !SplitTransactions.Contains(parent))
            return false;

        var amount = GetSplitParentAmount(parent);
        var childTotal = parent?.ChildAmountTotal ?? SplitTransactions.Sum(child => child.Amount);
        return childTotal <= amount;
    }

    [RelayCommand(CanExecute = nameof(CanSplitSelectedTransaction))]
    public void SplitEqually()
    {
        var children = SelectedSplitChildren;
        if (children.Count == 0)
            return;

        var amount = GetSplitParentAmount(SelectedSplitTransaction);
        var share = decimal.Round(amount / children.Count, 0, MidpointRounding.AwayFromZero);
        for (var index = 0; index < children.Count - 1; index++)
            children[index].Amount = share;
        children[^1].Amount = amount - share * (children.Count - 1);

        NotifySplitStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSplitSelectedTransaction))]
    public void ResetSplit()
    {
        foreach (var child in SelectedSplitChildren)
            child.Amount = 0m;

        NotifySplitStateChanged();
    }

    public void SelectSplitTransaction(TransactionVM? transaction)
    {
        EnsureTransactionState();
        SyncCurrentSplitTransaction();
        SelectedSplitTransaction = transaction;

        var source = transaction ?? PendingTransaction;
        LoadSplitTransactionIntoForm(source);
        NotifySplitStateChanged();
    }

    partial void OnSelectedSplitTransactionChanged(TransactionVM? value) => NotifySplitStateChanged();

    partial void OnSelectedSidePanelChanged(TransactionPopupSidePanel value)
    {
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(ShowSplitPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
    }

    private void SyncCurrentSplitTransaction()
    {
        if (SelectedSplitTransaction is null)
        {
            SyncPendingTransactionFromForm();
            return;
        }

        var target = SelectedSplitTransaction;
        target.Name = NameText;
        target.Amount = AmountText;
        target.Notes = NoteText;
        target.OccurredOn = SelectedDate.Date;
        target.ExpenseCategory = SelectedExpenseCategory;
        target.Tag = SelectedTag;
        target.IsIoU = IsIoU;
        target.ShouldAffectBalance = ShouldAffectBalance;
        target.IsExcludedFromBudget = IsExcludedFromBudget;
    }

    private void LoadSplitTransactionIntoForm(TransactionVM transaction)
    {
        _isLoadingSplitTransaction = true;
        try
        {
            IsExpense = transaction.Type != TransactionType.Income;
            IsGoal = false;
            IsRepayment = false;
            NameText = transaction.Name;
            AmountText = transaction.Amount;
            NoteText = transaction.Notes;
            SelectedDate = transaction.OccurredOn == default ? DateTime.Today : transaction.OccurredOn.Date;
            SelectedExpenseCategory = transaction.ExpenseCategory ?? ExpenseCategory.Needs;
            SelectedTag = transaction.Tag;
            IsIoU = transaction.IsIoU;
            ShouldAffectBalance = transaction.ShouldAffectBalance;
            IsExcludedFromBudget = transaction.IsExcludedFromBudget;
        }
        finally
        {
            _isLoadingSplitTransaction = false;
        }
    }

    private decimal GetSplitParentAmount(TransactionVM? parent) => parent?.Amount ?? PendingTransaction.Amount;

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
        {
            NotifySplitStateChanged();
            return;
        }

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

        NotifySplitStateChanged();
    }

    public async Task<TransactionPopupSubmissionResult> PersistSplitTreeAsync(int rootTransactionId)
    {
        if (!HasSplitTransactions && _removedSplitTransactionIds.Count == 0)
            return TransactionPopupSubmissionResult.Success();
        if (HasSplitOverflow())
            return TransactionPopupSubmissionResult.Failure("Split amounts cannot exceed their parent amount.");

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

    [RelayCommand]
    public void DeleteSplit(TransactionVM transaction)
    {
        if (transaction.Id > 0)
            _removedSplitTransactionIds.Add(transaction.Id);

        foreach (var parent in SplitTransactions)
        {
            if (parent.ChildTransactions.Remove(transaction))
            {
                SelectSplitTransaction(parent);
                NotifySplitStateChanged();
                return;
            }
        }

        if (SplitTransactions.Remove(transaction))
            ReturnToSplitRoot();

        NotifySplitStateChanged();
    }

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

    private void NotifySplitStateChanged()
    {
        OnPropertyChanged(nameof(IsSplitRootSelected));
        OnPropertyChanged(nameof(CanReturnToSplitRoot));
        OnPropertyChanged(nameof(CanSplitSelectedTransaction));
        OnPropertyChanged(nameof(CanSelectSplitAccount));
        OnPropertyChanged(nameof(IsSelectedSplitLeaf));
        OnPropertyChanged(nameof(CanEditCategory));
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(CanAddSplitAtRoot));
        OnPropertyChanged(nameof(HasSplitAmountOverflow));
        OnPropertyChanged(nameof(HasSplitTransactions));
        OnPropertyChanged(nameof(SplitAmount));
        OnPropertyChanged(nameof(ShowSidePanelToggle));
        OnPropertyChanged(nameof(IsHistoryPanelSelected));
        OnPropertyChanged(nameof(IsSplitPanelSelected));
        OnPropertyChanged(nameof(ShowCategoryImpact));
        OnPropertyChanged(nameof(ShowAccountImpact));
        OnPropertyChanged(nameof(CanAddSplit));
        ReturnToSplitRootCommand.NotifyCanExecuteChanged();
        AddSplitCommand.NotifyCanExecuteChanged();
        SplitEquallyCommand.NotifyCanExecuteChanged();
        ResetSplitCommand.NotifyCanExecuteChanged();
    }
}
