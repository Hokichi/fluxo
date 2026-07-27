using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Popups;
using Fluxo.Helpers.Popups;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Popups.Settings;

public partial class SettingsAccountsTabVM : ObservableObject
{
    private const int PageSize = 25;

    private readonly MainVM _mainViewModel;
    private readonly IMessenger _messenger;
    private readonly IAppDataService _appData;
    private readonly HashSet<SettingsAccountItemVM> _accountsVisibleWindow = [];
    private int _visibleAccountCount = PageSize;

    [ObservableProperty] private bool _isAccountChecksEnabled;
    [ObservableProperty] private bool _hasMoreItems;
    [ObservableProperty] private bool _isLoading;

    public SettingsAccountsTabVM(MainVM mainViewModel, IAppDataService appData, IMessenger? messenger = null)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        AccountsView = CollectionViewSource.GetDefaultView(Accounts);
        AccountsView.Filter = FilterAccount;
    }

    public ObservableCollection<SettingsAccountItemVM> Accounts { get; } = [];
    public ICollectionView AccountsView { get; }
    public bool AreAllAccountsChecked => Accounts.Count > 0 && Accounts.All(item => item.IsChecked);
    public bool HasCheckedAccounts => Accounts.Any(item => item.IsChecked);
    public bool HasAccounts => Accounts.Count > 0;

    public bool ShowAccountUnpinActionButton =>
        IsAccountChecksEnabled && ShouldShowUnpinAction(Accounts);
    public bool ShowAccountPinActionButton =>
        IsAccountChecksEnabled && !ShouldShowUnpinAction(Accounts);
    public bool ShowAccountDisableActionButton =>
        IsAccountChecksEnabled && ShouldShowDisableAction(Accounts);
    public bool ShowAccountEnableActionButton =>
        IsAccountChecksEnabled && !ShouldShowDisableAction(Accounts);
    public bool ShowAccountCheckAllButton => IsAccountChecksEnabled && !AreAllAccountsChecked;
    public bool ShowAccountUncheckAllButton => IsAccountChecksEnabled && AreAllAccountsChecked;
    public bool ShowAccountEnableChecksButton => !IsAccountChecksEnabled && HasAccounts;

    public async Task LoadAsync()
    {
        await RefreshAccountsAsync(resetPagination: true);
        IsAccountChecksEnabled = false;
    }

    public AddAccountVM CreateAddAccountViewModel()
    {
        return new AddAccountVM(_mainViewModel, _appData);
    }

    public AccountDetailVM CreateAccountDetailViewModel(int accountId)
    {
        return new AccountDetailVM(_mainViewModel, accountId, _appData);
    }

    public async Task OpenAddAccountAsync()
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddAccount,
                CreateAddAccountViewModel())));
        await RefreshAccountsAsync();
        _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.Accounts));
    }

    public async Task OpenAccountDetailAsync(int accountId)
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AccountDetail,
                CreateAccountDetailViewModel(accountId))));
        await RefreshAccountsAsync(keepVisibleItemId: accountId);
        _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.Accounts));
        SelectSingleItem(accountId);
    }

    public Task<string> BuildDeleteConfirmationMessageAsync(
        int accountId,
        string? fallbackSourceName = null,
        CancellationToken cancellationToken = default)
    {
        return AccountDeletionConfirmationHelper.BuildDeleteConfirmationMessageAsync(
            _appData,
            accountId,
            fallbackSourceName,
            cancellationToken);
    }

    public void ClearSelections()
    {
        SetSelections(false);
    }

    public void SetSelections(bool isChecked)
    {
        foreach (var item in Accounts)
            item.IsChecked = isChecked;
    }

    public bool ShouldWarnBeforeApplyingToAll(SettingsBatchAction action)
    {
        if (action is not (SettingsBatchAction.Unpin or SettingsBatchAction.Disable))
            return false;

        if (Accounts.Count == 0)
            return false;

        var selectedCount = Accounts.Count(item => item.IsChecked);
        return selectedCount == Accounts.Count;
    }

    public async Task<SettingsOperationResult> ExecuteActionAsync(
        SettingsBatchAction action,
        IReadOnlyCollection<int>? selectedIdsOverride = null)
    {
        var selectedIds = SettingsShared.NormalizeSelectionIds(selectedIdsOverride, Accounts.Select(item => item.Id),
            Accounts.Where(item => item.IsChecked).Select(item => item.Id));
        if (selectedIds.Length == 0)
            return SettingsOperationResult.Failure("Select at least one account first.");

        var actions = new List<ILogMemoryAction>();

        try
        {
            switch (action)
            {
                case SettingsBatchAction.Delete:
                    var allTransactions = await _appData.GetTransactionsAsync();
                    var transactionsBySourceId = allTransactions
                        .GroupBy(log => log.SourceAccountId)
                        .ToDictionary(group => group.Key, group => (IReadOnlyList<Transaction>)group.ToList());

                    foreach (var selectedId in selectedIds)
                    {
                        var account = await _appData.GetAccountByIdAsync(selectedId);
                        if (account is null)
                            continue;

                        if (transactionsBySourceId.TryGetValue(selectedId, out var transactions))
                            foreach (var transaction in transactions)
                                _appData.RemoveTransaction(transaction);

                        var snapshot = AccountMemorySnapshot.Create(account);
                        _appData.RemoveAccount(account);
                        actions.Add(new DeleteAccountMemoryAction(snapshot));
                    }

                    break;

                case SettingsBatchAction.Unpin:
                case SettingsBatchAction.Pin:
                case SettingsBatchAction.Disable:
                case SettingsBatchAction.Enable:
                    foreach (var selectedId in selectedIds)
                    {
                        var account = await _appData.GetAccountByIdAsync(selectedId);
                        if (account is null)
                            continue;

                        var beforeSnapshot = AccountMemorySnapshot.Create(account);
                        var updated = false;

                        switch (action)
                        {
                            case SettingsBatchAction.Unpin when account.PinnedOnUI:
                                account.PinnedOnUI = false;
                                updated = true;
                                break;
                            case SettingsBatchAction.Pin when !account.PinnedOnUI && account.IsEnabled:
                                account.PinnedOnUI = true;
                                updated = true;
                                break;
                            case SettingsBatchAction.Disable when account.IsEnabled:
                                account.IsEnabled = false;
                                account.PinnedOnUI = false;
                                updated = true;
                                break;
                            case SettingsBatchAction.Enable when !account.IsEnabled:
                                account.IsEnabled = true;
                                account.PinnedOnUI = true;
                                updated = true;
                                break;
                        }

                        if (!updated)
                            continue;

                        _appData.UpdateAccount(account);
                        var afterSnapshot = AccountMemorySnapshot.Create(account);
                        actions.Add(new EditAccountMemoryAction(beforeSnapshot, afterSnapshot));
                    }

                    break;
            }

            if (actions.Count == 0)
                return SettingsOperationResult.Failure("Nothing changed for the selected accounts.");

            await _appData.SaveChangesAsync();
            _messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));
            var affectedNames = Accounts.Where(item => selectedIds.Contains(item.Id)).Select(item => item.Name).ToArray();
            await _mainViewModel.ReloadCurrentDataAsync();
            await RefreshAccountsAsync();
            _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.Accounts));

            var actionKey = action.ToString().ToLowerInvariant();
            var verb = actionKey switch
            {
                "delete" => "deleted",
                "unpin" => "unpinned",
                "pin" => "pinned",
                "disable" => "disabled",
                _ => "enabled"
            };
            var header = affectedNames.Length == 1 ? affectedNames[0] : $"{affectedNames.Length} accounts";
            var message = actionKey switch
            {
                "delete" => "Selected accounts were removed.",
                "unpin" => "Selected accounts were removed from the dashboard.",
                "pin" => "Selected accounts were added to the dashboard.",
                "disable" => "Selected accounts were disabled.",
                _ => "Selected accounts were enabled."
            };
            FloatingNotificationPublisher.Success(
                _messenger, header, message,
                headerAction:
                char.ToUpperInvariant(verb[0]) + verb[1..]);
            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to update selected accounts from settings.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("update selected accounts"));
        }
    }

    public Task<SettingsOperationResult> ExecuteItemActionAsync(int itemId, SettingsBatchAction action)
    {
        return ExecuteActionAsync(action, [itemId]);
    }

    public void SelectSingleItem(int itemId)
    {
        if (EnsureItemVisible(itemId))
            RefreshAccountsView();

        foreach (var item in Accounts)
            item.IsSelected = item.Id == itemId;
    }

    public async Task RefreshAccountsAsync(bool resetPagination = false, int? keepVisibleItemId = null)
    {
        SettingsShared.ReplaceCollection(Accounts, (await _appData.GetAccountsAsync())
            .OrderByDescending(source => source.PinnedOnUI)
            .ThenBy(source => source.Name)
            .Select(source => new SettingsAccountItemVM(source)));

        AttachSelectableItemHandlers(Accounts);
        if (resetPagination)
            ResetPaginationWindow();
        else
            IsLoading = false;

        if (keepVisibleItemId is int itemId)
            EnsureItemVisible(itemId);

        RefreshAccountsView();
        OnPropertyChanged(nameof(HasAccounts));
        OnSelectionStateChanged();
    }

    partial void OnIsAccountChecksEnabledChanged(bool value)
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
            _visibleAccountCount += PageSize;
            RefreshAccountsView();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void AttachSelectableItemHandlers(IEnumerable<SettingsAccountItemVM> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= OnSelectableItemPropertyChanged;
            item.PropertyChanged += OnSelectableItemPropertyChanged;
        }
    }

    private void OnSelectableItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsAccountItemVM.IsChecked))
            OnSelectionStateChanged();
    }

    private void OnSelectionStateChanged()
    {
        OnPropertyChanged(nameof(AreAllAccountsChecked));
        OnPropertyChanged(nameof(HasCheckedAccounts));
        OnPropertyChanged(nameof(ShowAccountUnpinActionButton));
        OnPropertyChanged(nameof(ShowAccountPinActionButton));
        OnPropertyChanged(nameof(ShowAccountDisableActionButton));
        OnPropertyChanged(nameof(ShowAccountEnableActionButton));
        OnPropertyChanged(nameof(ShowAccountCheckAllButton));
        OnPropertyChanged(nameof(ShowAccountUncheckAllButton));
        OnPropertyChanged(nameof(ShowAccountEnableChecksButton));
    }

    private bool CanLoadMore()
    {
        return HasMoreItems && !IsLoading;
    }

    private bool FilterAccount(object item)
    {
        return item is SettingsAccountItemVM account &&
               _accountsVisibleWindow.Contains(account);
    }

    private void ResetPaginationWindow()
    {
        _visibleAccountCount = PageSize;
        IsLoading = false;
    }

    private bool EnsureItemVisible(int itemId)
    {
        var index = -1;
        for (var i = 0; i < Accounts.Count; i++)
            if (Accounts[i].Id == itemId)
            {
                index = i;
                break;
            }

        if (index < 0)
            return false;

        var requiredVisibleCount = ((index / PageSize) + 1) * PageSize;
        if (requiredVisibleCount <= _visibleAccountCount)
            return false;

        _visibleAccountCount = requiredVisibleCount;
        return true;
    }

    private void RefreshAccountsView()
    {
        RecomputeVisibleWindow();
        AccountsView.Refresh();
    }

    private void RecomputeVisibleWindow()
    {
        _accountsVisibleWindow.Clear();

        foreach (var account in Accounts.Take(_visibleAccountCount))
            _accountsVisibleWindow.Add(account);

        HasMoreItems = Accounts.Count > _visibleAccountCount;
    }

    private static bool ShouldShowUnpinAction(IReadOnlyCollection<SettingsAccountItemVM> items)
    {
        if (items.Count == 0)
            return true;

        var scopedItems = GetScopedItems(items);
        return scopedItems.Any(item => !item.IsUnpinned);
    }

    private static bool ShouldShowDisableAction(IReadOnlyCollection<SettingsAccountItemVM> items)
    {
        if (items.Count == 0)
            return true;

        var scopedItems = GetScopedItems(items);
        return scopedItems.Any(item => item.IsEnabled);
    }

    private static IReadOnlyList<SettingsAccountItemVM> GetScopedItems(
        IReadOnlyCollection<SettingsAccountItemVM> items)
    {
        var selectedItems = items.Where(item => item.IsChecked).ToArray();
        return selectedItems.Length > 0 ? selectedItems : items.ToArray();
    }
}

