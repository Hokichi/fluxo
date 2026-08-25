using System.Collections.ObjectModel;
using Fluxo.Helpers.Settings;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Persistence;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell;

namespace Fluxo.ViewModels.Popups.Settings;

public partial class SettingsGoalsTabVM : ObservableObject
{
    private const int PageSize = 25;

    private readonly IMessenger _messenger;
    private readonly IAppDataService _appData;
    private readonly HashSet<SettingsSavingGoalItemVM> _savingGoalsVisibleWindow = [];
    private int _visibleSavingGoalCount = PageSize;

    [ObservableProperty] private bool _isGoalChecksEnabled;
    [ObservableProperty] private bool _isDashboardSpendingAmountGateLocked;
    [ObservableProperty] private bool _hasMoreItems;
    [ObservableProperty] private bool _isLoading;

    public SettingsGoalsTabVM(IAppDataService appData, IMessenger? messenger = null)
    {
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;

        SavingGoalsView = CollectionViewSource.GetDefaultView(SavingGoals);
        SavingGoalsView.Filter = FilterSavingGoal;
    }

    public ObservableCollection<SettingsSavingGoalItemVM> SavingGoals { get; } = [];
    public ICollectionView SavingGoalsView { get; }
    public bool AreAllGoalsChecked => SavingGoals.Count > 0 && SavingGoals.All(item => item.IsChecked);
    public bool HasCheckedGoals => SavingGoals.Any(item => item.IsChecked);
    public bool HasSavingGoals => SavingGoals.Count > 0;

    public bool ShowGoalDisableActionButton =>
        IsGoalChecksEnabled && ShouldShowDisableAction(SavingGoals);

    public bool ShowGoalEnableActionButton =>
        IsGoalChecksEnabled && !ShouldShowDisableAction(SavingGoals);

    public bool ShowGoalCheckAllButton => IsGoalChecksEnabled && !AreAllGoalsChecked;
    public bool ShowGoalUncheckAllButton => IsGoalChecksEnabled && AreAllGoalsChecked;
    public bool ShowGoalEnableChecksButton => !IsGoalChecksEnabled && HasSavingGoals;

    public async Task LoadAsync()
    {
        await RefreshSavingGoalsAsync();
        IsGoalChecksEnabled = false;
    }

    public AddSavingGoalVM CreateAddSavingGoalViewModel()
    {
        return new AddSavingGoalVM(_appData);
    }

    public async Task<AddSavingGoalVM?> CreateEditSavingGoalViewModelAsync(int savingGoalId)
    {
        var goal = await _appData.GetSavingGoalByIdAsync(savingGoalId);
        if (goal is null)
            return null;

        return new AddSavingGoalVM(_appData)
        {
            EditingId = goal.Id,
            NameText = goal.Name,
            TargetAmountText = goal.TargetAmount,
            CurrentAmountText = goal.CurrentAmount,
            EndDate = goal.SavingEndDate,
            HasDefiniteEndDate = goal.SavingEndDate.HasValue
        };
    }

    public async Task OpenAddSavingGoalAsync()
    {
        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddSavingGoal,
                CreateAddSavingGoalViewModel())));
        await RefreshSavingGoalsAsync(resetPagination: false);
    }

    public async Task OpenEditSavingGoalAsync(int savingGoalId)
    {
        var viewModel = await CreateEditSavingGoalViewModelAsync(savingGoalId);
        if (viewModel is null)
            return;

        _messenger.Send(new SettingsDialogRequestedMessage(
            new SettingsDialogRequest(
                SettingsDialogRequestType.AddSavingGoal,
                viewModel)));
        await RefreshSavingGoalsAsync(resetPagination: false);
        SelectSingleItem(savingGoalId);
    }

    public void ClearSelections()
    {
        SetSelections(false);
    }

    public void SetSelections(bool isChecked)
    {
        foreach (var item in SavingGoals)
            item.IsChecked = isChecked;
    }

    public bool ShouldWarnBeforeApplyingToAll(SettingsBatchAction action)
    {
        if (action != SettingsBatchAction.Disable || SavingGoals.Count == 0)
            return false;

        var selectedCount = SavingGoals.Count(item => item.IsChecked);
        return selectedCount == SavingGoals.Count;
    }

    public async Task<SettingsOperationResult> ExecuteActionAsync(
        SettingsBatchAction action,
        IReadOnlyCollection<int>? selectedIdsOverride = null)
    {
        using var persistenceBatch = AppDataPersistenceBatch.Begin(_appData);
        var selectedIds = SettingsShared.NormalizeSelectionIds(selectedIdsOverride, SavingGoals.Select(item => item.Id),
            SavingGoals.Where(item => item.IsChecked).Select(item => item.Id));
        var selectedItemIds = selectedIds.ToHashSet();
        var selectedItems = SavingGoals.Where(item => selectedItemIds.Contains(item.Id)).ToArray();
        if (selectedItems.Length == 0)
            return SettingsOperationResult.Failure("Select at least one goal first.");

        var actions = new List<ILogMemoryAction>();

        try
        {
            switch (action)
            {
                case SettingsBatchAction.Delete:
                    foreach (var selectedItem in selectedItems)
                    {
                        var savingGoal = await _appData.GetSavingGoalByIdAsync(selectedItem.Id);
                        if (savingGoal is null)
                            continue;

                        var snapshot = SavingGoalMemorySnapshot.Create(savingGoal);
                        _appData.RemoveSavingGoal(savingGoal);
                        actions.Add(new DeleteSavingGoalMemoryAction(snapshot));
                    }

                    break;

                case SettingsBatchAction.Unpin:
                case SettingsBatchAction.Pin:
                    return SettingsOperationResult.Failure("Pin and unpin are not supported for goals.");

                case SettingsBatchAction.Disable:
                case SettingsBatchAction.Enable:
                    var disabledGoalIds = SettingsShared.ParseIdSet(
                        await SettingsShared.GetSettingsDictionaryAsync(_appData),
                        UserSettingNames.DisabledSavingGoalIds);

                    if (action == SettingsBatchAction.Disable)
                        disabledGoalIds.UnionWith(selectedItems.Select(item => item.Id));
                    else
                        disabledGoalIds.ExceptWith(selectedItems.Select(item => item.Id));

                    await SettingsShared.UpdateIdSetSettingAsync(_appData, UserSettingNames.DisabledSavingGoalIds,
                        disabledGoalIds, actions);
                    break;
            }

            if (actions.Count == 0)
                return SettingsOperationResult.Failure("Nothing changed for the selected goals.");

            await persistenceBatch.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to update selected saving goals from settings.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("update selected goals"));
        }

        try
        {
            _messenger.Send(new SettingsDataChangedMessage(SettingsDataChangedScope.SavingGoals));
            _messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.SavingGoals));
            await RefreshSavingGoalsAsync(resetPagination: false);

            var actionKey = action.ToString().ToLowerInvariant();
            var verb = actionKey switch
            {
                "delete" => "deleted",
                "disable" => "disabled",
                _ => "enabled"
            };
            var header = selectedItems.Length == 1
                ? selectedItems[0].Name
                : $"{selectedItems.Length} goals";
            var message = actionKey switch
            {
                "delete" => "Selected goals were removed.",
                "disable" => "Selected goals were disabled.",
                _ => "Selected goals were enabled."
            };
            FloatingNotificationPublisher.Success(
                _messenger, header, message,
                headerAction:
                char.ToUpperInvariant(verb[0]) + verb[1..]);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(
                exception,
                "Saving goals were updated, but the current UI could not be refreshed.");
        }

        return SettingsOperationResult.Success();
    }

    public Task<SettingsOperationResult> ExecuteItemActionAsync(int itemId, SettingsBatchAction action)
    {
        return ExecuteActionAsync(action, [itemId]);
    }

    public void SelectSingleItem(int itemId)
    {
        if (EnsureItemVisible(itemId))
            RefreshSavingGoalsView();

        foreach (var item in SavingGoals)
            item.IsSelected = item.Id == itemId;
    }

    public async Task RefreshSavingGoalsAsync(bool resetPagination = true)
    {
        var disabledSavingGoalIds = SettingsShared.ParseIdSet(
            await SettingsShared.GetSettingsDictionaryAsync(_appData),
            UserSettingNames.DisabledSavingGoalIds);

        SettingsShared.ReplaceCollection(SavingGoals, (await _appData.GetSavingGoalsAsync())
            .OrderBy(goal => goal.SavingEndDate ?? DateTime.MaxValue)
            .ThenBy(goal => goal.Name)
            .Select(goal => new SettingsSavingGoalItemVM(
                goal,
                !disabledSavingGoalIds.Contains(goal.Id))));

        AttachSelectableItemHandlers(SavingGoals);
        if (resetPagination)
            ResetPaginationWindow();
        else
            IsLoading = false;

        RefreshSavingGoalsView();
        OnPropertyChanged(nameof(HasSavingGoals));
        OnSelectionStateChanged();
    }

    partial void OnIsGoalChecksEnabledChanged(bool value)
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
            _visibleSavingGoalCount += PageSize;
            RefreshSavingGoalsView();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void AttachSelectableItemHandlers(IEnumerable<SettingsSavingGoalItemVM> items)
    {
        foreach (var item in items)
        {
            item.PropertyChanged -= OnSelectableItemPropertyChanged;
            item.PropertyChanged += OnSelectableItemPropertyChanged;
        }
    }

    private void OnSelectableItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSavingGoalItemVM.IsChecked))
            OnSelectionStateChanged();
    }

    private void OnSelectionStateChanged()
    {
        OnPropertyChanged(nameof(AreAllGoalsChecked));
        OnPropertyChanged(nameof(HasCheckedGoals));
        OnPropertyChanged(nameof(ShowGoalDisableActionButton));
        OnPropertyChanged(nameof(ShowGoalEnableActionButton));
        OnPropertyChanged(nameof(ShowGoalCheckAllButton));
        OnPropertyChanged(nameof(ShowGoalUncheckAllButton));
        OnPropertyChanged(nameof(ShowGoalEnableChecksButton));
    }

    private bool CanLoadMore()
    {
        return HasMoreItems && !IsLoading;
    }

    private bool FilterSavingGoal(object item)
    {
        return item is SettingsSavingGoalItemVM savingGoal &&
               _savingGoalsVisibleWindow.Contains(savingGoal);
    }

    private void ResetPaginationWindow()
    {
        _visibleSavingGoalCount = PageSize;
        IsLoading = false;
    }

    private bool EnsureItemVisible(int itemId)
    {
        var index = -1;
        for (var i = 0; i < SavingGoals.Count; i++)
            if (SavingGoals[i].Id == itemId)
            {
                index = i;
                break;
            }

        if (index < 0)
            return false;

        var requiredVisibleCount = ((index / PageSize) + 1) * PageSize;
        if (requiredVisibleCount <= _visibleSavingGoalCount)
            return false;

        _visibleSavingGoalCount = requiredVisibleCount;
        return true;
    }

    private void RefreshSavingGoalsView()
    {
        RecomputeVisibleWindow();
        SavingGoalsView.Refresh();
    }

    private void RecomputeVisibleWindow()
    {
        _savingGoalsVisibleWindow.Clear();

        foreach (var savingGoal in SavingGoals.Take(_visibleSavingGoalCount))
            _savingGoalsVisibleWindow.Add(savingGoal);

        HasMoreItems = SavingGoals.Count > _visibleSavingGoalCount;
    }

    private static bool ShouldShowDisableAction(IReadOnlyCollection<SettingsSavingGoalItemVM> items)
    {
        if (items.Count == 0)
            return true;

        var scopedItems = GetScopedItems(items);
        return scopedItems.Any(item => item.IsEnabled);
    }

    private static IReadOnlyList<SettingsSavingGoalItemVM> GetScopedItems(
        IReadOnlyCollection<SettingsSavingGoalItemVM> items)
    {
        var selectedItems = items.Where(item => item.IsChecked).ToArray();
        return selectedItems.Length > 0 ? selectedItems : items.ToArray();
    }
}

