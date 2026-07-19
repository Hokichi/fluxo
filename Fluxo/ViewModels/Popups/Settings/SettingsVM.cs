using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Logging;
using Fluxo.Services.Ui;
using Fluxo.Services.History;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Shell.Main;

namespace Fluxo.ViewModels.Popups.Settings;

public partial class SettingsVM : ObservableRecipient, IRecipient<SettingsPendingChangesChangedMessage>,
    IRecipient<SettingsMaintenanceRequestedMessage>, IRecipient<SettingsDataChangedMessage>, IDisposable
{
    private static readonly IReadOnlyList<Tag> RequiredDeleteAllDataSystemTags =
    [
        new()
        {
            Name = SystemTags.BalanceUpdateName,
            HexCode = SystemTags.BalanceUpdateHexCode,
            IsSystemTag = true
        },
        new()
        {
            Name = SystemTags.GoalUpdateName,
            HexCode = SystemTags.GoalUpdateHexCode,
            IsSystemTag = true
        },
        new()
        {
            Name = SystemTags.DataRestorationName,
            HexCode = SystemTags.DataRestorationHexCode,
            IsSystemTag = true
        },
        new()
        {
            Name = SystemTags.BudgetReconciliationName,
            HexCode = SystemTags.BudgetReconciliationHexCode,
            IsSystemTag = true
        }
    ];

    private static readonly IReadOnlyDictionary<string, string> SettingsDefaultsAfterDeletion =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [UserSettingNames.IsRecurringOverdueNotifEnabled] = bool.TrueString,
            [UserSettingNames.IsGoalOverdueNotifEnabled] = bool.TrueString,
            [UserSettingNames.IsDailyAllowanceNotifEnabled] = bool.TrueString,
            [UserSettingNames.IsLatePaymentNotifEnabled] = bool.FalseString,
            [UserSettingNames.IsBudgetThresholdNotifEnabled] = bool.FalseString,
            [UserSettingNames.IsLowCreditNotifEnabled] = bool.FalseString,
            [UserSettingNames.IsLowAccountBalanceNotifEnabled] = bool.FalseString,
            [UserSettingNames.ShouldRunAtStartup] = bool.FalseString,
            [UserSettingNames.CloseBehavior] = AppCloseBehavior.Exit.ToString(),
            [UserSettingNames.NotificationsSnoozeEndDate] = string.Empty
        };

    private readonly MainVM _mainViewModel;
    private readonly IUiSettleAwaiter _uiSettleAwaiter;
    private readonly IAppDataService _appData;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private bool _isBudgetPending;
    private bool _isPersonalizationPending;
    private bool _isDisposed;

    public SettingsVM(
        MainVM mainViewModel,
        IAppDataService appData,
        IStartupRegistrationService startupRegistrationService,
        IUiSettleAwaiter uiSettleAwaiter,
        SettingsBudgetTabVM budgetTab,
        SettingsAccountsTabVM accountsTab,
        SettingsRecurringTransactionsTabVM fixedExpensesTab,
        SettingsGoalsTabVM goalsTab,
        SettingsIoUsTabVM debtIousTab,
        SettingsTagsTabVM tagsTab,
        SettingsPersonalizationTabVM personalizationTab,
        IMessenger? messenger = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _startupRegistrationService = startupRegistrationService;
        _uiSettleAwaiter = uiSettleAwaiter;
        BudgetTab = budgetTab;
        AccountsTab = accountsTab;
        RecurringTransactionsTab = fixedExpensesTab;
        GoalsTab = goalsTab;
        IoUsTab = debtIousTab;
        TagsTab = tagsTab;
        PersonalizationTab = personalizationTab;
        IsActive = true;
    }

    public SettingsBudgetTabVM BudgetTab { get; }
    public SettingsAccountsTabVM AccountsTab { get; }
    public SettingsRecurringTransactionsTabVM RecurringTransactionsTab { get; }
    public SettingsGoalsTabVM GoalsTab { get; }
    public SettingsIoUsTabVM IoUsTab { get; }
    public SettingsTagsTabVM TagsTab { get; }
    public SettingsPersonalizationTabVM PersonalizationTab { get; }
    public bool IsDashboardSpendingAmountGateLocked => _mainViewModel.IsDashboardSpendingAmountGateLocked;
    public bool IsSufficientFundsActionGateLocked => _mainViewModel.IsSufficientFundsActionGateLocked;

    public bool HasPendingBudgetConfigurationChanges => _isBudgetPending;

    public bool HasPendingPersonalizationConfigurationChanges => _isPersonalizationPending;

    public bool CanSaveBudgetConfiguration => BudgetTab.CanSaveConfiguration;

    public string BudgetConfigurationErrorMessage => BudgetTab.ConfigurationErrorMessage;

    public bool HasPendingConfigurationChanges => _isBudgetPending || _isPersonalizationPending;

    public void Receive(SettingsPendingChangesChangedMessage message)
    {
        switch (message.Value.TabKey)
        {
            case SettingsTabKey.Budget:
                _isBudgetPending = message.Value.HasPendingChanges;
                OnPropertyChanged(nameof(HasPendingBudgetConfigurationChanges));
                break;

            case SettingsTabKey.Personalization:
                _isPersonalizationPending = message.Value.HasPendingChanges;
                OnPropertyChanged(nameof(HasPendingPersonalizationConfigurationChanges));
                break;

            default:
                return;
        }

        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
    }

    public void Receive(SettingsMaintenanceRequestedMessage message)
    {
        if (_isDisposed)
            return;

        _ = HandleMaintenanceRequestAsync(message.Value);
    }

    public void Receive(SettingsDataChangedMessage message)
    {
        if (message.Value.HasFlag(SettingsDataChangedScope.Accounts))
            RefreshDashboardSpendingAmountGateState();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        IsActive = false;
    }

    public async Task LoadAsync()
    {
        // Tab VMs share the same unit of work, so loads must be sequenced.
        await BudgetTab.LoadAsync();
        await AccountsTab.LoadAsync();
        await RecurringTransactionsTab.LoadAsync();
        await GoalsTab.LoadAsync();
        await IoUsTab.LoadAsync();
        await TagsTab.LoadAsync();
        await PersonalizationTab.LoadAsync();
        ApplyDashboardSpendingAmountGateState();

        OnPropertyChanged(nameof(IsAccountChecksEnabled));
        OnPropertyChanged(nameof(IsRecurringTransactionChecksEnabled));
        OnPropertyChanged(nameof(IsGoalChecksEnabled));
        OnPropertyChanged(nameof(HasPendingBudgetConfigurationChanges));
        OnPropertyChanged(nameof(HasPendingPersonalizationConfigurationChanges));
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
    }

    public async Task<SettingsOperationResult> ApplyConfigurationAsync()
    {
        var (budgetResult, budgetActions) = await BudgetTab.BuildApplyChangesAsync();
        if (!budgetResult.IsSuccess)
            return budgetResult;

        var (personalResult, personalizationActions, oldUsername, newUsername, _) =
            await PersonalizationTab.BuildApplyChangesAsync();
        if (!personalResult.IsSuccess)
            return personalResult;

        var actions = new List<ILogMemoryAction>();
        actions.AddRange(budgetActions);
        actions.AddRange(personalizationActions);

        if (actions.Count == 0 &&
            !BudgetTab.HasPendingChanges &&
            !PersonalizationTab.HasPendingChanges)
            return SettingsOperationResult.Success();

        try
        {
            _startupRegistrationService.SetRunAtStartup(PersonalizationTab.ShouldRunAtStartup);
            await _appData.SaveChangesAsync();

            if (!string.Equals(newUsername, oldUsername, StringComparison.Ordinal))
            {
                var resolvedUsername = newUsername ?? "User";
                Messenger.Send(new UsernameChangedMessage(resolvedUsername));

                try
                {
                    FluxoLogManager.Initialize(resolvedUsername);
                }
                catch (Exception loggerException)
                {
                    FluxoLogManager.LogWarning(
                        loggerException,
                        "Failed to rotate log file after username update from settings.");
                }
            }

            await _mainViewModel.ReloadUserSettingsAsync();
            Messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.All));
            await _mainViewModel.ReloadCurrentDataAsync();
            ApplyDashboardSpendingAmountGateState();

            BudgetTab.CommitSavedState();
            PersonalizationTab.CommitSavedState();
            OnPropertyChanged(nameof(HasPendingBudgetConfigurationChanges));
            OnPropertyChanged(nameof(HasPendingPersonalizationConfigurationChanges));
            OnPropertyChanged(nameof(HasPendingConfigurationChanges));

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to apply settings.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("apply settings"));
        }
    }

    public Task<SettingsOperationResult> SaveConfigurationChangesAsync()
    {
        return ApplyConfigurationAsync();
    }

    public void DiscardBudgetConfigurationChanges()
    {
        BudgetTab.RevertChanges();
        OnPropertyChanged(nameof(HasPendingBudgetConfigurationChanges));
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
    }

    public void RevertConfigurationChanges()
    {
        BudgetTab.RevertChanges();
        PersonalizationTab.RevertChanges();
        OnPropertyChanged(nameof(HasPendingBudgetConfigurationChanges));
        OnPropertyChanged(nameof(HasPendingPersonalizationConfigurationChanges));
        OnPropertyChanged(nameof(HasPendingConfigurationChanges));
    }

    public void ClearSelections(SettingsBatchTarget target)
    {
        switch (target)
        {
            case SettingsBatchTarget.Accounts:
                AccountsTab.ClearSelections();
                break;

            case SettingsBatchTarget.RecurringTransactions:
                RecurringTransactionsTab.ClearSelections();
                break;

            case SettingsBatchTarget.Goals:
                GoalsTab.ClearSelections();
                break;
        }
    }

    public void SetSelections(SettingsBatchTarget target, bool isChecked)
    {
        switch (target)
        {
            case SettingsBatchTarget.Accounts:
                AccountsTab.SetSelections(isChecked);
                break;

            case SettingsBatchTarget.RecurringTransactions:
                RecurringTransactionsTab.SetSelections(isChecked);
                break;

            case SettingsBatchTarget.Goals:
                GoalsTab.SetSelections(isChecked);
                break;
        }
    }

    public bool ShouldWarnBeforeApplyingToAll(SettingsBatchTarget target, SettingsBatchAction action)
    {
        return target switch
        {
            SettingsBatchTarget.Accounts => AccountsTab.ShouldWarnBeforeApplyingToAll(action),
            SettingsBatchTarget.RecurringTransactions => RecurringTransactionsTab.ShouldWarnBeforeApplyingToAll(action),
            SettingsBatchTarget.Goals => GoalsTab.ShouldWarnBeforeApplyingToAll(action),
            _ => false
        };
    }

    public Task<SettingsOperationResult> ExecuteAccountActionAsync(SettingsBatchAction action,
        IReadOnlyCollection<int>? selectedIdsOverride = null)
    {
        return AccountsTab.ExecuteActionAsync(action, selectedIdsOverride);
    }

    public Task<SettingsOperationResult> ExecuteRecurringTransactionActionAsync(SettingsBatchAction action,
        IReadOnlyCollection<int>? selectedIdsOverride = null)
    {
        return RecurringTransactionsTab.ExecuteActionAsync(action, selectedIdsOverride);
    }

    public Task<SettingsOperationResult> ExecuteGoalActionAsync(SettingsBatchAction action,
        IReadOnlyCollection<int>? selectedIdsOverride = null)
    {
        return GoalsTab.ExecuteActionAsync(action, selectedIdsOverride);
    }

    public Task<SettingsOperationResult> ExecuteAccountItemActionAsync(int itemId, SettingsBatchAction action)
    {
        return AccountsTab.ExecuteItemActionAsync(itemId, action);
    }

    public Task<SettingsOperationResult> ExecuteRecurringTransactionItemActionAsync(int itemId, SettingsBatchAction action)
    {
        return RecurringTransactionsTab.ExecuteItemActionAsync(itemId, action);
    }

    public Task<SettingsOperationResult> ExecuteGoalItemActionAsync(int itemId, SettingsBatchAction action)
    {
        return GoalsTab.ExecuteItemActionAsync(itemId, action);
    }

    public Task<SettingsOperationResult> CreateTagAsync(string name, string hexCode, string spendingLimitText)
    {
        return TagsTab.CreateTagAsync(name, hexCode, spendingLimitText);
    }

    public Task<SettingsOperationResult> CreateTagAsync(string name, string hexCode)
    {
        return TagsTab.CreateTagAsync(name, hexCode);
    }

    public Task<SettingsOperationResult> DeleteTagAsync(TagVM tag)
    {
        return TagsTab.DeleteTagAsync(tag);
    }

    public async Task<SettingsOperationResult> ResetAllSettingsAsync()
    {
        try
        {
            var settings = await _appData.GetUserSettingsAsync();
            var actions = await ApplySettingsResetPolicyAsync(settings, trackActions: true);
            await ResetBudgetAllocationToDefaultsAsync(_appData);
            _startupRegistrationService.SetRunAtStartup(false);

            await _appData.SaveChangesAsync();
            await _mainViewModel.ReloadUserSettingsAsync();
            Messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.All));
            await _mainViewModel.ReloadCurrentDataAsync();
            await LoadAsync();

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to reset settings.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("reset settings"));
        }
    }

    public async Task<SettingsOperationResult> DeleteAllDataAsync(bool keepSettings)
    {
        try
        {
            var transactions = await _appData.GetTransactionsAsync();
            var savingGoals = await _appData.GetSavingGoalsAsync();
            var accounts = await _appData.GetAccountsAsync();
            var tags = await _appData.GetTagsAsync();
            var recurringTransactions = await _appData.GetRecurringTransactionsAsync();
            var settings = keepSettings ? [] : await _appData.GetUserSettingsAsync();

            ApplyDeleteAllDataRemovalPolicy(
                _appData,
                tags,
                accounts,
                transactions,
                savingGoals,
                recurringTransactions);

            if (!keepSettings)
            {
                await ApplySettingsResetPolicyAsync(settings, trackActions: false);
                await ApplyDeleteAllDataBudgetAllocationPolicyAsync(_appData, keepSettings);
                _startupRegistrationService.SetRunAtStartup(false);
            }

            await EnsureDeleteAllDataSystemTagsAsync(_appData, tags);
            await _appData.SaveChangesAsync();
            await _mainViewModel.ReloadUserSettingsAsync();
            Messenger.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.All));
            await _mainViewModel.ReloadCurrentDataAsync();
            await LoadAsync();
            await _uiSettleAwaiter.WaitForUiReadyAsync();

            return SettingsOperationResult.Success();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to delete all data from settings.");
            return SettingsOperationResult.Failure(
                FluxoLogManager.CreateFailureMessage("delete all data"));
        }
    }

    public AddAccountVM CreateAddAccountViewModel()
    {
        return AccountsTab.CreateAddAccountViewModel();
    }

    public TransactionPopupVM CreateAddRecurringTransactionViewModel()
    {
        return RecurringTransactionsTab.CreateAddRecurringTransactionViewModel();
    }

    public AddSavingGoalVM CreateAddSavingGoalViewModel()
    {
        return GoalsTab.CreateAddSavingGoalViewModel();
    }

    public AccountDetailVM CreateAccountDetailViewModel(int accountId)
    {
        return AccountsTab.CreateAccountDetailViewModel(accountId);
    }

    public void SelectSingleItem(SettingsBatchTarget target, int itemId)
    {
        switch (target)
        {
            case SettingsBatchTarget.Accounts:
                AccountsTab.SelectSingleItem(itemId);
                break;

            case SettingsBatchTarget.RecurringTransactions:
                RecurringTransactionsTab.SelectSingleItem(itemId);
                break;

            case SettingsBatchTarget.Goals:
                GoalsTab.SelectSingleItem(itemId);
                break;
        }
    }

    public Task RefreshAccountsAsync()
    {
        return AccountsTab.RefreshAccountsAsync();
    }

    public Task RefreshRecurringTransactionsAsync()
    {
        return RecurringTransactionsTab.RefreshRecurringTransactionsAsync();
    }

    public Task RefreshSavingGoalsAsync()
    {
        return GoalsTab.RefreshSavingGoalsAsync();
    }

    public void IncrementAllocation(BudgetAllocationSegment segment, int delta)
    {
        BudgetTab.IncrementAllocation(segment, delta);
    }

    public void SetAllocation(BudgetAllocationSegment segment, double value)
    {
        BudgetTab.SetAllocation(segment, value);
    }

    public bool IsAccountChecksEnabled
    {
        get => AccountsTab.IsAccountChecksEnabled;
        set
        {
            AccountsTab.IsAccountChecksEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsRecurringTransactionChecksEnabled
    {
        get => RecurringTransactionsTab.IsRecurringTransactionChecksEnabled;
        set
        {
            RecurringTransactionsTab.IsRecurringTransactionChecksEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsGoalChecksEnabled
    {
        get => GoalsTab.IsGoalChecksEnabled;
        set
        {
            GoalsTab.IsGoalChecksEnabled = value;
            OnPropertyChanged();
        }
    }

    public void RefreshDashboardSpendingAmountGateState()
    {
        ApplyDashboardSpendingAmountGateState();
    }

    private async Task HandleMaintenanceRequestAsync(SettingsMaintenanceRequest request)
    {
        try
        {
            var result = request.RequestType switch
            {
                SettingsMaintenanceRequestType.ResetAllSettings => await ResetAllSettingsAsync(),
                SettingsMaintenanceRequestType.DeleteAllData => await DeleteAllDataAsync(request.KeepSettings),
                _ => SettingsOperationResult.Failure("Unsupported settings action.")
            };

            request.CompletionSource.TrySetResult(result.IsSuccess
                ? SettingsMaintenanceResult.Success()
                : SettingsMaintenanceResult.Failure(result.ErrorMessage));
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to complete deferred settings maintenance action.");
            request.CompletionSource.TrySetResult(SettingsMaintenanceResult.Failure(
                FluxoLogManager.CreateFailureMessage("complete settings action")));
        }
    }

    internal static bool ShouldDeleteTagOnDeleteAllData(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        return !tag.IsSystemTag;
    }

    internal static async Task EnsureDeleteAllDataSystemTagsAsync(
        IAppDataService appData,
        IReadOnlyList<Tag> existingTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appData);
        ArgumentNullException.ThrowIfNull(existingTags);

        foreach (var requiredTag in RequiredDeleteAllDataSystemTags)
        {
            var existingSystemTag = existingTags.FirstOrDefault(existingTag =>
                existingTag.IsSystemTag &&
                string.Equals(existingTag.Name, requiredTag.Name, StringComparison.OrdinalIgnoreCase));

            if (existingSystemTag is not null)
            {
                if (!string.Equals(existingSystemTag.HexCode, requiredTag.HexCode, StringComparison.Ordinal))
                {
                    existingSystemTag.Name = requiredTag.Name;
                    existingSystemTag.HexCode = requiredTag.HexCode;
                    existingSystemTag.IsSystemTag = true;
                    appData.UpdateTag(existingSystemTag);
                }

                continue;
            }

            await appData.AddTagAsync(new Tag
            {
                Name = requiredTag.Name,
                HexCode = requiredTag.HexCode,
                IsSystemTag = true
            }, cancellationToken);
        }
    }

    internal static void ApplyDeleteAllDataRemovalPolicy(
        IAppDataService appData,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<Account> accounts,
        IReadOnlyList<Transaction> transactions,
        IReadOnlyList<SavingGoal> savingGoals,
        IReadOnlyList<RecurringTransaction> recurringTransactions)
    {
        ArgumentNullException.ThrowIfNull(appData);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(transactions);
        ArgumentNullException.ThrowIfNull(savingGoals);
        ArgumentNullException.ThrowIfNull(recurringTransactions);

        foreach (var recurringTransaction in recurringTransactions)
            appData.RemoveRecurringTransaction(recurringTransaction);

        foreach (var transaction in transactions)
            appData.RemoveTransaction(transaction);

        foreach (var source in accounts)
            appData.RemoveAccount(source);

        foreach (var goal in savingGoals)
            appData.RemoveSavingGoal(goal);

        foreach (var tag in tags)
            if (ShouldDeleteTagOnDeleteAllData(tag))
                appData.RemoveTag(tag);

    }

    internal static (HashSet<string> RemovedSettingNames, Dictionary<string, string> UpsertSettingValues)
        BuildSettingsResetPlan(IReadOnlyList<UserSettings> existingSettings)
    {
        ArgumentNullException.ThrowIfNull(existingSettings);

        var removedSettingNames = existingSettings
            .Where(setting => !SettingsDefaultsAfterDeletion.ContainsKey(setting.Name))
            .Select(setting => setting.Name)
            .ToHashSet(StringComparer.Ordinal);

        var upsertSettingValues = SettingsDefaultsAfterDeletion
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);

        return (removedSettingNames, upsertSettingValues);
    }

    internal static async Task ApplyDeleteAllDataBudgetAllocationPolicyAsync(
        IAppDataService appData,
        bool keepSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appData);

        if (keepSettings)
            return;

        await ResetBudgetAllocationToDefaultsAsync(appData, cancellationToken);
    }

    internal static async Task ResetBudgetAllocationToDefaultsAsync(
        IAppDataService appData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appData);

        var allocation = await appData.GetBudgetAllocationAsync(cancellationToken);
        var defaults = new BudgetAllocation();

        allocation.NeedsThreshold = defaults.NeedsThreshold;
        allocation.WantsThreshold = defaults.WantsThreshold;
        allocation.InvestThreshold = defaults.InvestThreshold;
        allocation.AllocationPeriod = defaults.AllocationPeriod;
        allocation.AllocationLimit = defaults.AllocationLimit;
        allocation.NeedsDebt = defaults.NeedsDebt;
        allocation.WantsDebt = defaults.WantsDebt;
        allocation.InvestDebt = defaults.InvestDebt;
        allocation.RolloverPolicy = defaults.RolloverPolicy;
        allocation.OverspendPolicy = defaults.OverspendPolicy;

        appData.UpdateBudgetAllocation(allocation);
    }

    private async Task<List<ILogMemoryAction>> ApplySettingsResetPolicyAsync(
        IReadOnlyList<UserSettings> existingSettings,
        bool trackActions)
    {
        var actions = new List<ILogMemoryAction>();
        var (removedSettingNames, upsertSettingValues) = BuildSettingsResetPlan(existingSettings);

        foreach (var setting in existingSettings)
        {
            if (removedSettingNames.Contains(setting.Name))
            {
                _appData.RemoveUserSetting(setting);
                if (trackActions)
                {
                    actions.Add(new SetUserSettingMemoryAction(
                        UserSettingMemorySnapshot.Create(setting),
                        UserSettingMemorySnapshot.Missing(setting.Name)));
                }

                continue;
            }

            if (!upsertSettingValues.TryGetValue(setting.Name, out var desiredValue))
                continue;

            upsertSettingValues.Remove(setting.Name);
            if (string.Equals(setting.Value, desiredValue, StringComparison.Ordinal))
                continue;

            var beforeSnapshot = UserSettingMemorySnapshot.Create(setting);
            setting.Value = desiredValue;
            _appData.UpdateUserSetting(setting);

            if (trackActions)
            {
                actions.Add(new SetUserSettingMemoryAction(
                    beforeSnapshot,
                    new UserSettingMemorySnapshot(setting.Name, desiredValue, true)));
            }
        }

        foreach (var (name, value) in upsertSettingValues)
        {
            await _appData.AddUserSettingAsync(new UserSettings { Name = name, Value = value });

            if (trackActions)
            {
                actions.Add(new SetUserSettingMemoryAction(
                    UserSettingMemorySnapshot.Missing(name),
                    new UserSettingMemorySnapshot(name, value, true)));
            }
        }

        return actions;
    }

    private void ApplyDashboardSpendingAmountGateState()
    {
        var isLocked = IsSufficientFundsActionGateLocked;
        RecurringTransactionsTab.IsDashboardSpendingAmountGateLocked = isLocked;
        GoalsTab.IsDashboardSpendingAmountGateLocked = isLocked;
        OnPropertyChanged(nameof(IsDashboardSpendingAmountGateLocked));
        OnPropertyChanged(nameof(IsSufficientFundsActionGateLocked));
    }
}
