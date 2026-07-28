using System.Collections.ObjectModel;
using Fluxo.Helpers.Settings;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.Helpers.Popups;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardAccountsVM : ObservableObject
{
    private readonly MainVM _mainViewModel;
    private readonly IAppDataService _appData;
    private readonly IMessenger _messenger;
    private readonly Dictionary<int, QuickSetupWizardDraftAccount> _draftSources = [];
    private readonly HashSet<int> _removedPersistedIds = [];
    private IReadOnlyDictionary<int, int> _lastPersistedIdMap = new Dictionary<int, int>();
    private int _nextTemporaryId = -1;
    private bool _isLoaded;

    [ObservableProperty] private bool _isStep2Active;

    public QuickSetupWizardAccountsVM(
        MainVM mainViewModel,
        IAppDataService appData,
        IMessenger? messenger = null)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public ObservableCollection<QuickSetupWizardAccountItemVM> Accounts { get; } = [];

    public bool HasAccounts => Accounts.Count > 0;

    public decimal TotalBudgetAmount => Accounts.Sum(source => source.PrimaryAmount);

    public IReadOnlyDictionary<int, int> LastPersistedIdMap => _lastPersistedIdMap;

    internal int GetNextTemporaryId()
    {
        return _nextTemporaryId--;
    }

    public string ResolveSourceName(int sourceId)
    {
        return _draftSources.TryGetValue(sourceId, out var source)
            ? source.Name
            : "No source";
    }

    public IReadOnlyList<AccountVM> BuildAccountOptions()
    {
        return _draftSources.Values
            .Where(source => source.IsEnabled)
            .OrderBy(source => source.Name)
            .Select(source => new AccountVM
            {
                Id = source.Id,
                Name = source.Name,
                AccountType = source.AccountType,
                Balance = source.Balance,
                SpentAmount = source.SpentAmount,
                AccountLimit = source.AccountLimit,
                MaximumSpending = source.MaximumSpending,
                MinimumPayment = source.MinimumPayment,
                MonthlyDueDate = source.MonthlyDueDate,
                DeductSource = source.DeductSource,
                InterestRate = source.InterestRate,
                PinnedOnUI = source.PinnedOnUi,
                IsEnabled = source.IsEnabled,
                IsDefault = source.IsDefault
            })
            .ToList();
    }

    public AddAccountVM CreateAddViewModel()
    {
        return new AddAccountVM(
            _mainViewModel,
            _appData,
            saveDraftAsync: input => SaveDraftSourceAsync(input, null),
            loadDraftDeductSourcesAsync: editingId =>
                Task.FromResult<IReadOnlyList<AddAccountDeductSourceOption>>(
                    BuildDraftDeductSourceOptions(editingId)));
    }

    public async Task<AddAccountVM> CreateEditViewModelAsync(int id)
    {
        if (!_isLoaded)
            await LoadDraftSourcesAsync();

        if (!_draftSources.TryGetValue(id, out var source))
            return CreateAddViewModel();

        var vm = new AddAccountVM(
            _mainViewModel,
            _appData,
            saveDraftAsync: input => SaveDraftSourceAsync(input, source.Id),
            loadDraftDeductSourcesAsync: editingId =>
                Task.FromResult<IReadOnlyList<AddAccountDeductSourceOption>>(
                    BuildDraftDeductSourceOptions(editingId)))
        {
            EditingId = source.Id
        };
        vm.NameText = source.Name;
        vm.SelectedAccountType = source.AccountType;
        vm.PinnedOnUI = source.PinnedOnUi;
        vm.IsEnabled = source.IsEnabled;
        vm.IsDefault = source.IsDefault;

        if (source.AccountType == AccountType.Credit)
        {
            vm.PrimaryAmountText = source.SpentAmount;
            vm.SpentAmountText = source.SpentAmount;
            vm.AccountLimitText = source.AccountLimit;
            vm.MaximumSpendingText = source.MaximumSpending;
            vm.MinimumPaymentText = source.MinimumPayment ?? 0m;
            vm.MonthlyDueDateText = MonthlyDueDateHelper.Normalize(source.MonthlyDueDate)?.ToString(CultureInfo.InvariantCulture) ??
                                    string.Empty;
            vm.SelectedDeductSource = source.DeductSource;
        }
        else
        {
            vm.PrimaryAmountText = source.Balance;
        }

        if (source.AccountType != AccountType.Credit)
            vm.MaximumSpendingText = source.MaximumSpending;

        if (source.AccountType == AccountType.Saving && source.InterestRate.HasValue)
            vm.ApyText = source.InterestRate.Value;

        return vm;
    }

    public Task DeleteAsync(int id)
    {
        if (id > 0)
            _removedPersistedIds.Add(id);

        _draftSources.Remove(id);

        foreach (var draft in _draftSources.Values.Where(source => source.DeductSource == id).ToList())
        {
            _draftSources[draft.Id] = draft with { DeductSource = null };
        }

        RefreshProjectionAndPublish();
        return Task.CompletedTask;
    }

    public string BuildDeleteConfirmationMessage(int id)
    {
        var sourceName = ResolveSourceName(id);
        var functioningSourceIds = _draftSources.Values
            .Where(source => AccountDeletionConfirmationHelper.IsFunctioning(
                source.AccountType,
                source.IsEnabled,
                source.Balance,
                source.AccountLimit))
            .Select(source => source.Id)
            .ToList();

        var isOnlyFunctioningSource = functioningSourceIds.Count == 1 && functioningSourceIds[0] == id;
        return AccountDeletionConfirmationHelper.BuildDeleteConfirmationMessage(sourceName, isOnlyFunctioningSource);
    }

    public async Task RefreshAsync()
    {
        if (!_isLoaded)
            await LoadDraftSourcesAsync();

        RefreshProjectionAndPublish();
    }

    public async Task ApplyAsync(IAppDataService appData)
    {
        var existingSources = (await appData.GetAccountsAsync())
            .ToDictionary(source => source.Id);
        var idMap = new Dictionary<int, int>();

        foreach (var existingDefault in existingSources.Values.Where(source =>
                     source.IsDefault &&
                     (!_draftSources.TryGetValue(source.Id, out var draft) || !draft.IsDefault)))
        {
            existingDefault.IsDefault = false;
            appData.UpdateAccount(existingDefault);
        }

        await appData.SaveChangesAsync();

        foreach (var draft in _draftSources.Values.OrderBy(source => source.Id))
        {
            if (draft.Id > 0 && existingSources.TryGetValue(draft.Id, out var persisted))
            {
                var previousIsEnabled = persisted.IsEnabled;
                persisted.Name = draft.Name;
                persisted.AccountType = draft.AccountType;
                persisted.Balance = draft.Balance;
                persisted.SpentAmount = draft.SpentAmount;
                persisted.AccountLimit = draft.AccountLimit;
                persisted.MaximumSpending = draft.MaximumSpending;
                persisted.MinimumPayment = draft.MinimumPayment;
                persisted.MonthlyDueDate = draft.MonthlyDueDate;
                persisted.DeductSource = null;
                persisted.InterestRate = draft.InterestRate;
                persisted.IsEnabled = draft.IsEnabled;
                persisted.IsDefault = draft.IsDefault;
                persisted.PinnedOnUI = ResolvePinnedOnUiFromEnabledState(previousIsEnabled, draft.IsEnabled, draft.PinnedOnUi);
                appData.UpdateAccount(persisted);
                idMap[draft.Id] = persisted.Id;
            }
            else
            {
                var created = new Account
                {
                    Name = draft.Name,
                    AccountType = draft.AccountType,
                    Balance = draft.Balance,
                    SpentAmount = draft.SpentAmount,
                    AccountLimit = draft.AccountLimit,
                    MaximumSpending = draft.MaximumSpending,
                    MinimumPayment = draft.MinimumPayment,
                    MonthlyDueDate = draft.MonthlyDueDate,
                    DeductSource = null,
                    InterestRate = draft.InterestRate,
                    PinnedOnUI = draft.PinnedOnUi,
                    IsEnabled = draft.IsEnabled,
                    IsDefault = draft.IsDefault
                };
                await appData.AddAccountAsync(created);
                await appData.SaveChangesAsync();
                idMap[draft.Id] = created.Id;
            }
        }

        foreach (var draft in _draftSources.Values)
        {
            if (!idMap.TryGetValue(draft.Id, out var persistedId))
                continue;

            var persisted = await appData.GetAccountByIdAsync(persistedId);
            if (persisted is null)
                continue;

            int? mappedDeductSource = null;
            if (draft.DeductSource.HasValue && idMap.TryGetValue(draft.DeductSource.Value, out var mapped))
                mappedDeductSource = mapped;

            persisted.DeductSource = mappedDeductSource;
            appData.UpdateAccount(persisted);
        }

        var allTransactions = await appData.GetTransactionsAsync();

        foreach (var removedId in _removedPersistedIds)
        {
            if (existingSources.TryGetValue(removedId, out var existing))
            {
                foreach (var transaction in allTransactions.Where(item => item.SourceAccountId == removedId))
                    appData.RemoveTransaction(transaction);

                appData.RemoveAccount(existing);
            }
        }

        _lastPersistedIdMap = new Dictionary<int, int>(idMap);
    }

    private void PublishSnapshot()
    {
        _messenger.Send(new QuickSetupWizardAccountsChangedMessage(
            new QuickSetupWizardAccountsChanged(
                Accounts.Count,
                HasAccounts,
                TotalBudgetAmount)));
    }

    private async Task LoadDraftSourcesAsync()
    {
        var persistedSources = await _appData.GetAccountsAsync();
        _draftSources.Clear();

        foreach (var source in persistedSources)
        {
            _draftSources[source.Id] = new QuickSetupWizardDraftAccount(
                source.Id,
                source.Name,
                source.AccountType,
                source.Balance,
                source.SpentAmount,
                source.AccountLimit,
                source.MaximumSpending,
                source.MinimumPayment,
                source.MonthlyDueDate,
                source.DeductSource,
                source.InterestRate,
                source.PinnedOnUI,
                source.IsEnabled,
                source.IsDefault);
        }

        _removedPersistedIds.Clear();
        _lastPersistedIdMap = new Dictionary<int, int>();
        _nextTemporaryId = -1;
        _isLoaded = true;
    }

    private Task<AddAccountResult> SaveDraftSourceAsync(
        AddAccountInput input,
        int? editingId)
    {
        if (_draftSources.Values.Any(source =>
                source.Id != (editingId ?? int.MinValue) &&
                string.Equals(source.Name, input.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(AddAccountResult.Failure(
                $"A account named \"{input.Name}\" already exists."));
        }

        var id = editingId ?? GetNextTemporaryId();
        var previousIsEnabled = _draftSources.TryGetValue(id, out var existingDraft)
            ? existingDraft.IsEnabled
            : input.IsEnabled;
        var resolvedPinnedOnUi = ResolvePinnedOnUiFromEnabledState(previousIsEnabled, input.IsEnabled, input.PinnedOnUI);
        _draftSources[id] = new QuickSetupWizardDraftAccount(
            id,
            input.Name,
            input.AccountType,
            input.Balance,
            input.SpentAmount,
            input.AccountLimit,
            input.MaximumSpending,
            input.MinimumPayment,
            input.MonthlyDueDate,
            input.DeductSource,
            input.InterestRate,
            resolvedPinnedOnUi,
            input.IsEnabled,
            input.IsDefault);

        if (input.IsDefault)
        {
            foreach (var key in _draftSources.Keys.Where(key => key != id).ToList())
                _draftSources[key] = _draftSources[key] with { IsDefault = false };
        }

        if (id > 0)
            _removedPersistedIds.Remove(id);

        RefreshProjectionAndPublish();
        return Task.FromResult(AddAccountResult.Success(true));
    }

    private IReadOnlyList<AddAccountDeductSourceOption> BuildDraftDeductSourceOptions(int? editingId)
    {
        return _draftSources.Values
            .Where(source => source.Id != (editingId ?? 0))
            .Where(source => source.AccountType != AccountType.Credit)
            .OrderBy(source => source.Name)
            .Select(source => new AddAccountDeductSourceOption(source.Id, source.Name))
            .ToList();
    }

    private void RefreshProjectionAndPublish()
    {
        QuickSetupWizardShared.ReplaceCollection(
            Accounts,
            _draftSources.Values
                .OrderBy(source => source.Name)
                .Select(source => new QuickSetupWizardAccountItemVM(source)));

        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(TotalBudgetAmount));
        PublishSnapshot();
    }

    private static bool ResolvePinnedOnUiFromEnabledState(bool previousIsEnabled, bool nextIsEnabled, bool requestedPinnedOnUi)
    {
        if (!nextIsEnabled)
            return false;

        if (previousIsEnabled == nextIsEnabled)
            return requestedPinnedOnUi;

        return true;
    }
}
