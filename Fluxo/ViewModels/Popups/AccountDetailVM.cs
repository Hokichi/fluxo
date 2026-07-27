using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Helpers.Popups;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Popups;

public partial class AccountDetailVM : ObservableObject
{
    private readonly IAppDataService _appData;

    [ObservableProperty] private decimal _accountLimitText;
    [ObservableProperty] private decimal _apyText;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private decimal _maximumSpendingText;
    [ObservableProperty] private decimal _minimumPaymentText;
    [ObservableProperty] private string _monthlyDueDateText = string.Empty;
    [ObservableProperty] private decimal _moneyIn;
    [ObservableProperty] private decimal _moneyOut;
    [ObservableProperty] private string _nameText = string.Empty;
    [ObservableProperty] private decimal _primaryAmountText;
    [ObservableProperty] private int? _selectedDeductSource;
    private AccountDetailState _savedState = AccountDetailState.Empty;
    [ObservableProperty] private bool _pinnedOnUI = true;
    [ObservableProperty] private AccountType _accountType;
    [ObservableProperty] private decimal _spentAmountText;
    [ObservableProperty] private decimal _trendMaximum = 1m;

    public AccountDetailVM(MainVM mainViewModel, int accountId, IAppDataService appData)
    {
        MainViewModel = mainViewModel;
        AccountId = accountId;
        _appData = appData;
        DeductSourcesView = AccountComboBoxViewFactory.CreateGroupedByTypeThenName(
            DeductSources,
            nameof(DeductSourceOption.TypeDisplayName),
            nameof(DeductSourceOption.AccountType),
            nameof(DeductSourceOption.Name));
    }

    public ObservableCollection<AccountActivityItemVM> RecentActivities { get; } = [];
    public ObservableCollection<AccountTrendItemVM> Trends { get; } = [];
    public ObservableCollection<DeductSourceOption> DeductSources { get; } = [];
    public ICollectionView DeductSourcesView { get; }

    public MainVM MainViewModel { get; }

    public IAppDataService AppData => _appData;

    public int AccountId { get; }

    public string PopupTitle => "Income Detail";

    public bool IsCashOrChecking => AccountType is AccountType.Cash or AccountType.Checking;

    public bool IsCredit => AccountType == AccountType.Credit;

    public bool IsSaving => AccountType == AccountType.Saving;

    public bool CanTransfer => IsEnabled &&
                               AccountType != AccountType.Credit &&
                               !IsEditing &&
                               DeductSources.Count > 0;

    public bool CanDelete => !IsEditing;

    public bool CanPinOrUnpin => IsEnabled && !IsEditing;

    public string EditButtonLabel => IsEditing ? "Save" : "Edit";

    public bool IsUnpinned => !PinnedOnUI;

    public bool HasRecentActivities => RecentActivities.Count > 0;

    public decimal DisplayPrimaryAmount => AccountType == AccountType.Credit
        ? SpentAmountText
        : PrimaryAmountText;

    public string PrimaryAmountLabel => AccountType == AccountType.Credit
        ? "Spent"
        : "Balance";

    public string MonthlyDueDateDisplay => TryParseMonthlyDueDate(MonthlyDueDateText, out var dueDay)
        ? $"Day {dueDay}"
        : "Not set";

    public string DeductSourceDisplay =>
        DeductSources.FirstOrDefault(option => option.Id == SelectedDeductSource).Name ?? "Not set";

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(EditButtonLabel));
        OnPropertyChanged(nameof(CanPinOrUnpin));
    }

    partial void OnPinnedOnUIChanged(bool value)
    {
        OnPropertyChanged(nameof(IsUnpinned));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(CanPinOrUnpin));
    }

    partial void OnAccountTypeChanged(AccountType value)
    {
        OnPropertyChanged(nameof(IsCashOrChecking));
        OnPropertyChanged(nameof(IsCredit));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(CanTransfer));
        OnPropertyChanged(nameof(DisplayPrimaryAmount));
        OnPropertyChanged(nameof(PrimaryAmountLabel));
        OnPropertyChanged(nameof(DeductSourceDisplay));

        if (value != AccountType.Credit)
        {
            MonthlyDueDateText = string.Empty;
            SelectedDeductSource = null;
        }

        if (value != AccountType.Credit)
            MinimumPaymentText = 0m;
    }

    partial void OnPrimaryAmountTextChanged(decimal value)
    {
        OnPropertyChanged(nameof(DisplayPrimaryAmount));
    }

    partial void OnSpentAmountTextChanged(decimal value)
    {
        OnPropertyChanged(nameof(DisplayPrimaryAmount));
    }

    partial void OnMonthlyDueDateTextChanged(string value)
    {
        OnPropertyChanged(nameof(MonthlyDueDateDisplay));
    }

    partial void OnSelectedDeductSourceChanged(int? value)
    {
        OnPropertyChanged(nameof(DeductSourceDisplay));
    }

    public async Task<bool> LoadAsync()
    {
        return await RefreshAsync(true);
    }

    public void BeginEditing()
    {
        IsEditing = true;
    }

    public void CancelEditing()
    {
        IsEditing = false;
        LoadFromState(_savedState);
    }

    public async Task<AccountDetailResult> SaveAsync()
    {
        if (!TryBuildInput(out var input, out var validationMessage))
            return AccountDetailResult.Failure(validationMessage);

        if (input == _savedState)
        {
            IsEditing = false;
            LoadFromState(_savedState);
            return AccountDetailResult.Success();
        }

        if (IsBusy)
            return AccountDetailResult.Failure("This account is already being updated.");

        IsBusy = true;

        try
        {
            var account = await _appData.GetAccountByIdAsync(AccountId);
            if (account is null)
                return AccountDetailResult.Failure("Unable to load this account.");

            var beforeSnapshot = AccountMemorySnapshot.Create(account);

            ApplyInput(account, input);

            _appData.UpdateAccount(account);
            await _appData.SaveChangesAsync();

            var afterSnapshot = AccountMemorySnapshot.Create(account);
            WeakReferenceMessenger.Default.Send(
                new RecordLogMemoryMessage(new EditAccountMemoryAction(beforeSnapshot, afterSnapshot)));
            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));

            await MainViewModel.ReloadCurrentDataAsync();
            await RefreshAsync(true);
            IsEditing = false;

            FloatingNotificationPublisher.Success(
                input.Name, "Account details were saved.", true, "Updated");
            return AccountDetailResult.Success();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception, "save account");
            return AccountDetailResult.Failure(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<AddAccountVM?> CreateEditAccountViewModelAsync()
    {
        var account = await _appData.GetAccountByIdAsync(AccountId);
        if (account is null)
            return null;

        var viewModel = new AddAccountVM(MainViewModel, _appData);
        viewModel.InitializeFromAccount(account);
        return viewModel;
    }

    public async Task<bool> ShouldConfirmDisablingOnlyEnabledSourceAsync()
    {
        if (!IsEnabled)
            return false;

        var accounts = await _appData.GetAccountsAsync();
        return accounts.Count(source => source.IsEnabled) == 1 &&
               accounts.Any(source => source.Id == AccountId && source.IsEnabled);
    }

    public Task<string> BuildDeleteConfirmationMessageAsync(CancellationToken cancellationToken = default)
    {
        return AccountDeletionConfirmationHelper.BuildDeleteConfirmationMessageAsync(
            _appData,
            AccountId,
            NameText,
            cancellationToken);
    }

    public async Task<AccountDetailResult> ToggleVisibilityAsync()
    {
        if (IsEditing)
            return AccountDetailResult.Failure("Finish editing before hiding or unhiding this source.");

        if (!IsEnabled)
            return AccountDetailResult.Failure("Enable this source before hiding or unhiding it.");

        if (IsBusy)
            return AccountDetailResult.Failure("This account is already being updated.");

        IsBusy = true;

        try
        {
            var account = await _appData.GetAccountByIdAsync(AccountId);
            if (account is null)
                return AccountDetailResult.Failure("Unable to load this account.");

            var beforeSnapshot = AccountMemorySnapshot.Create(account);

            account.PinnedOnUI = !account.PinnedOnUI;
            _appData.UpdateAccount(account);
            await _appData.SaveChangesAsync();

            var afterSnapshot = AccountMemorySnapshot.Create(account);
            WeakReferenceMessenger.Default.Send(
                new RecordLogMemoryMessage(new EditAccountMemoryAction(beforeSnapshot, afterSnapshot)));
            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));

            await MainViewModel.ReloadCurrentDataAsync();
            await RefreshAsync(true);

            FloatingNotificationPublisher.Success(
                account.Name,
                account.PinnedOnUI ? "Account was added to the dashboard." : "Account was removed from the dashboard.",
                true,
                account.PinnedOnUI ? "Pinned" : "Unpinned");
            return AccountDetailResult.Success();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception, "update account");
            return AccountDetailResult.Failure(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<AccountDetailResult> ToggleEnabledAsync()
    {
        if (IsEditing)
            return AccountDetailResult.Failure("Finish editing before enabling or disabling this source.");

        if (IsBusy)
            return AccountDetailResult.Failure("This account is already being updated.");

        IsBusy = true;

        try
        {
            var account = await _appData.GetAccountByIdAsync(AccountId);
            if (account is null)
                return AccountDetailResult.Failure("Unable to load this account.");

            var beforeSnapshot = AccountMemorySnapshot.Create(account);

            account.IsEnabled = !account.IsEnabled;
            account.PinnedOnUI = account.IsEnabled;
            _appData.UpdateAccount(account);
            await _appData.SaveChangesAsync();

            var afterSnapshot = AccountMemorySnapshot.Create(account);
            WeakReferenceMessenger.Default.Send(
                new RecordLogMemoryMessage(new EditAccountMemoryAction(beforeSnapshot, afterSnapshot)));
            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));

            await MainViewModel.ReloadCurrentDataAsync();
            await RefreshAsync(true);

            FloatingNotificationPublisher.Success(
                account.Name,
                account.IsEnabled ? "Account is available for transactions." : "Account is unavailable for transactions.",
                true,
                account.IsEnabled ? "Enabled" : "Disabled");
            return AccountDetailResult.Success();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception, "adjust account");
            return AccountDetailResult.Failure(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<AccountDetailResult> DeleteAsync()
    {
        if (IsBusy)
            return AccountDetailResult.Failure("This account is already being updated.");

        IsBusy = true;

        try
        {
            var account = await _appData.GetAccountByIdAsync(AccountId);
            if (account is null)
                return AccountDetailResult.Failure("Unable to load this account.");

            var transactions = await _appData.GetTransactionsAsync();

            var snapshot = AccountMemorySnapshot.Create(account);

            foreach (var transaction in transactions.Where(item => item.SourceAccountId == AccountId))
                _appData.RemoveTransaction(transaction);

            _appData.RemoveAccount(account);
            await _appData.SaveChangesAsync();

            WeakReferenceMessenger.Default.Send(
                new RecordLogMemoryMessage(new DeleteAccountMemoryAction(snapshot)));
            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));

            await MainViewModel.ReloadCurrentDataAsync();

            FloatingNotificationPublisher.Success(
                account.Name, "Account was permanently removed.", true, "Deleted");
            return AccountDetailResult.Success(true);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception, "delete account");
            return AccountDetailResult.Failure(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool HasValidChangesToPersistOnClose()
    {
        return IsEditing && TryBuildInput(out var input, out _) && input != _savedState;
    }

    private async Task<bool> RefreshAsync(bool resetDraft)
    {
        var account = await _appData.GetAccountByIdAsync(AccountId);
        if (account is null)
            return false;

        await LoadDeductSourcesAsync();

        var transactions = (await _appData.GetTransactionsAsync())
            .Where(transaction => transaction.SourceAccountId == AccountId && !transaction.IsForDeletion)
            .ToList();
        var expenseLogs = transactions.Where(transaction => transaction.Type == TransactionType.Expense).ToList();
        var incomeLogs = transactions.Where(transaction => transaction.Type == TransactionType.Income).ToList();

        MoneyIn = incomeLogs.Sum(log => log.Amount);
        MoneyOut = expenseLogs.Sum(log => log.Amount);

        ReplaceCollection(RecentActivities, BuildActivities(expenseLogs, incomeLogs));
        OnPropertyChanged(nameof(HasRecentActivities));
        ReplaceCollection(Trends, BuildTrends(expenseLogs, incomeLogs));
        TrendMaximum = Math.Max(1m,
            Trends.SelectMany(item => new[] { item.IncomeAmount, item.ExpenseAmount }).DefaultIfEmpty(1m).Max());

        _savedState = CreateState(account);

        if (resetDraft || !IsEditing)
            LoadFromState(_savedState);

        return true;
    }

    private void LoadFromState(AccountDetailState state)
    {
        AccountType = state.AccountType;
        NameText = state.Name;
        PrimaryAmountText = state.PrimaryAmount;
        SpentAmountText = state.SpentAmount;
        AccountLimitText = state.AccountLimit;
        MaximumSpendingText = state.MaximumSpending;
        MinimumPaymentText = state.MinimumPayment ?? 0m;
        ApyText = state.InterestRate ?? 0m;
        MonthlyDueDateText = state.MonthlyDueDate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        SelectedDeductSource = state.DeductSource;
        IsEnabled = state.IsEnabled;
        PinnedOnUI = state.PinnedOnUI;
    }

    private bool TryBuildInput(out AccountDetailState input, out string validationMessage)
    {
        input = AccountDetailState.Empty;
        validationMessage = string.Empty;

        var name = NameText.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            validationMessage = "Please enter a account name.";
            return false;
        }

        var primaryAmount = PrimaryAmountText;
        var spentAmount = SpentAmountText;
        var accountLimit = AccountLimitText;
        var maximumSpending = MaximumSpendingText;
        var minimumPayment = AccountType == AccountType.Credit ? MinimumPaymentText : 0m;
        decimal? interestRate = IsSaving ? ApyText : null;

        if (primaryAmount < 0m || spentAmount < 0m || accountLimit < 0m ||
            maximumSpending < 0m || minimumPayment < 0m ||
            (interestRate.HasValue && interestRate.Value < 0m))
        {
            validationMessage = "Values must be zero or greater.";
            return false;
        }

        int? monthlyDueDate = null;
        int? deductSource = null;
        if (AccountType == AccountType.Credit)
        {
            if (string.IsNullOrWhiteSpace(MonthlyDueDateText))
            {
                validationMessage = "Credit accounts require a due date.";
                return false;
            }

            if (!TryParseMonthlyDueDate(MonthlyDueDateText, out var parsedMonthlyDueDate))
            {
                validationMessage = "Due date must be a number between 1 and 28.";
                return false;
            }

            monthlyDueDate = parsedMonthlyDueDate;

            if (!SelectedDeductSource.HasValue || DeductSources.All(option => option.Id != SelectedDeductSource.Value))
            {
                validationMessage = "Credit accounts require a deduct account.";
                return false;
            }

            deductSource = SelectedDeductSource;
        }

        input = new AccountDetailState(
            name,
            AccountType,
            primaryAmount,
            accountLimit,
            maximumSpending,
            AccountType == AccountType.Credit ? minimumPayment : null,
            spentAmount,
            monthlyDueDate,
            deductSource,
            interestRate,
            IsEnabled,
            PinnedOnUI);

        return true;
    }

    private static void ApplyInput(Account account, AccountDetailState input)
    {
        var previousIsEnabled = account.IsEnabled;
        account.Name = input.Name;
        account.AccountLimit = input.AccountLimit;
        account.MaximumSpending = input.MaximumSpending;
        account.MinimumPayment = input.MinimumPayment;
        account.SpentAmount = input.SpentAmount;
        account.MonthlyDueDate = input.MonthlyDueDate;
        account.DeductSource = input.DeductSource;
        account.InterestRate = input.InterestRate;
        account.IsEnabled = input.IsEnabled;
        account.PinnedOnUI = ResolvePinnedOnUiFromEnabledState(previousIsEnabled, input.IsEnabled, input.PinnedOnUI);

        if (input.AccountType == AccountType.Credit)
        {
            account.SpentAmount = input.SpentAmount;
            return;
        }

        account.MonthlyDueDate = null;
        account.DeductSource = null;
        account.AccountLimit = 0m;
        account.MinimumPayment = null;
        account.Balance = input.PrimaryAmount;
    }

    private static AccountDetailState CreateState(Account account)
    {
        return new AccountDetailState(
            account.Name,
            account.AccountType,
            account.AccountType == AccountType.Credit
                ? account.SpentAmount
                : account.Balance,
            account.AccountLimit,
            account.MaximumSpending,
            account.MinimumPayment,
            account.SpentAmount,
            MonthlyDueDateHelper.Normalize(account.MonthlyDueDate),
            account.DeductSource,
            account.InterestRate,
            account.IsEnabled,
            account.PinnedOnUI);
    }

    private async Task LoadDeductSourcesAsync()
    {
        var options = (await _appData.GetAccountsAsync())
            .Where(source => source.Id != AccountId)
            .Where(source => source.IsEnabled)
            .Where(source => source.AccountType != AccountType.Credit)
            .OrderBy(source => source.AccountType)
            .ThenBy(source => source.Name)
            .Select(source => new DeductSourceOption(source.Id, source.Name, source.AccountType))
            .ToList();

        DeductSources.Clear();
        foreach (var option in options)
            DeductSources.Add(option);

        OnPropertyChanged(nameof(CanTransfer));

        if (!IsCredit)
        {
            SelectedDeductSource = null;
            return;
        }

        if (SelectedDeductSource.HasValue && DeductSources.Any(option => option.Id == SelectedDeductSource.Value))
            return;

        SelectedDeductSource = DeductSources.Count > 0 ? DeductSources[0].Id : null;
    }

    private static IEnumerable<AccountActivityItemVM> BuildActivities(
        IEnumerable<Transaction> expenseLogs,
        IEnumerable<Transaction> incomeLogs)
    {
        var expenseActivities = expenseLogs.Where(c => c.ParentTransactionId == null).Select(log => new AccountActivityItemVM(
            log.OccurredOn,
            log.Name.Trim() is { Length: > 0 } expenseName ? expenseName : "Expense",
            string.IsNullOrWhiteSpace(log.Notes) ? "Expense" : log.Notes.Trim(),
            log.Amount,
            true));

        var incomeActivities = incomeLogs.Select(log => new AccountActivityItemVM(
            log.OccurredOn,
            BuildIncomeTitle(log.Notes),
            string.IsNullOrWhiteSpace(log.Notes) ? "Income" : log.Notes.Trim(),
            log.Amount,
            false));

        return expenseActivities
            .Concat(incomeActivities)
            .OrderByDescending(item => item.Date)
            .Take(8)
            .ToList();
    }

    private static IEnumerable<AccountTrendItemVM> BuildTrends(
        IEnumerable<Transaction> expenseLogs,
        IEnumerable<Transaction> incomeLogs)
    {
        var months = Enumerable.Range(0, 4)
            .Select(offset => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-3 + offset))
            .ToList();

        var incomeByMonth = incomeLogs
            .GroupBy(log => new DateTime(log.OccurredOn.Year, log.OccurredOn.Month, 1))
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));

        var expenseByMonth = expenseLogs
            .GroupBy(log => new DateTime(log.OccurredOn.Year, log.OccurredOn.Month, 1))
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));

        return months.Select(month => new AccountTrendItemVM(
                month,
                month.Year == DateTime.Today.Year ? month.ToString("MMM") : month.ToString("MMM yy"),
                incomeByMonth.GetValueOrDefault(month),
                expenseByMonth.GetValueOrDefault(month)))
            .ToList();
    }

    private static string BuildIncomeTitle(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "Income";

        var firstMeaningfulLine = notes
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        return string.IsNullOrWhiteSpace(firstMeaningfulLine) ? "Income" : firstMeaningfulLine;
    }

    private static bool TryParseMonthlyDueDate(string text, out int monthlyDueDate)
    {
        monthlyDueDate = 0;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out monthlyDueDate) &&
               monthlyDueDate is >= MonthlyDueDateHelper.MinMonthlyDay and <= MonthlyDueDateHelper.MaxMonthlyDay;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
            target.Add(item);
    }

    private static bool ResolvePinnedOnUiFromEnabledState(bool previousIsEnabled, bool nextIsEnabled, bool requestedPinnedOnUi)
    {
        if (!nextIsEnabled)
            return false;

        if (previousIsEnabled == nextIsEnabled)
            return requestedPinnedOnUi;

        return true;
    }

    public readonly record struct AccountDetailResult(bool IsSuccess, bool ShouldClose, string? ErrorMessage)
    {
        public static AccountDetailResult Success(bool shouldClose = false)
        {
            return new AccountDetailResult(true, shouldClose, null);
        }

        public static AccountDetailResult Failure(string? errorMessage)
        {
            return new AccountDetailResult(false, false, errorMessage);
        }
    }

    private readonly record struct AccountDetailState(
        string Name,
        AccountType AccountType,
        decimal PrimaryAmount,
        decimal AccountLimit,
        decimal MaximumSpending,
        decimal? MinimumPayment,
        decimal SpentAmount,
        int? MonthlyDueDate,
        int? DeductSource,
        decimal? InterestRate,
        bool IsEnabled,
        bool PinnedOnUI)
    {
        public static AccountDetailState Empty => new(
            string.Empty,
            AccountType.Checking,
            0m,
            0m,
            0m,
            null,
            0m,
            null,
            null,
            null,
            true,
            true);
    }

    public readonly record struct DeductSourceOption(
        int Id,
        string Name,
        AccountType AccountType = AccountType.Checking)
    {
        public string TypeDisplayName => AccountType switch
        {
            AccountType.Credit => "Credit",
            AccountType.Checking => "Checking",
            AccountType.Cash => "Cash",
            AccountType.Saving => "Savings",
            _ => "Account"
        };
    }
}

public sealed record AccountActivityItemVM(
    DateTime Date,
    string Title,
    string Detail,
    decimal Amount,
    bool IsExpense)
{
    public string DirectionLabel => IsExpense ? "Expense" : "Income";
}

public sealed record AccountTrendItemVM(
    DateTime Month,
    string Label,
    decimal IncomeAmount,
    decimal ExpenseAmount);
