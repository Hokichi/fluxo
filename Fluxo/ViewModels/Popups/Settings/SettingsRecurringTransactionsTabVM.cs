using System.Collections.ObjectModel;
using Fluxo.Helpers.Settings;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Logging;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell;

namespace Fluxo.ViewModels.Popups.Settings;

public partial class SettingsRecurringTransactionsTabVM : ObservableObject
{
    private const int PageSize = 25;

    private readonly IMessenger _messenger;
    private readonly IAppDataService _appData;
    private readonly HashSet<SettingsRecurringTransactionItemVM> _fixedExpensesVisibleWindow = [];
    private int _visibleRecurringTransactionCount = PageSize;

    [ObservableProperty] private bool _isRecurringTransactionChecksEnabled;
    [ObservableProperty] private bool _isDashboardSpendingAmountGateLocked;
    [ObservableProperty] private bool _hasMoreItems;
    [ObservableProperty] private bool _isLoading;

    public SettingsRecurringTransactionsTabVM(IAppDataService appData, IMessenger messenger)
    {
        _appData = appData;
        _messenger = messenger;

        RecurringTransactionsView = CollectionViewSource.GetDefaultView(RecurringTransactions);
        RecurringTransactionsView.Filter = FilterRecurringTransaction;
    }

    public ObservableCollection<SettingsRecurringTransactionItemVM> RecurringTransactions { get; } = [];
    public ICollectionView RecurringTransactionsView { get; }
    public bool AreAllRecurringTransactionsChecked => RecurringTransactions.Count > 0 && RecurringTransactions.All(item => item.IsChecked);
    public bool HasCheckedRecurringTransactions => RecurringTransactions.Any(item => item.IsChecked);
    public bool HasRecurringTransactions => RecurringTransactions.Count > 0;

    public bool ShowRecurringTransactionDisableActionButton =>
        IsRecurringTransactionChecksEnabled && ShouldShowDisableAction(RecurringTransactions);
    public bool ShowRecurringTransactionEnableActionButton =>
        IsRecurringTransactionChecksEnabled && !ShouldShowDisableAction(RecurringTransactions);
    public bool ShowRecurringTransactionCheckAllButton => IsRecurringTransactionChecksEnabled && !AreAllRecurringTransactionsChecked;
    public bool ShowRecurringTransactionUncheckAllButton => IsRecurringTransactionChecksEnabled && AreAllRecurringTransactionsChecked;
    public bool ShowRecurringTransactionEnableChecksButton => !IsRecurringTransactionChecksEnabled && HasRecurringTransactions;

    public TransactionPopupRequest CreateAddRecurringTransactionViewModel() =>
        TransactionPopupRequest.AddRecurring(isLocked: true);

    public async Task LoadAsync()
    {
        await RefreshRecurringTransactionsAsync();
        IsRecurringTransactionChecksEnabled = false;
    }

    public async Task OpenAddRecurringTransactionAsync()
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddRecurringTransaction,
                CreateAddRecurringTransactionViewModel())));
        await RefreshRecurringTransactionsAsync(resetPagination: false);
    }

    public async Task OpenEditRecurringTransactionAsync(int fixedExpenseId)
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddRecurringTransaction,
                new TransactionPopupRequest
                {
                    Kind = TransactionPopupRequestKind.EditRecurringTransaction,
                    RecurringTransactionId = fixedExpenseId
                })));

        await RefreshRecurringTransactionsAsync(resetPagination: false);
        SelectSingleItem(fixedExpenseId);
    }

    public void ClearSelections()
    {
        SetSelections(false);
    }

    public void SetSelections(bool isChecked)
    {
        foreach (var item in RecurringTransactions)
            item.IsChecked = isChecked;
    }

    public bool ShouldWarnBeforeApplyingToAll(SettingsBatchAction action)
    {
        if (action != SettingsBatchAction.Disable || RecurringTransactions.Count == 0)
            return false;

        var selectedCount = RecurringTransactions.Count(item => item.IsChecked);
        return selectedCount == RecurringTransactions.Count;
    }

    public async Task<SettingsOperationResult> ExecuteActionAsync(
        SettingsBatchAction action,
        IReadOnlyCollection<int>? selectedIdsOverride = null)
    {
        var selectedIds = SettingsShared.NormalizeSelectionIds(selectedIdsOverride, RecurringTransactions.Select(item => item.Id),
            RecurringTransactions.Where(item => item.IsChecked).Select(item => item.Id));
        var selectedItemIds = selectedIds.ToHashSet();
        var selectedItems = RecurringTransactions.Where(item => selectedItemIds.Contains(item.Id)).ToArray();
        if (selectedItems.Length == 0)
            return SettingsOperationResult.Failure("Select at least one recurring transaction first.");

        try
        {
            switch (action)
            {
                case SettingsBatchAction.Delete:
                    foreach (var selectedItem in selectedItems)
                    {
                        var recurring = await _appData.GetRecurringTransactionByIdAsync(selectedItem.Id);
                        if (recurring is null)
                            continue;

                        _appData.RemoveRecurringTransaction(recurring);
                    }

                    break;

                case SettingsBatchAction.Disable:
                case SettingsBatchAction.Enable:
                    foreach (var selectedItem in selectedItems)
                    {
                        var recurring = await _appData.GetRecurringTransactionByIdAsync(selectedItem.Id);
                        if (recurring is null)
                            continue;
                        recurring.IsEnabled = action == SettingsBatchAction.Enable;
                        _appData.UpdateRecurringTransaction(recurring);
                    }
                    break;

                case SettingsBatchAction.Unpin:
                case SettingsBatchAction.Pin:
                    return SettingsOperationResult.Failure(
                        "Pin and unpin are not supported for recurring transactions.");
            }

            await _appData.SaveChangesAsync();
            _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.RecurringTransactions));
            _messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));
            await RefreshRecurringTransactionsAsync(resetPagination: false);

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to update selected recurring transactions from settings.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("update selected recurring transactions"));
        }
    }

    public Task<SettingsOperationResult> ExecuteItemActionAsync(int itemId, SettingsBatchAction action)
    {
        return ExecuteActionAsync(action, [itemId]);
    }

    public void SelectSingleItem(int itemId)
    {
        if (EnsureItemVisible(itemId))
            RefreshRecurringTransactionsView();

        foreach (var item in RecurringTransactions)
            item.IsSelected = item.Id == itemId;
    }

    public async Task RefreshRecurringTransactionsAsync(bool resetPagination = true)
    {
        SettingsShared.ReplaceCollection(RecurringTransactions, (await _appData.GetRecurringTransactionsAsync())
            .OrderBy(transaction => transaction.Name)
            .Select(transaction => new SettingsRecurringTransactionItemVM(transaction)));

        AttachSelectableItemHandlers(RecurringTransactions);
        if (resetPagination)
            ResetPaginationWindow();
        else
            IsLoading = false;

        RefreshRecurringTransactionsView();
        OnPropertyChanged(nameof(HasRecurringTransactions));
        OnSelectionStateChanged();
    }

    partial void OnIsRecurringTransactionChecksEnabledChanged(bool value)
    {
        OnSelectionStateChanged();
    }

    partial void OnHasMoreItemsChanged(bool value)
    {
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        LoadMoreCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private void LoadMore()
    {
        if (!CanLoadMore())
            return;

        IsLoading = true;
        try
        {
            _visibleRecurringTransactionCount += PageSize;
            RefreshRecurringTransactionsView();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void AttachSelectableItemHandlers(IEnumerable<SettingsRecurringTransactionItemVM> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= OnSelectableItemPropertyChanged;
            item.PropertyChanged += OnSelectableItemPropertyChanged;
        }
    }

    private void OnSelectableItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsRecurringTransactionItemVM.IsChecked))
            OnSelectionStateChanged();
    }

    private void OnSelectionStateChanged()
    {
        OnPropertyChanged(nameof(AreAllRecurringTransactionsChecked));
        OnPropertyChanged(nameof(HasCheckedRecurringTransactions));
        OnPropertyChanged(nameof(ShowRecurringTransactionDisableActionButton));
        OnPropertyChanged(nameof(ShowRecurringTransactionEnableActionButton));
        OnPropertyChanged(nameof(ShowRecurringTransactionCheckAllButton));
        OnPropertyChanged(nameof(ShowRecurringTransactionUncheckAllButton));
        OnPropertyChanged(nameof(ShowRecurringTransactionEnableChecksButton));
    }

    private bool CanLoadMore()
    {
        return HasMoreItems && !IsLoading;
    }

    private bool FilterRecurringTransaction(object item)
    {
        return item is SettingsRecurringTransactionItemVM fixedExpense &&
               _fixedExpensesVisibleWindow.Contains(fixedExpense);
    }

    private void ResetPaginationWindow()
    {
        _visibleRecurringTransactionCount = PageSize;
        IsLoading = false;
    }

    private bool EnsureItemVisible(int itemId)
    {
        var index = -1;
        for (var i = 0; i < RecurringTransactions.Count; i++)
            if (RecurringTransactions[i].Id == itemId)
            {
                index = i;
                break;
            }

        if (index < 0)
            return false;

        var requiredVisibleCount = ((index / PageSize) + 1) * PageSize;
        if (requiredVisibleCount <= _visibleRecurringTransactionCount)
            return false;

        _visibleRecurringTransactionCount = requiredVisibleCount;
        return true;
    }

    private void RefreshRecurringTransactionsView()
    {
        RecomputeVisibleWindow();
        RecurringTransactionsView.Refresh();
    }

    private void RecomputeVisibleWindow()
    {
        _fixedExpensesVisibleWindow.Clear();

        foreach (var fixedExpense in RecurringTransactions.Take(_visibleRecurringTransactionCount))
            _fixedExpensesVisibleWindow.Add(fixedExpense);

        HasMoreItems = RecurringTransactions.Count > _visibleRecurringTransactionCount;
    }

    private static bool ShouldShowDisableAction(IReadOnlyCollection<SettingsRecurringTransactionItemVM> items)
    {
        if (items.Count == 0)
            return true;

        var scopedItems = GetScopedItems(items);
        return scopedItems.Any(item => item.IsEnabled);
    }

    private static IReadOnlyList<SettingsRecurringTransactionItemVM> GetScopedItems(
        IReadOnlyCollection<SettingsRecurringTransactionItemVM> items)
    {
        var selectedItems = items.Where(item => item.IsChecked).ToArray();
        return selectedItems.Length > 0 ? selectedItems : items.ToArray();
    }
}

