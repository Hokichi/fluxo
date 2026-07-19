using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Transactions;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups.Helpers;
using Fluxo.ViewModels.Shell;

namespace Fluxo.ViewModels.Popups;

public partial class TransactionSplitVM : ObservableObject
{
    private const int DefaultVisibleTagSlots = 4;
    private readonly List<AccountVM> _availableAccounts = [];
    private readonly TransactionVM _transaction;
    private readonly IMessenger _messenger;
    private readonly List<TagVM> _orderedTags = [];
    private readonly List<TransactionSplitRowVM> _removedSplitRows = [];
    private readonly List<TransactionSplitRowVM> _savedSplitRows = [];
    private readonly IAppDataService _appData;

    [ObservableProperty] private decimal _amountText;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isIoU;
    [ObservableProperty] private bool _shouldAffectBalance;
    [ObservableProperty] private bool _isExcludedFromBudget;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isSplitMode;
    [ObservableProperty] private bool _isSplitEquallyEnabled;
    [ObservableProperty] private bool _isMoreTagsOpen;
    [ObservableProperty] private bool _hasNegativeSplitRemainder;
    [ObservableProperty] private decimal _splitTotalAmount;
    [ObservableProperty] private decimal _splitRemainingAmount;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private TransactionSplitRowVM? _negativeRemainderRow;
    private bool _areSplitRowsLoaded;
    private bool _isUpdatingTagCollections;
    private int _visibleTagSlots = DefaultVisibleTagSlots;
    [ObservableProperty] private string _nameText = string.Empty;
    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private string _popupTitle = "Transaction Detail";

    private TransactionDetailSavedState _savedState = new(string.Empty, 0m, false, string.Empty, DateTime.Today,
        ExpenseCategory.Needs, 0, 0, false, false, false);

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private ExpenseCategory _selectedExpenseCategory = ExpenseCategory.Needs;
    [ObservableProperty] private AccountVM? _selectedAccount;
    [ObservableProperty] private TagVM? _selectedTag;

    public TransactionSplitVM(TransactionVM transaction, IAppDataService appData, IMessenger messenger)
    {
        _transaction = transaction;
        _appData = appData;
        _messenger = messenger;
        AccountsView = AccountComboBoxViewFactory.CreateGroupedByTypeThenName(
            Accounts,
            nameof(AccountVM.TypeDisplayName),
            nameof(AccountVM.AccountType),
            nameof(AccountVM.Name));

        _savedState = CreateSavedState(transaction);
        LoadFromSavedState();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _availableAccounts.Clear();
        _availableAccounts.AddRange((await _appData.GetAccountsAsync(cancellationToken))
            .Select(TransactionPopupVM.ProjectAccount)
            .Where(source => source.IsEnabled));
        _orderedTags.Clear();
        _orderedTags.AddRange(TransactionPopupVM.ProjectNonSystemTags(
            await _appData.GetTagsAsync(cancellationToken)));
        RefreshTagCollections();
        RefreshAccounts();
    }

    public void RequestParentRefresh() =>
        _messenger.Send(new TransactionPopupRefreshRequestedMessage(_transaction.Id));

    public IReadOnlyList<ExpenseCategoryOption> ExpenseCategories { get; } =
    [
        new("Needs", ExpenseCategory.Needs),
        new("Wants", ExpenseCategory.Wants),
        new("Invest", ExpenseCategory.Savings)
    ];

    public string DeleteConfirmationMessage => GetDeleteConfirmationMessage(_transaction);

    internal static string GetDeleteConfirmationMessage(TransactionVM transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return transaction.Type == TransactionType.Expense &&
               transaction.RepaymentAccountId is not null &&
               string.Equals(transaction.Tag?.Name, "Balance Update", StringComparison.OrdinalIgnoreCase)
            ? "Reverse this repayment? Both repayment transactions will be deleted."
            : "Delete this transaction?";
    }

    public ObservableCollection<AccountVM> Accounts { get; } = [];
    public ICollectionView AccountsView { get; }
    public ObservableCollection<TransactionSplitRowVM> SplitRows { get; } = [];
    public ObservableCollection<TransactionDetailChildTransactionVM> ChildTransactions { get; } = [];
    public ObservableCollection<TagVM> VisibleTags { get; } = [];
    public ObservableCollection<TagVM> OverflowTags { get; } = [];

    public bool AreFieldsReadOnly => !IsEditing;
    public bool CanEditFields => IsEditing;
    public bool IsSystemTagged => _transaction.Tag?.IsSystemTag == true;
    public bool ShowTagSelection => ShowNormalTransactionFields && !HasChildTransactions;

    public bool IsRegularMode
    {
        get => !IsIoU;
        set { if (value) IsIoU = false; }
    }

    public bool IsUnpostedIoUMode
    {
        get => IsIoU && !ShouldAffectBalance;
        set
        {
            if (!value) return;
            IsIoU = false;
            IsIoU = true;
        }
    }

    public bool IsPostedIoUMode
    {
        get => IsIoU && ShouldAffectBalance;
        set
        {
            if (!value) return;
            IsIoU = true;
            ShouldAffectBalance = true;
        }
    }

    public string TransactionModeDescription =>
        GetTransactionModeDescription(IsIoU, ShouldAffectBalance);

    public bool IsExpense => _transaction.Type == TransactionType.Expense;
    public string IoUTooltip => IsExpense ? "Set as lend" : "Set as debt";
    public bool IsCategoryEnabled => IsEditing && IsExpense;
    public bool ShowCategoryField => IsExpense && !IsExcludedFromBudget;
    public bool ShouldExpandAccountField => !ShowCategoryField;
    public bool HasMoreTags => OverflowTags.Count > 0;
    public bool HasSplitRows => SplitRows.Count > 0;
    public bool HasSplitRowsWithAmounts => SplitRows.Any(row => row.HasAmount);
    public bool HasSplitRowsWithoutAmounts => SplitRows.Count > 0 && SplitRows.All(row => !row.HasAmount);
    public bool HasChildTransactions => ChildTransactions.Count > 0;
    public bool ShowChildTransactions => HasChildTransactions && !IsSplitMode;
    public bool ShowSplitButton => !IsEditing;

    public bool HasPendingSplitChanges =>
        _areSplitRowsLoaded && HasEffectiveSplitChanges(AllSplitRows(), _savedSplitRows, _removedSplitRows);

    public bool ShowNormalTransactionFields => !IsSplitMode;
    public IEnumerable<TagVM> AllSplitTags => _orderedTags.Where(tag => !tag.IsSystemTag);
    public bool HasSplitParentRemainder => IsSplitMode && SplitRemainingAmount > 0m;
    public bool CanSplitEqually => SplitTotalAmount > 0m;
    public bool CanCloseSplitModeWithoutSaving => IsSplitMode && !HasPendingSplitChanges;
    public bool RequiresEmptySplitConfirmationOnClose => false;

    partial void OnIsExcludedFromBudgetChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCategoryField));
        OnPropertyChanged(nameof(ShouldExpandAccountField));
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(AreFieldsReadOnly));
        OnPropertyChanged(nameof(CanEditFields));
        OnPropertyChanged(nameof(IsCategoryEnabled));
        RefreshTagCollections();

        if (!value)
            IsMoreTagsOpen = false;
    }

    partial void OnIsSplitModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSplitButton));
        OnPropertyChanged(nameof(ShowNormalTransactionFields));
        OnPropertyChanged(nameof(ShowChildTransactions));
        OnPropertyChanged(nameof(HasSplitParentRemainder));
        OnPropertyChanged(nameof(CanCloseSplitModeWithoutSaving));
        OnPropertyChanged(nameof(RequiresEmptySplitConfirmationOnClose));
    }

    partial void OnAmountTextChanged(decimal value)
    {
        OnPropertyChanged(nameof(HasSplitParentRemainder));
        OnPropertyChanged(nameof(CanSplitEqually));

        if (!IsSplitMode)
            return;

        UpdateSplitNegativeRemainderState(SplitRemainingAmount, SplitRows.LastOrDefault(row => row.HasAmount));
    }

    partial void OnSplitTotalAmountChanged(decimal value)
    {
        OnPropertyChanged(nameof(CanSplitEqually));
        RecalculateSplitRemainder(SplitRows.LastOrDefault(row => row.HasAmount));
        if (IsSplitEquallyEnabled)
            ApplyEqualSplitAmounts();
    }

    partial void OnSelectedTagChanged(TagVM? value)
    {
        if (_isUpdatingTagCollections || value is null)
            return;

        if (!IsEditing)
        {
            RefreshTagCollections();
            return;
        }

        if (OverflowTags.Any(tag => tag.Id == value.Id))
            PromoteTagToVisibleStart(value);

        IsMoreTagsOpen = false;
    }

    partial void OnIsIoUChanged(bool value)
    {
        if (!value)
            ShouldAffectBalance = false;

        OnPropertyChanged(nameof(IsRegularMode));
        OnPropertyChanged(nameof(IsUnpostedIoUMode));
        OnPropertyChanged(nameof(IsPostedIoUMode));
        OnPropertyChanged(nameof(TransactionModeDescription));
    }

    partial void OnShouldAffectBalanceChanged(bool value)
    {
        if (value && !IsIoU)
        {
            ShouldAffectBalance = false;
            return;
        }

        OnPropertyChanged(nameof(IsUnpostedIoUMode));
        OnPropertyChanged(nameof(IsPostedIoUMode));
        OnPropertyChanged(nameof(TransactionModeDescription));
    }

    internal static string GetTransactionModeDescription(bool isIoU, bool shouldAffectBalance) =>
        isIoU
            ? shouldAffectBalance
                ? "A transaction marked as debt/IoU and affects the accounts"
                : "A transaction marked as debt/IoU but doesn't affect the accounts"
            : "A one-time transaction";

    public async Task BeginEditingAsync()
    {
        IsEditing = true;
        await EnsureTagsLoadedAsync();
    }

    public void CancelEditing()
    {
        IsEditing = false;
        ClearSplitMode();
        LoadFromSavedState();
    }

    public void BeginSplitMode()
    {
        IsEditing = true;
        IsSplitMode = true;
        HasNegativeSplitRemainder = false;
        NegativeRemainderRow = null;
        SplitTotalAmount = _savedState.Amount;
        SplitRemainingAmount = _savedState.Amount;
    }

    public async Task BeginSplitModeAsync(CancellationToken cancellationToken = default)
    {
        BeginSplitMode();
        if (_areSplitRowsLoaded)
            return;

        await LoadChildTransactionsIntoSplitRowsAsync(cancellationToken);
        _areSplitRowsLoaded = true;
    }

    public void ShowParentTransaction()
    {
        IsSplitMode = false;
        IsEditing = false;
    }

    public void AddSplitRow()
    {
        var row = new TransactionSplitRowVM
        {
            SelectedExpenseCategory = SelectedExpenseCategory,
            SelectedTag = SelectedTag,
            IsIoU = IsPostedIoUMode
        };

        AddSplitRow(row);

        if (IsSplitEquallyEnabled)
            ApplyEqualSplitAmounts();
    }

    partial void OnIsSplitEquallyEnabledChanged(bool value)
    {
        if (value)
            ApplyEqualSplitAmounts();
        else
            ClearSplitAmounts();
    }

    [RelayCommand]
    private void SetSplitMode()
    {
        if (SplitRows.Count == 0)
            AddSplitRow();
        IsSplitEquallyEnabled = false;
    }

    [RelayCommand]
    private void SetSplitEquallyMode()
    {
        if (SplitRows.Count == 0)
            AddSplitRow();
        IsSplitEquallyEnabled = true;
    }

    private void AddSplitRow(TransactionSplitRowVM row)
    {
        SubscribeSplitRow(row);
        SplitRows.Add(row);
        NotifySplitRowStateChanged();
        RecalculateSplitRemainder(row);
    }

    public void RemoveSplitRow(TransactionSplitRowVM row)
    {
        foreach (var child in row.ChildRows)
        {
            UnsubscribeSplitRow(child);
            TrackRemovedRow(child);
        }

        UnsubscribeSplitRow(row);
        TrackRemovedRow(row);

        SplitRows.Remove(row);
        NotifySplitRowStateChanged();
        if (IsSplitEquallyEnabled)
            ApplyEqualSplitAmounts();
        else
            RecalculateSplitRemainder(SplitRows.LastOrDefault());
    }

    public void AddNestedSplitRow(TransactionSplitRowVM parent)
    {
        parent.AddChildRow();
        var child = parent.ChildRows[^1];
        SubscribeSplitRow(child);
        parent.RecalculateChildRemainder(child);
        NotifySplitRowStateChanged();
    }

    public void RemoveNestedSplitRow(TransactionSplitRowVM child)
    {
        var parent = SplitRows.First(row => row.ChildRows.Contains(child));
        UnsubscribeSplitRow(child);
        TrackRemovedRow(child);
        parent.ChildRows.Remove(child);
        parent.RecalculateChildRemainder(parent.ChildRows.LastOrDefault());
        NotifySplitRowStateChanged();
    }

    public async Task LoadChildTransactionsAsync(CancellationToken cancellationToken = default)
    {
        var childTransactions = (await LoadChildTransactionEntitiesAsync(cancellationToken))
            .Select(ProjectChildTransaction)
            .ToList();

        ChildTransactions.Clear();
        foreach (var childTransaction in childTransactions)
            ChildTransactions.Add(childTransaction);

        OnPropertyChanged(nameof(HasChildTransactions));
        OnPropertyChanged(nameof(ShowChildTransactions));
        OnPropertyChanged(nameof(ShowTagSelection));
    }

    private async Task LoadChildTransactionsIntoSplitRowsAsync(CancellationToken cancellationToken)
    {
        var childLogs = await LoadChildTransactionEntitiesAsync(cancellationToken);
        if (childLogs.Count == 0)
            return;

        foreach (var row in AllSplitRows())
            UnsubscribeSplitRow(row);

        SplitRows.Clear();
        _removedSplitRows.Clear();

        var allTransactions = await _appData.GetTransactionsAsync(cancellationToken);
        var childIds = childLogs.Select(log => log.Id).ToHashSet();
        var nestedByParentId = allTransactions
            .Where(log => log.ParentTransactionId is int id && childIds.Contains(id))
            .GroupBy(log => log.ParentTransactionId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var childLog in childLogs)
        {
            var row = ProjectSplitRow(childLog);
            if (nestedByParentId.TryGetValue(childLog.Id, out var nestedLogs))
            {
                foreach (var nestedLog in nestedLogs)
                {
                    var nestedRow = ProjectSplitRow(nestedLog);
                    row.ChildRows.Add(nestedRow);
                    SubscribeSplitRow(nestedRow);
                }

                row.IsSplit = true;
                row.RecalculateChildRemainder(row.ChildRows.LastOrDefault());
            }

            AddSplitRow(row);
        }

        _savedSplitRows.Clear();
        _savedSplitRows.AddRange(AllSplitRows().Select(CloneSplitRow));
        NotifySplitRowStateChanged();
    }

    private async Task<IReadOnlyList<Transaction>> LoadChildTransactionEntitiesAsync(CancellationToken cancellationToken)
    {
        return (await _appData.GetTransactionsAsync(cancellationToken))
            .Where(transaction => transaction.Type == _transaction.Type && !transaction.IsForDeletion)
            .Where(transaction => transaction.ParentTransactionId == _transaction.Id)
            .OrderByDescending(transaction => transaction.OccurredOn)
            .ThenByDescending(transaction => transaction.LoggedOn)
            .ThenBy(transaction => transaction.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void ClearSplitMode()
    {
        foreach (var row in AllSplitRows())
            UnsubscribeSplitRow(row);

        SplitRows.Clear();
        _removedSplitRows.Clear();
        _savedSplitRows.Clear();
        _areSplitRowsLoaded = false;
        IsSplitMode = false;
        IsSplitEquallyEnabled = false;
        HasNegativeSplitRemainder = false;
        NegativeRemainderRow = null;
        NotifySplitRowStateChanged();
    }

    public void RecalculateSplitRemainder(TransactionSplitRowVM? changedRow)
    {
        var remainder = SplitTotalAmount - SplitRows.Sum(row => row.AmountText);
        SplitRemainingAmount = remainder;

        UpdateSplitNegativeRemainderState(remainder, changedRow);
    }

    private void ApplyEqualSplitAmounts()
    {
        ApplyEqualSplitAmounts(SplitRows, SplitTotalAmount);
    }

    private void ClearSplitAmounts()
    {
        ClearSplitAmounts(SplitRows);
    }

    public async Task EnsureTagsLoadedAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Tag> allTags;
        try
        {
            allTags = await _appData.GetTagsAsync(cancellationToken);
        }
        catch
        {
            return;
        }

        var selectedTagId = SelectedTag?.Id;
        var persistedTags = TransactionPopupVM.ProjectNonSystemTags(allTags).ToList();
        if (persistedTags.Count == 0)
            return;

        _orderedTags.Clear();
        _orderedTags.AddRange(persistedTags);
        RefreshTagCollections();

        SelectedTag = selectedTagId is null
            ? _orderedTags.FirstOrDefault()
            : _orderedTags.FirstOrDefault(tag => tag.Id == selectedTagId.Value) ?? _orderedTags.FirstOrDefault();
    }

    public TransactionPopupVM.TransactionPopupDraft CreateTransactionPopupDraft()
    {
        return new TransactionPopupVM.TransactionPopupDraft(
            IsExpense,
            NameText,
            AmountText,
            SelectedAccount?.Id,
            SelectedDate.Date,
            NoteText,
            SelectedExpenseCategory,
            SelectedTag?.Id,
            ShouldAffectBalance: ShouldAffectBalance);
    }

    public async Task<TransactionSplitSaveResult> SaveAsync(
        bool keepParentExpenseWhenRemainder = false,
        bool allowMaximumSpendingOverflow = false)
    {
        if (IsSaving)
            return TransactionSplitSaveResult.Failure("This expense is already being saved.");

        if (IsSplitMode || HasPendingSplitChanges)
            return await SaveSplitAsync(keepParentExpenseWhenRemainder);

        if (!TryBuildInput(out var input, out var validationMessage))
            return TransactionSplitSaveResult.Failure(validationMessage);

        var previousState = CreateMessageSnapshot(_savedState);
        var changedFields = GetChangedFields(input, _savedState);
        if (changedFields == TransactionDetailChangedFields.None)
        {
            IsEditing = false;
            ClearSplitMode();
            LoadFromSavedState();
            return TransactionSplitSaveResult.Success();
        }

        IsSaving = true;

        try
        {
            var transaction = await _appData.GetTransactionByIdAsync(_transaction.Id);
            if (transaction is null || transaction.Type != _transaction.Type)
                return TransactionSplitSaveResult.Failure("Unable to load this transaction.");

            var beforeHistorySnapshot = TransactionMemorySnapshot.Create(transaction);
            var currentAccount = transaction.Account;
            if (currentAccount is null)
                return TransactionSplitSaveResult.Failure("Unable to load this expense source.");

            var newAccount = await _appData.GetAccountByIdAsync(input.AccountId);
            if (newAccount is null)
                return TransactionSplitSaveResult.Failure("Please select a valid account.");

            var tag = await _appData.GetTagByIdAsync(input.TagId);
            if (tag is null)
                return TransactionSplitSaveResult.Failure("Please select a valid tag.");

            var resolvedName = BuildTransactionName(input.Name, input.Note, tag.Name);

            var sourceChanged = currentAccount.Id != newAccount.Id;
            var exclusionChanged = input.IsExcludedFromBudget != _savedState.IsExcludedFromBudget;
            if (!sourceChanged)
                newAccount = currentAccount;
            var oldAffectsBalance = transaction.AffectsAccountBalance;
            var newAffectsBalance = Transaction.ShouldAffectAccountBalance(
                input.IsIoU, input.ShouldAffectBalance);
            var destinationSpending = 0m;
            if (sourceChanged && newAccount.MaximumSpending > 0m)
            {
                destinationSpending = newAccount.AccountType == AccountType.Credit
                    ? newAccount.SpentAmount
                    : CalculateAccountSpending(await _appData.GetTransactionsAsync(), newAccount.Id);
            }
            if (newAffectsBalance && transaction.Type == TransactionType.Expense &&
                RequiresMaximumSpendingConfirmation(
                    currentAccount.Id,
                    newAccount.Id,
                    newAccount.MaximumSpending,
                    destinationSpending,
                    input.Amount,
                    allowMaximumSpendingOverflow))
            {
                return TransactionSplitSaveResult.Confirmation(
                    $"This expense exceeds {newAccount.Name}'s maximum spending limit. Save anyway?");
            }

            if (oldAffectsBalance)
                LogMemoryPersistence.RevertTransactionFromAccount(currentAccount, transaction.Type, transaction.Amount);

            if (newAffectsBalance)
                LogMemoryPersistence.ApplyTransactionToAccount(newAccount, transaction.Type, input.Amount);

            transaction.Name = resolvedName;
            transaction.Amount = input.Amount;
            transaction.ExpenseCategory = transaction.Type == TransactionType.Expense ? input.Category : null;
            transaction.Tag = HasChildTransactions ? null : tag;
            transaction.TagId = HasChildTransactions ? null : tag.Id;
            transaction.IsPinned = input.IsPinned;
            transaction.OccurredOn = input.Date;
            transaction.Notes = input.Note;
            transaction.Account = newAccount;
            transaction.SourceAccountId = newAccount.Id;
            transaction.IsIoU = input.IsIoU;
            transaction.ShouldAffectBalance = input.ShouldAffectBalance;
            transaction.IsExcludedFromBudget = input.IsExcludedFromBudget;

            if (sourceChanged || exclusionChanged)
                await CascadeParentStateToChildTransactionsAsync(newAccount, input.IsExcludedFromBudget);

            _appData.UpdateTransaction(transaction);
            if (oldAffectsBalance || newAffectsBalance && !sourceChanged)
                _appData.UpdateAccount(currentAccount);

            if (sourceChanged && newAffectsBalance)
                _appData.UpdateAccount(newAccount);

            await _appData.SaveChangesAsync();
            _savedState = new TransactionDetailSavedState(
                resolvedName,
                input.Amount,
                input.IsPinned,
                input.Note,
                input.Date,
                input.Category,
                input.AccountId,
                input.TagId,
                input.IsIoU,
                input.ShouldAffectBalance,
                input.IsExcludedFromBudget);

            IsEditing = false;
            ClearSplitMode();
            LoadFromSavedState();
            _messenger.Send(new TransactionDetailUpdatedMessage(
                new TransactionDetailUpdate(_transaction.Id, previousState, changedFields)));
            _messenger.Send(new RecordLogMemoryMessage(
                new EditTransactionMemoryAction(beforeHistorySnapshot, TransactionMemorySnapshot.Create(transaction))));
            return TransactionSplitSaveResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to save expense detail changes.");
            return TransactionSplitSaveResult.Failure(FluxoLogManager.CreateFailureMessage("save expense"));
        }
        finally
        {
            IsSaving = false;
        }
    }

    public bool HasValidChangesToPersistOnClose()
    {
        if (HasPendingSplitChanges)
            return true;

        if (!IsEditing)
            return false;

        if (!TryBuildInput(out var input, out _))
            return false;

        return GetChangedFields(input, _savedState) != TransactionDetailChangedFields.None;
    }

    public async Task<TransactionSplitSaveResult> DeleteAsync()
    {
        if (IsSaving)
            return TransactionSplitSaveResult.Failure("This expense is already being saved.");

        IsSaving = true;
        try
        {
            var transaction = await _appData.GetTransactionByIdAsync(_transaction.Id);
            if (transaction is null)
                return TransactionSplitSaveResult.Failure("Unable to load this expense.");

            var plan = await BuildDeletionPlanAsync(_appData, transaction, CancellationToken.None);
            if (!plan.IsSuccess)
                return TransactionSplitSaveResult.Failure(plan.ErrorMessage);

            var snapshots = plan.Transactions.Select(TransactionMemorySnapshot.Create).ToList();
            foreach (var item in plan.Transactions)
            {
                if (item.AffectsAccountBalance)
                {
                    LogMemoryPersistence.RevertTransactionFromAccount(item.Account, item.Type, item.Amount);
                    _appData.UpdateAccount(item.Account);
                }
                _appData.RemoveTransaction(item);
            }

            if (plan.Goal is { } goal)
            {
                goal.CurrentAmount -= transaction.Amount;
                _appData.UpdateSavingGoal(goal);
            }

            await _appData.SaveChangesAsync();

            ILogMemoryAction historyAction = snapshots.Count == 1
                ? new DeleteTransactionMemoryAction(snapshots[0])
                : new CompositeLogMemoryAction(
                    "Reverse repayment",
                    snapshots.Select(snapshot => (ILogMemoryAction)new DeleteTransactionMemoryAction(snapshot)).ToList());
            _messenger.Send(new RecordLogMemoryMessage(historyAction));

            var scope = DashboardDataInvalidationScope.Budget;
            if (plan.Goal is not null)
                scope |= DashboardDataInvalidationScope.SavingGoals;
            _messenger.Send(new DashboardDataInvalidatedMessage(scope));

            if (plan.RepaymentAccountName is { } accountName)
                PublishRepaymentReversalNotification(_messenger, accountName);
            return TransactionSplitSaveResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to delete expense detail.");
            return TransactionSplitSaveResult.Failure(FluxoLogManager.CreateFailureMessage("delete expense"));
        }
        finally
        {
            IsSaving = false;
        }
    }

    public void SetVisibleTagSlots(int visibleTagSlots)
    {
        var normalizedSlots = Math.Max(0, visibleTagSlots);
        if (_visibleTagSlots == normalizedSlots)
            return;

        _visibleTagSlots = normalizedSlots;
        RefreshTagCollections();
    }

    private void LoadFromSavedState()
    {
        AmountText = _savedState.Amount;
        NameText = _savedState.Name;
        IsPinned = _savedState.IsPinned;
        IsIoU = _savedState.IsIoU;
        ShouldAffectBalance = _savedState.ShouldAffectBalance;
        IsExcludedFromBudget = _savedState.IsExcludedFromBudget;
        NoteText = _savedState.Note;
        SelectedDate = _savedState.Date == default ? DateTime.Today : _savedState.Date.Date;
        SelectedExpenseCategory = _savedState.Category;
        SelectedAccount = Accounts.FirstOrDefault(source => source.Id == _savedState.AccountId) ??
                                 Accounts.FirstOrDefault();
        SelectedTag = _orderedTags.FirstOrDefault(tag => tag.Id == _savedState.TagId) ??
                      _orderedTags.FirstOrDefault();
        PopupTitle = string.IsNullOrWhiteSpace(NameText) ? "Transaction Detail" : NameText.Trim();
        IsMoreTagsOpen = false;
    }

    private bool TryBuildInput(out TransactionDetailInput input, out string validationMessage)
    {
        input = default;
        validationMessage = string.Empty;

        if (AmountText <= 0m)
        {
            validationMessage = "Please enter a valid amount greater than zero.";
            return false;
        }

        if (SelectedAccount is null)
        {
            validationMessage = "Please choose a account.";
            return false;
        }

        if (SelectedTag is null)
        {
            validationMessage = "Please choose a tag.";
            return false;
        }

        input = new TransactionDetailInput(
            NameText.Trim(),
            AmountText,
            IsPinned,
            SelectedAccount.Id,
            SelectedDate.Date,
            NoteText.Trim(),
            SelectedExpenseCategory,
            SelectedTag.Id,
            IsIoU,
            ShouldAffectBalance,
            IsExcludedFromBudget);

        return true;
    }

    private bool TryBuildSplitInputs(
        bool keepParentExpenseWhenRemainder,
        out IReadOnlyList<TransactionSplitInput> inputs,
        out string validationMessage)
    {
        inputs = [];
        validationMessage = string.Empty;

        if (HasNegativeSplitRemainder || SplitTotalAmount <= 0m)
        {
            validationMessage = "Split amounts exceed the original expense amount.";
            return false;
        }

        if (AllSplitRows().Any(row => row.AmountText < 0m))
        {
            validationMessage = "Split amounts cannot be negative.";
            return false;
        }

        foreach (var row in SplitRows)
        {
            row.RecalculateChildRemainder(row.ChildRows.LastOrDefault(child => child.HasAmount));
            if (row.HasNegativeChildRemainder)
            {
                validationMessage = "Split amounts exceed the parent expense amount.";
                return false;
            }

            if (row.ChildRows.Any(child => child.AmountText > 0m && child.SelectedTag is null))
            {
                validationMessage = "Please choose a tag for each split row.";
                return false;
            }
        }

        _ = keepParentExpenseWhenRemainder;
        var result = new List<TransactionSplitInput>();

        foreach (var row in SplitRows.Where(row => row.AmountText > 0m))
        {
            if (!row.IsSplit && row.SelectedTag is null)
            {
                validationMessage = "Please choose a tag for each split row.";
                return false;
            }

            result.Add(new TransactionSplitInput(
                row.TransactionId,
                _transaction.Id,
                row.NameText.Trim(),
                row.AmountText,
                row.SelectedExpenseCategory,
                row.IsSplit ? null : row.SelectedTag!.Id,
                string.Empty,
                SelectedDate.Date,
                row.IsIoU,
                row.IsExcludedFromBudget));
        }

        if (result.Count == 0 && _removedSplitRows.All(row => row.TransactionId is null))
        {
            validationMessage = "Add at least one split amount before saving.";
            return false;
        }

        var splitRowsPositiveTotal = SplitRows
            .Where(row => row.AmountText > 0m)
            .Sum(row => row.AmountText);
        if (splitRowsPositiveTotal > SplitTotalAmount)
        {
            validationMessage = "Split amounts exceed the original expense amount.";
            return false;
        }

        inputs = result;
        return true;
    }

    private async Task<TransactionSplitSaveResult> SaveSplitAsync(bool keepParentExpenseWhenRemainder)
    {
        if (!TryBuildSplitInputs(keepParentExpenseWhenRemainder, out var inputs, out var validationMessage))
            return TransactionSplitSaveResult.Failure(validationMessage);

        IsSaving = true;

        try
        {
            var originalLog = await _appData.GetTransactionByIdAsync(_transaction.Id);
            if (originalLog is null || originalLog.Type != _transaction.Type)
                return TransactionSplitSaveResult.Failure("Unable to load this transaction.");

            var account = originalLog.Account;
            if (account is null)
                return TransactionSplitSaveResult.Failure("Unable to load this expense source.");

            var splitEntries = new List<(TransactionSplitInput Input, Tag? Tag)>();
            foreach (var input in inputs)
            {
                Tag? tag = null;
                if (input.TagId is { } tagId)
                {
                    tag = await _appData.GetTagByIdAsync(tagId);
                    if (tag is null)
                        return TransactionSplitSaveResult.Failure("Please select a valid tag.");
                }

                splitEntries.Add((input, tag));
            }

            _ = keepParentExpenseWhenRemainder;
            originalLog.ParentTransactionId = null;
            ClearSplitParentMetadata(originalLog);
            originalLog.Amount = SplitTotalAmount;
            _appData.UpdateTransaction(originalLog);
            var existingChildren = (await LoadChildTransactionEntitiesAsync(CancellationToken.None))
                .ToDictionary(log => log.Id);

            var changedSnapshots = new List<(TransactionMemorySnapshot Before, TransactionMemorySnapshot After)>();
            var createdLogs = new List<Transaction>(splitEntries.Count);
            foreach (var (input, tag) in splitEntries)
            {
                if (input.TransactionId is { } childLogId &&
                    existingChildren.TryGetValue(childLogId, out var existingChild) &&
                    existingChild.Type == originalLog.Type)
                {
                    var beforeSnapshot = TransactionMemorySnapshot.Create(existingChild);
                    ApplySplitInputToExistingChild(existingChild, input, tag, account);
                    _appData.UpdateTransaction(existingChild);
                    changedSnapshots.Add((beforeSnapshot, TransactionMemorySnapshot.Create(existingChild)));
                    continue;
                }

                var transaction = CreateSplitTransaction(input, tag, account, originalLog.Id, originalLog.Type);
                await _appData.AddTransactionAsync(transaction);
                createdLogs.Add(transaction);
            }

            var retainedChildIds = inputs
                .Select(input => input.TransactionId)
                .OfType<int>()
                .ToHashSet();
            var removedChildIds = new HashSet<int>();

            foreach (var removedRow in _removedSplitRows.Where(row => row.TransactionId is not null))
            {
                var removedChildId = removedRow.TransactionId!.Value;
                removedChildIds.Add(removedChildId);

                if (!existingChildren.TryGetValue(removedChildId, out var removedChild) ||
                    removedChild.IsForDeletion)
                    continue;

                if (removedRow.SelectedTag is null)
                {
                    removedChild.IsForDeletion = true;
                    _appData.UpdateTransaction(removedChild);
                    continue;
                }

                var removedTag = await _appData.GetTagByIdAsync(removedRow.SelectedTag.Id);
                if (removedTag is null)
                    return TransactionSplitSaveResult.Failure("Please select a valid tag.");

                var input = new TransactionSplitInput(
                    removedRow.TransactionId,
                    originalLog.Id,
                    removedRow.NameText.Trim(),
                    removedRow.AmountText,
                    removedRow.SelectedExpenseCategory,
                    removedRow.SelectedTag.Id,
                    string.Empty,
                    SelectedDate.Date,
                    removedRow.IsIoU,
                    removedRow.IsExcludedFromBudget);
                var beforeSnapshot = TransactionMemorySnapshot.Create(removedChild);
                ApplySplitInputToExistingChild(removedChild, input, removedTag, account);
                removedChild.IsForDeletion = true;
                _appData.UpdateTransaction(removedChild);
                changedSnapshots.Add((beforeSnapshot, TransactionMemorySnapshot.Create(removedChild)));
            }

            foreach (var staleChild in existingChildren.Values.Where(child =>
                         !retainedChildIds.Contains(child.Id) &&
                         !removedChildIds.Contains(child.Id)))
            {
                var beforeSnapshot = TransactionMemorySnapshot.Create(staleChild);
                staleChild.IsForDeletion = true;
                _appData.UpdateTransaction(staleChild);
                changedSnapshots.Add((beforeSnapshot, TransactionMemorySnapshot.Create(staleChild)));
            }

            var parentTransactions = new Dictionary<TransactionSplitRowVM, Transaction>();
            foreach (var row in SplitRows.Where(row => row.AmountText > 0m && row.TransactionId is not null))
                parentTransactions[row] = existingChildren[row.TransactionId!.Value];

            var createdParentRows = SplitRows.Where(row => row.AmountText > 0m && row.TransactionId is null).ToList();
            for (var index = 0; index < createdParentRows.Count; index++)
                parentTransactions[createdParentRows[index]] = createdLogs[index];

            var existingNestedChildren = (await _appData.GetTransactionsAsync())
                .Where(transaction => transaction.ParentTransactionId is not null)
                .ToDictionary(transaction => transaction.Id);
            foreach (var (parentRow, parentTransaction) in parentTransactions)
            {
                if (parentRow.ChildRows.Any(row => row.AmountText > 0m))
                {
                    ClearSplitParentMetadata(parentTransaction);
                    _appData.UpdateTransaction(parentTransaction);
                }

                foreach (var childRow in parentRow.ChildRows.Where(row => row.AmountText > 0m))
                {
                    var tag = await _appData.GetTagByIdAsync(childRow.SelectedTag!.Id);
                    if (tag is null)
                        return TransactionSplitSaveResult.Failure("Please select a valid tag.");

                    var input = new TransactionSplitInput(
                        childRow.TransactionId,
                        parentTransaction.Id,
                        childRow.NameText.Trim(),
                        childRow.AmountText,
                        childRow.SelectedExpenseCategory,
                        tag.Id,
                        string.Empty,
                        SelectedDate.Date,
                        childRow.IsIoU,
                        IsNestedRowExcluded(parentRow, childRow));

                    if (childRow.TransactionId is { } childId && existingNestedChildren.TryGetValue(childId, out var existingChild))
                    {
                        var beforeSnapshot = TransactionMemorySnapshot.Create(existingChild);
                        ApplySplitInputToExistingChild(existingChild, input, tag, account);
                        _appData.UpdateTransaction(existingChild);
                        changedSnapshots.Add((beforeSnapshot, TransactionMemorySnapshot.Create(existingChild)));
                        continue;
                    }

                    var child = CreateSplitTransaction(input, tag, account, parentTransaction.Id, originalLog.Type);
                    child.ParentTransaction = parentTransaction;
                    child.ParentTransactionId = parentTransaction.Id > 0 ? parentTransaction.Id : null;
                    await _appData.AddTransactionAsync(child);
                    createdLogs.Add(child);
                }
            }

            foreach (var removedRow in _removedSplitRows.Where(row => row.TransactionId is not null))
            {
                if (!existingNestedChildren.TryGetValue(removedRow.TransactionId!.Value, out var removedChild) ||
                    removedChild.IsForDeletion)
                    continue;

                removedChild.IsForDeletion = true;
                _appData.UpdateTransaction(removedChild);
            }

            await _appData.SaveChangesAsync();

            var createdSnapshots = createdLogs.Select(TransactionMemorySnapshot.Create).ToList();

            var historyActions = changedSnapshots
                .Select(snapshots => (ILogMemoryAction)new EditTransactionMemoryAction(snapshots.Before, snapshots.After))
                .Concat(createdSnapshots
                .Select(snapshot => (ILogMemoryAction)new AddTransactionMemoryAction(snapshot, shouldAdjustAccountTotals: false))
                )
                .ToList();

            _messenger.Send(new RecordLogMemoryMessage(
                new CompositeLogMemoryAction("Split expense", historyActions)));
            _messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));
            await LoadChildTransactionsAsync();

            IsEditing = false;
            ClearSplitMode();
            LoadFromSavedState();
            return TransactionSplitSaveResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to split expense.");
            return TransactionSplitSaveResult.Failure(FluxoLogManager.CreateFailureMessage("split expense"));
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void PromoteTagToVisibleStart(TagVM selectedTag)
    {
        var reorderedTags = _orderedTags
            .Where(tag => tag.Id != selectedTag.Id)
            .Prepend(selectedTag)
            .ToList();

        _orderedTags.Clear();
        _orderedTags.AddRange(reorderedTags);

        RefreshTagCollections();
        SelectedTag = _orderedTags.FirstOrDefault(tag => tag.Id == selectedTag.Id);
    }

    private void RefreshTagCollections()
    {
        var selectedTagId = SelectedTag?.Id;

        _isUpdatingTagCollections = true;

        try
        {
            if (!IsEditing)
            {
                ReplaceCollection(VisibleTags, SelectedTag is null ? [] : [SelectedTag]);
                ReplaceCollection(OverflowTags, []);
                OnPropertyChanged(nameof(HasMoreTags));
                OnPropertyChanged(nameof(AllSplitTags));
                IsMoreTagsOpen = false;
                return;
            }

            var editableTags = _orderedTags.Where(tag => !tag.IsSystemTag).ToList();
            ReplaceCollection(VisibleTags, editableTags.Take(_visibleTagSlots));
            ReplaceCollection(OverflowTags, editableTags.Skip(_visibleTagSlots));

            OnPropertyChanged(nameof(HasMoreTags));
            OnPropertyChanged(nameof(AllSplitTags));
            if (!HasMoreTags)
                IsMoreTagsOpen = false;

            if (selectedTagId is null)
                return;

            SelectedTag = _orderedTags.FirstOrDefault(tag => tag.Id == selectedTagId.Value);
        }
        finally
        {
            _isUpdatingTagCollections = false;
        }
    }

    private void RefreshAccounts()
    {
        var selectedAccountId = SelectedAccount?.Id;
        ReplaceCollection(Accounts, _availableAccounts
            .OrderBy(source => source.AccountType)
            .ThenBy(source => source.Name));

        SelectedAccount = selectedAccountId is null
            ? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault(source => source.Id == selectedAccountId.Value) ??
              Accounts.FirstOrDefault();
    }

    private static string BuildTransactionName(string name, string note, string fallbackName)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        if (string.IsNullOrWhiteSpace(note))
            return fallbackName;

        var firstMeaningfulLine = note
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        return string.IsNullOrWhiteSpace(firstMeaningfulLine)
            ? fallbackName
            : firstMeaningfulLine;
    }

    private static TransactionDetailChildTransactionVM ProjectChildTransaction(Transaction log)
    {
        return new TransactionDetailChildTransactionVM
        {
            Id = log.Id,
            Name = log.Name,
            Amount = log.Amount,
            OccurredOn = log.OccurredOn,
            Category = log.ExpenseCategory ?? ExpenseCategory.Needs,
            AccountName = log.Account?.Name ?? string.Empty,
            TagName = log.Tag?.Name ?? string.Empty,
            TagHexCode = log.Tag?.HexCode ?? string.Empty,
            Notes = log.Notes,
            IsIoU = log.IsIoU
        };
    }

    internal static void PublishRepaymentReversalNotification(IMessenger messenger, string accountName)
    {
        FloatingNotificationPublisher.Success(
            messenger,
            $"Repayment for {accountName} reversed.",
            string.Empty);
    }

    internal static async Task<TransactionDeletionPlan> BuildDeletionPlanAsync(
        IAppDataService appData,
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appData);
        ArgumentNullException.ThrowIfNull(transaction);

        if (string.Equals(transaction.Tag?.Name, GoalUpdateTransactionSupport.GoalUpdateTagName,
                StringComparison.OrdinalIgnoreCase) &&
            transaction.GoalId is { } goalId)
        {
            var goal = await appData.GetSavingGoalByIdAsync(goalId, cancellationToken);
            return goal is null
                ? TransactionDeletionPlan.Failure("Unable to load the linked saving goal.")
                : TransactionDeletionPlan.Success([transaction], goal);
        }

        if (transaction.Type == TransactionType.Expense &&
            transaction.RepaymentAccountId is not null &&
            string.Equals(transaction.Tag?.Name, "Balance Update", StringComparison.OrdinalIgnoreCase))
        {
            var income = RepaymentTransactionSupport.FindNewestIncome(
                transaction,
                await appData.GetTransactionsAsync(cancellationToken));
            if (income is null)
                return TransactionDeletionPlan.Failure("Unable to find the matching repayment income.");

            var accountName = transaction.RepaymentAccount?.Name ?? income.Account.Name;
            return TransactionDeletionPlan.Success([transaction, income], repaymentAccountName: accountName);
        }

        return TransactionDeletionPlan.Success([transaction]);
    }

    internal readonly record struct TransactionDeletionPlan(
        bool IsSuccess,
        string? ErrorMessage,
        IReadOnlyList<Transaction> Transactions,
        SavingGoal? Goal,
        string? RepaymentAccountName)
    {
        public static TransactionDeletionPlan Success(
            IReadOnlyList<Transaction> transactions,
            SavingGoal? goal = null,
            string? repaymentAccountName = null) =>
            new(true, null, transactions, goal, repaymentAccountName);

        public static TransactionDeletionPlan Failure(string errorMessage) =>
            new(false, errorMessage, [], null, null);
    }

    private TransactionSplitRowVM ProjectSplitRow(Transaction log)
    {
        return new TransactionSplitRowVM
        {
            TransactionId = log.Id,
            AmountText = log.Amount,
            NameText = log.Name,
            SelectedExpenseCategory = log.ExpenseCategory ?? SelectedExpenseCategory,
            SelectedTag = ResolveSplitRowTag(log.Tag),
            IsIoU = log.IsIoU,
            IsExcludedFromBudget = log.IsExcludedFromBudget
        };
    }

    private TagVM? ResolveSplitRowTag(Tag? tag)
    {
        if (tag is null)
            return SelectedTag;

        var existingTag = _orderedTags.FirstOrDefault(candidate => candidate.Id == tag.Id);
        if (existingTag is not null)
            return existingTag;

        return new TagVM
        {
            Id = tag.Id,
            Name = tag.Name,
            HexCode = tag.HexCode,
            IsSystemTag = tag.IsSystemTag
        };
    }

    internal static void ApplyParentStateToChildTransactions(
        IEnumerable<Transaction> childTransactions,
        Account account,
        bool isExcludedFromBudget)
    {
        foreach (var childTransaction in childTransactions)
        {
            childTransaction.Account = account;
            childTransaction.SourceAccountId = account.Id;
            childTransaction.IsExcludedFromBudget = isExcludedFromBudget;
        }
    }

    internal static void ClearParentTransactionCategory(Transaction transaction) =>
        transaction.ExpenseCategory = null;

    internal static void ClearSplitParentMetadata(Transaction transaction)
    {
        ClearParentTransactionCategory(transaction);
        transaction.Tag = null;
        transaction.TagId = null;
    }

    internal static bool IsNestedRowExcluded(TransactionSplitRowVM parent, TransactionSplitRowVM child) =>
        parent.IsExcludedFromBudget || child.IsExcludedFromBudget || child.IsIoU;

    private async Task CascadeParentStateToChildTransactionsAsync(
        Account account,
        bool isExcludedFromBudget,
        CancellationToken cancellationToken = default)
    {
        var childLogs = await LoadChildTransactionEntitiesAsync(cancellationToken);
        ApplyParentStateToChildTransactions(childLogs, account, isExcludedFromBudget);

        foreach (var childLog in childLogs)
            _appData.UpdateTransaction(childLog);
    }

    private static Transaction CreateSplitTransaction(
        TransactionSplitInput input,
        Tag? tag,
        Account account,
        int parentTransactionId,
        TransactionType type)
    {
        return new Transaction
        {
            Type = type,
            Name = BuildTransactionName(input.Name, input.Note, tag?.Name ?? string.Empty),
            Amount = input.Amount,
            OccurredOn = input.Date,
            Notes = input.Note,
            ExpenseCategory = type == TransactionType.Expense && tag is not null ? input.Category : null,
            SourceAccountId = account.Id,
            Account = account,
            TagId = tag?.Id,
            Tag = tag,
            ParentTransactionId = parentTransactionId,
            IsIoU = input.IsIoU,
            ShouldAffectBalance = input.IsIoU,
            IsExcludedFromBudget = input.IsExcludedFromBudget
        };
    }

    private static void ApplySplitInputToExistingChild(
        Transaction transaction,
        TransactionSplitInput input,
        Tag? tag,
        Account account)
    {
        transaction.Name = BuildTransactionName(input.Name, input.Note, tag?.Name ?? string.Empty);
        transaction.Amount = input.Amount;
        transaction.OccurredOn = input.Date;
        transaction.Notes = input.Note;
        transaction.ExpenseCategory = transaction.Type == TransactionType.Expense && tag is not null ? input.Category : null;
        transaction.Tag = tag;
        transaction.TagId = tag?.Id;
        transaction.Account = account;
        transaction.SourceAccountId = account.Id;
        transaction.IsIoU = input.IsIoU;
        transaction.ShouldAffectBalance = input.IsIoU;
        transaction.IsExcludedFromBudget = input.IsExcludedFromBudget;
        transaction.IsForDeletion = false;
        transaction.ParentTransactionId = input.ParentTransactionId;
    }

    private static bool IsBudgetReconciliationExpenseLog(Transaction transaction)
    {
        var tag = transaction.Tag;
        return tag is { IsSystemTag: true } &&
               string.Equals(tag.Name, SystemTags.BudgetReconciliationName, StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
            target.Add(item);
    }

    internal static IReadOnlyList<decimal> CreateEqualSplitAmounts(decimal amount, int count)
    {
        if (count <= 0)
            return [];

        var splitAmount = decimal.Round(amount / count, 2, MidpointRounding.AwayFromZero);
        var amounts = Enumerable.Repeat(splitAmount, count).ToArray();
        amounts[^1] = amount - splitAmount * (count - 1);
        return amounts;
    }

    internal static void ApplyEqualSplitAmounts(IList<TransactionSplitRowVM> rows, decimal amount)
    {
        var amounts = CreateEqualSplitAmounts(amount, rows.Count);
        for (var i = 0; i < amounts.Count; i++)
            rows[i].AmountText = amounts[i];
    }

    internal static void ClearSplitAmounts(IEnumerable<TransactionSplitRowVM> rows)
    {
        foreach (var row in rows)
            row.AmountText = 0m;
    }

    private static TransactionDetailSavedState CreateSavedState(TransactionVM transaction)
    {
        return new TransactionDetailSavedState(
            transaction.Name?.Trim() ?? string.Empty,
            transaction.Amount,
            transaction.IsPinned,
            transaction.Notes?.Trim() ?? string.Empty,
            transaction.OccurredOn == default ? DateTime.Today : transaction.OccurredOn.Date,
            transaction.ExpenseCategory ?? ExpenseCategory.Needs,
            transaction.Account?.Id ?? 0,
            transaction.Tag?.Id ?? 0,
            transaction.IsIoU,
            transaction.ShouldAffectBalance,
            transaction.IsExcludedFromBudget);
    }

    private static TransactionDetailSnapshot CreateMessageSnapshot(TransactionDetailSavedState savedState)
    {
        return new TransactionDetailSnapshot(
            savedState.Amount,
            savedState.Date,
            savedState.Category,
            savedState.AccountId,
            savedState.TagId);
    }

    private static TransactionDetailChangedFields GetChangedFields(TransactionDetailInput input,
        TransactionDetailSavedState savedState)
    {
        var changedFields = TransactionDetailChangedFields.None;

        if (!string.Equals(input.Name, savedState.Name, StringComparison.Ordinal))
            changedFields |= TransactionDetailChangedFields.Name;

        if (input.Amount != savedState.Amount)
            changedFields |= TransactionDetailChangedFields.Amount;

        if (input.IsPinned != savedState.IsPinned)
            changedFields |= TransactionDetailChangedFields.Pin;

        if (input.Date.Date != savedState.Date.Date)
            changedFields |= TransactionDetailChangedFields.Date;

        if (input.Category != savedState.Category)
            changedFields |= TransactionDetailChangedFields.Category;

        if (input.AccountId != savedState.AccountId)
            changedFields |= TransactionDetailChangedFields.Account;

        if (input.TagId != savedState.TagId)
            changedFields |= TransactionDetailChangedFields.Tag;

        if (!string.Equals(input.Note, savedState.Note, StringComparison.Ordinal))
            changedFields |= TransactionDetailChangedFields.Note;

        if (input.IsIoU != savedState.IsIoU)
            changedFields |= TransactionDetailChangedFields.IoU;

        if (input.ShouldAffectBalance != savedState.ShouldAffectBalance)
            changedFields |= TransactionDetailChangedFields.IoU;

        if (input.IsExcludedFromBudget != savedState.IsExcludedFromBudget)
            changedFields |= TransactionDetailChangedFields.BudgetExclusion;

        return changedFields;
    }

    private void NotifySplitRowStateChanged()
    {
        OnPropertyChanged(nameof(HasSplitRows));
        OnPropertyChanged(nameof(HasSplitRowsWithAmounts));
        OnPropertyChanged(nameof(HasSplitRowsWithoutAmounts));
        OnPropertyChanged(nameof(CanCloseSplitModeWithoutSaving));
        OnPropertyChanged(nameof(RequiresEmptySplitConfirmationOnClose));
        OnPropertyChanged(nameof(HasPendingSplitChanges));
    }

    private void OnSplitRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TransactionSplitRowVM row)
            return;

        NotifySplitRowStateChanged();

        if (e.PropertyName != nameof(TransactionSplitRowVM.AmountText))
            return;
        var parent = SplitRows.FirstOrDefault(candidate => candidate.ChildRows.Contains(row));
        if (parent is not null)
        {
            parent.RecalculateChildRemainder(row);
            return;
        }

        RecalculateSplitRemainder(row);
        row.RecalculateChildRemainder(row.ChildRows.LastOrDefault(child => child.HasAmount));
    }

    internal static void RecalculateNestedSplitRemainders(IEnumerable<TransactionSplitRowVM> rows)
    {
        foreach (var row in rows)
            row.RecalculateChildRemainder(row.ChildRows.LastOrDefault(child => child.HasAmount));
    }

    private IEnumerable<TransactionSplitRowVM> AllSplitRows() =>
        SplitRows.Concat(SplitRows.SelectMany(row => row.ChildRows));

    internal static bool CanUseEqualSplit(decimal amount, decimal originalAmount) => amount >= originalAmount;

    internal static bool HasEffectiveSplitChanges(
        IEnumerable<TransactionSplitRowVM> rows,
        IEnumerable<TransactionSplitRowVM> savedRows,
        IEnumerable<TransactionSplitRowVM> removedRows)
    {
        var savedById = savedRows
            .Where(row => row.TransactionId is not null)
            .ToDictionary(row => row.TransactionId!.Value);

        foreach (var row in rows)
        {
            if (row.TransactionId is null)
            {
                if (row.AmountText > 0m)
                    return true;

                continue;
            }

            if (!savedById.Remove(row.TransactionId.Value, out var savedRow) || !RowsMatch(row, savedRow))
                return true;
        }

        return savedById.Count > 0 || removedRows.Any(row => row.TransactionId is not null);
    }

    private static TransactionSplitRowVM CloneSplitRow(TransactionSplitRowVM row) => new()
    {
        TransactionId = row.TransactionId,
        AmountText = row.AmountText,
        NameText = row.NameText,
        SelectedExpenseCategory = row.SelectedExpenseCategory,
        SelectedTag = row.SelectedTag,
        IsIoU = row.IsIoU
    };

    private static bool RowsMatch(TransactionSplitRowVM row, TransactionSplitRowVM savedRow) =>
        row.AmountText == savedRow.AmountText &&
        string.Equals(row.NameText.Trim(), savedRow.NameText.Trim(), StringComparison.Ordinal) &&
        row.SelectedExpenseCategory == savedRow.SelectedExpenseCategory &&
        row.SelectedTag?.Id == savedRow.SelectedTag?.Id &&
        row.IsIoU == savedRow.IsIoU;

    private void SubscribeSplitRow(TransactionSplitRowVM row) =>
        row.PropertyChanged += OnSplitRowPropertyChanged;

    private void UnsubscribeSplitRow(TransactionSplitRowVM row) =>
        row.PropertyChanged -= OnSplitRowPropertyChanged;

    private void TrackRemovedRow(TransactionSplitRowVM row)
    {
        if (row.TransactionId is not null)
            _removedSplitRows.Add(row);
    }

    private void UpdateSplitNegativeRemainderState(decimal remainder, TransactionSplitRowVM? causingRow)
    {
        foreach (var row in SplitRows)
            row.IsCausingNegativeRemainder = false;

        HasNegativeSplitRemainder = remainder < 0m;
        NegativeRemainderRow = HasNegativeSplitRemainder ? causingRow : null;

        if (NegativeRemainderRow is not null)
            NegativeRemainderRow.IsCausingNegativeRemainder = true;
    }

    public sealed record ExpenseCategoryOption(string Label, ExpenseCategory Value);

    internal static bool RequiresMaximumSpendingConfirmation(
        int currentAccountId,
        int destinationAccountId,
        decimal maximumSpending,
        decimal destinationSpending,
        decimal amount,
        bool overflowApproved)
    {
        return !overflowApproved &&
               currentAccountId != destinationAccountId &&
               maximumSpending > 0m &&
               destinationSpending + amount > maximumSpending;
    }

    internal static decimal CalculateAccountSpending(IEnumerable<Transaction> transactions, int accountId)
    {
        var expenses = transactions
            .Where(transaction => transaction.SourceAccountId == accountId &&
                                  transaction.Type == TransactionType.Expense &&
                                  !transaction.IsForDeletion)
            .ToList();
        var parentIds = expenses
            .Where(transaction => transaction.ParentTransactionId.HasValue)
            .Select(transaction => transaction.ParentTransactionId!.Value)
            .ToHashSet();

        return expenses.Where(transaction => !parentIds.Contains(transaction.Id)).Sum(transaction => transaction.Amount);
    }

    public readonly record struct TransactionSplitSaveResult(
        bool IsSuccess,
        string? ErrorMessage,
        bool RequiresConfirmation)
    {
        public static TransactionSplitSaveResult Success()
        {
            return new TransactionSplitSaveResult(true, null, false);
        }

        public static TransactionSplitSaveResult Failure(string? errorMessage)
        {
            return new TransactionSplitSaveResult(false, errorMessage, false);
        }

        public static TransactionSplitSaveResult Confirmation(string message)
        {
            return new TransactionSplitSaveResult(false, message, true);
        }
    }

    private readonly record struct TransactionDetailInput(
        string Name,
        decimal Amount,
        bool IsPinned,
        int AccountId,
        DateTime Date,
        string Note,
        ExpenseCategory Category,
        int TagId,
        bool IsIoU,
        bool ShouldAffectBalance,
        bool IsExcludedFromBudget);

    private readonly record struct TransactionSplitInput(
        int? TransactionId,
        int ParentTransactionId,
        string Name,
        decimal Amount,
        ExpenseCategory Category,
        int? TagId,
        string Note,
        DateTime Date,
        bool IsIoU,
        bool IsExcludedFromBudget);

    private readonly record struct TransactionDetailSavedState(
        string Name,
        decimal Amount,
        bool IsPinned,
        string Note,
        DateTime Date,
        ExpenseCategory Category,
        int AccountId,
        int TagId,
        bool IsIoU,
        bool ShouldAffectBalance,
        bool IsExcludedFromBudget);
}
