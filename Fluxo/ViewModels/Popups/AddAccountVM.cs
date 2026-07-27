using System.Globalization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Shell;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;
using Fluxo.Helpers.Popups;

using Fluxo.DataModels.Popups.AddAccount;
namespace Fluxo.ViewModels.Popups;

public partial class AddAccountVM : ObservableValidator
{
    private const string BalanceUpdateTagColor = "#e8ca5f";
    private const string BalanceUpdateTagName = "Balance Update";
    private const int MaxNameLength = 256;
    private const decimal PercentageMaximum = 100m;

    private readonly MainVM _mainViewModel;
    private readonly IAppDataService _appData;
    private readonly HashSet<string> _knownAccountNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<AddAccountInput, Task<AddAccountResult>>? _saveDraftAsync;
    private readonly Func<int?, Task<IReadOnlyList<DeductSourceOption>>>? _loadDraftDeductSourcesAsync;
    private FormState _initialState;
    private bool _isChangeTrackingInitialized;
    private bool _isApyValidationActive;
    private bool _isMaximumSpendingModified;
    private bool _isMaximumSpendingValidationActive;
    private bool _isMinimumPaymentValidationActive;
    private bool _isNameValidationActive;
    private bool _isSpentAmountValidationActive;

    [ObservableProperty] private decimal _accountLimitText;
    [ObservableProperty]
    [CustomValidation(typeof(AddAccountVM), nameof(ValidateApyText))]
    private decimal _apyText;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isDefault;
    [ObservableProperty]
    [CustomValidation(typeof(AddAccountVM), nameof(ValidateMaximumSpendingText))]
    private decimal _maximumSpendingText;
    [ObservableProperty]
    [CustomValidation(typeof(AddAccountVM), nameof(ValidateMinimumPaymentText))]
    private decimal _minimumPaymentText;
    [ObservableProperty] private string _monthlyDueDateText = GetDefaultMonthlyDueDateText();
    [ObservableProperty]
    [CustomValidation(typeof(AddAccountVM), nameof(ValidateNameText))]
    private string _nameText = string.Empty;
    [ObservableProperty] private decimal _primaryAmountText;
    [ObservableProperty] private int? _selectedDeductSource;
    [ObservableProperty] private AccountType _selectedAccountType = AccountType.Checking;
    [ObservableProperty] private bool _pinnedOnUI = true;
    [ObservableProperty]
    [CustomValidation(typeof(AddAccountVM), nameof(ValidateSpentAmountText))]
    private decimal _spentAmountText;

    public int? EditingId { get; set; }

    public AddAccountVM(
        MainVM mainViewModel,
        IAppDataService appData,
        Func<AddAccountInput, Task<AddAccountResult>>? saveDraftAsync = null,
        Func<int?, Task<IReadOnlyList<DeductSourceOption>>>? loadDraftDeductSourcesAsync = null)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _saveDraftAsync = saveDraftAsync;
        _loadDraftDeductSourcesAsync = loadDraftDeductSourcesAsync;
        DeductSourcesView = AccountComboBoxViewFactory.CreateGroupedByTypeThenName(
            DeductSources,
            nameof(DeductSourceOption.TypeDisplayName),
            nameof(DeductSourceOption.AccountType),
            nameof(DeductSourceOption.Name));
        ErrorsChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(CanSave));

            if (e.PropertyName == nameof(NameText))
                OnPropertyChanged(nameof(NameValidationHint));

            if (e.PropertyName == nameof(MaximumSpendingText))
                OnPropertyChanged(nameof(MaximumSpendingValidationHint));

            if (e.PropertyName == nameof(SpentAmountText) || e.PropertyName == nameof(AccountLimitText))
                OnPropertyChanged(nameof(SpentAmountValidationHint));

            if (e.PropertyName == nameof(MinimumPaymentText))
                OnPropertyChanged(nameof(MinimumPaymentValidationHint));

            if (e.PropertyName == nameof(ApyText))
                OnPropertyChanged(nameof(ApyValidationHint));
        };
        _initialState = CaptureState();
    }

    public IReadOnlyList<AccountTypeOption> AccountTypes { get; } =
    [
        new("Checking", AccountType.Checking),
        new("Cash", AccountType.Cash),
        new("Credit", AccountType.Credit),
        new("Savings", AccountType.Saving)
    ];

    public ObservableCollection<DeductSourceOption> DeductSources { get; } = [];
    public ICollectionView DeductSourcesView { get; }

    public bool CanSave => !IsBusy && !HasErrors && AreRequiredFieldsFilled();
    public bool HasChanges => _isChangeTrackingInitialized && !CaptureState().Equals(_initialState);
    public bool IsEditMode => EditingId.HasValue;
    public string NotificationAction => IsEditMode ? "Updated" : "Added";
    public bool IsSourceTypeSelectionEnabled => !IsEditMode;
    public string PopupTitle => IsEditMode ? "Edit Account" : "Add New Account";
    public string HeaderTitle => PopupTitle;

    public string HeaderDescription => IsEditMode
        ? "Update this account for checking, cash, credit, or savings."
        : "Set up a new account for checking, cash, credit, or savings.";

    public string ValidationDialogTitle => PopupTitle;
    public string NameValidationHint => GetValidationHint(nameof(NameText));
    public string MaximumSpendingValidationHint => GetValidationHint(nameof(MaximumSpendingText));
    public string SpentAmountValidationHint => GetValidationHint(nameof(SpentAmountText));
    public string MinimumPaymentValidationHint => GetValidationHint(nameof(MinimumPaymentText));
    public string ApyValidationHint => GetValidationHint(nameof(ApyText));
    public string MaximumSpendingPlaceholderText => FormatPlaceholderAmount(GetMaximumSpendingPlaceholderValue());

    public void BeginChangeTracking()
    {
        _initialState = CaptureState();
        _isChangeTrackingInitialized = true;
        NotifyFormStateChanged();
    }

    public void InitializeFromAccount(Account source)
    {
        ArgumentNullException.ThrowIfNull(source);

        EditingId = source.Id;
        NameText = source.Name;
        SelectedAccountType = source.AccountType;
        PinnedOnUI = source.PinnedOnUI;
        IsEnabled = source.IsEnabled;
        IsDefault = source.IsDefault;

        if (source.AccountType == AccountType.Credit)
        {
            PrimaryAmountText = source.SpentAmount;
            SpentAmountText = source.SpentAmount;
            AccountLimitText = source.AccountLimit;
            MaximumSpendingText = source.MaximumSpending;
            MinimumPaymentText = source.MinimumPayment ?? 0m;
            MonthlyDueDateText = MonthlyDueDateHelper.Normalize(source.MonthlyDueDate)?
                .ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            SelectedDeductSource = source.DeductSource;
        }
        else
        {
            PrimaryAmountText = source.Balance;
            MaximumSpendingText = source.MaximumSpending;
        }

        if (source.AccountType == AccountType.Saving && source.InterestRate.HasValue)
            ApyText = source.InterestRate.Value;

        OnPropertyChanged(nameof(IsSourceTypeSelectionEnabled));
        ResetValidationState();
    }

    public void ResetAfterSaveAndCreateNew()
    {
        AccountLimitText = 0m;
        ApyText = 0m;
        IsEnabled = true;
        IsDefault = false;
        MaximumSpendingText = 0m;
        MinimumPaymentText = 0m;
        MonthlyDueDateText = GetDefaultMonthlyDueDateText();
        NameText = string.Empty;
        PrimaryAmountText = 0m;
        SelectedAccountType = AccountType.Checking;
        PinnedOnUI = true;
        SpentAmountText = 0m;
        SelectedDeductSource = null;
        ResetValidationState();
        BeginChangeTracking();
    }

    public void ValidateNameField()
    {
        _isNameValidationActive = true;
        ValidateProperty(NameText, nameof(NameText));
    }

    public void ValidateMaximumSpendingField()
    {
        _isMaximumSpendingValidationActive = true;
        ValidateProperty(MaximumSpendingText, nameof(MaximumSpendingText));
    }

    public void ValidateSpentAmountField()
    {
        _isSpentAmountValidationActive = true;
        ValidateProperty(SpentAmountText, nameof(SpentAmountText));
    }

    public void ValidateMinimumPaymentField()
    {
        _isMinimumPaymentValidationActive = true;
        ValidateProperty(MinimumPaymentText, nameof(MinimumPaymentText));
    }

    public void ValidateApyField()
    {
        _isApyValidationActive = true;
        ValidateProperty(ApyText, nameof(ApyText));
    }

    public void MarkMaximumSpendingModified()
    {
        _isMaximumSpendingModified = true;
    }

    public bool IsCredit => SelectedAccountType == AccountType.Credit;
    public bool IsSaving => SelectedAccountType == AccountType.Saving;
    public bool IsCashLike => SelectedAccountType is AccountType.Checking or AccountType.Cash;
    public bool IsBalanceLike => IsCashLike || IsSaving;
    public string PrimaryAmountLabel => IsCredit ? "Current spent" : IsCashLike ? "Current amount" : "Current balance";

    partial void OnAccountLimitTextChanged(decimal value)
    {
        OnPropertyChanged(nameof(MaximumSpendingPlaceholderText));
        SyncMaximumSpendingToPlaceholder();
        RefreshActiveValidation(nameof(MaximumSpendingText), nameof(SpentAmountText));
        NotifyFormStateChanged();
    }

    partial void OnApyTextChanged(decimal value)
    {
        RefreshActiveValidation(nameof(ApyText));
        NotifyFormStateChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyFormStateChanged();

    partial void OnIsEnabledChanged(bool value)
    {
        if (PinnedOnUI != value)
            PinnedOnUI = value;

        NotifyFormStateChanged();
    }

    partial void OnMaximumSpendingTextChanged(decimal value)
    {
        RefreshActiveValidation(nameof(MaximumSpendingText));
        NotifyFormStateChanged();
    }

    partial void OnMinimumPaymentTextChanged(decimal value)
    {
        RefreshActiveValidation(nameof(MinimumPaymentText));
        NotifyFormStateChanged();
    }

    partial void OnMonthlyDueDateTextChanged(string value) => NotifyFormStateChanged();

    partial void OnNameTextChanged(string value)
    {
        _isNameValidationActive = true;
        RefreshActiveValidation(nameof(NameText));
        NotifyFormStateChanged();
    }

    partial void OnPrimaryAmountTextChanged(decimal value)
    {
        OnPropertyChanged(nameof(MaximumSpendingPlaceholderText));
        SyncMaximumSpendingToPlaceholder();
        RefreshActiveValidation(nameof(MaximumSpendingText));
        NotifyFormStateChanged();
    }

    partial void OnSelectedDeductSourceChanged(int? value) => NotifyFormStateChanged();

    partial void OnPinnedOnUIChanged(bool value) => NotifyFormStateChanged();

    partial void OnSpentAmountTextChanged(decimal value)
    {
        RefreshActiveValidation(nameof(MaximumSpendingText), nameof(SpentAmountText));
        NotifyFormStateChanged();
    }

    partial void OnSelectedAccountTypeChanged(AccountType value)
    {
        OnPropertyChanged(nameof(IsCredit));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(IsCashLike));
        OnPropertyChanged(nameof(IsBalanceLike));
        OnPropertyChanged(nameof(PrimaryAmountLabel));
        OnPropertyChanged(nameof(MaximumSpendingPlaceholderText));
        SyncMaximumSpendingToPlaceholder();

        if (!IsCredit)
        {
            AccountLimitText = 0m;
            SpentAmountText = 0m;
            MonthlyDueDateText = string.Empty;
            SelectedDeductSource = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(MonthlyDueDateText))
                MonthlyDueDateText = GetDefaultMonthlyDueDateText();

            if (!SelectedDeductSource.HasValue && DeductSources.Count > 0)
                SelectedDeductSource = DeductSources[0].Id;
        }

        if (!IsCredit)
        {
            _isMaximumSpendingModified = false;
            SyncMaximumSpendingToPlaceholder();
            MinimumPaymentText = 0m;
        }

        if (!IsSaving)
            ApyText = 0m;

        ClearValidationState();
        NotifyFormStateChanged();
    }

    public async Task LoadDeductSourcesAsync(CancellationToken cancellationToken = default)
    {
        if (_loadDraftDeductSourcesAsync is null)
            await LoadKnownAccountNamesAsync(cancellationToken);

        IReadOnlyList<DeductSourceOption> options;
        if (_loadDraftDeductSourcesAsync is not null)
        {
            options = await _loadDraftDeductSourcesAsync(EditingId);
        }
        else
        {
            var existingSources = await _appData.GetAccountsAsync(cancellationToken);
            options = existingSources
                .Where(source => source.Id != (EditingId ?? 0))
                .Where(source => source.IsEnabled)
                .Where(source => source.AccountType != AccountType.Credit)
                .OrderBy(source => source.AccountType)
                .ThenBy(source => source.Name)
                .Select(source => new DeductSourceOption(source.Id, source.Name, source.AccountType))
                .ToList();
        }

        DeductSources.Clear();
        foreach (var option in options)
            DeductSources.Add(option);

        if (!IsCredit)
        {
            SelectedDeductSource = null;
            return;
        }

        if (SelectedDeductSource.HasValue && DeductSources.Any(option => option.Id == SelectedDeductSource.Value))
            return;

        SelectedDeductSource = DeductSources.Count > 0 ? DeductSources[0].Id : null;
    }

    public async Task<AddAccountResult> SaveAsync()
    {
        if (IsBusy)
            return AddAccountResult.Failure("An account is already being saved.");

        if (!TryBuildInput(out var input, out var validationMessage, out var failurePresentation))
            return AddAccountResult.Failure(validationMessage, failurePresentation);

        if (_saveDraftAsync is not null)
            return await _saveDraftAsync(input);

        IsBusy = true;

        try
        {
            await LoadKnownAccountNamesAsync(CancellationToken.None);
            var existingSources = await _appData.GetAccountsAsync();
            if (HasDuplicateSourceName(input.Name))
                return AddAccountResult.Failure(
                    $"An account named \"{input.Name}\" already exists.");

            Account account;
            AccountMemorySnapshot? beforeSnapshot = null;
            var previousSpentAmount = 0m;

            if (input.IsDefault)
            {
                foreach (var other in existingSources.Where(source =>
                             source.Id != EditingId && source.IsDefault))
                {
                    other.IsDefault = false;
                    _appData.UpdateAccount(other);
                }

                await _appData.SaveChangesAsync();
            }

            if (EditingId.HasValue)
            {
                account = existingSources.FirstOrDefault(s => s.Id == EditingId.Value)
                                 ?? throw new InvalidOperationException("Account not found.");
                beforeSnapshot = AccountMemorySnapshot.Create(account);
                previousSpentAmount = account.SpentAmount;
                account.Name = input.Name;
                account.AccountType = input.AccountType;
                account.AccountLimit = input.AccountLimit;
                account.SpentAmount = input.SpentAmount;
                account.Balance = input.Balance;
                account.MaximumSpending = input.MaximumSpending;
                account.MinimumPayment = input.MinimumPayment;
                account.MonthlyDueDate = input.MonthlyDueDate;
                account.DeductSource = input.DeductSource;
                account.InterestRate = input.InterestRate;
                account.PinnedOnUI = ResolvePinnedOnUiFromEnabledState(account.IsEnabled, input.IsEnabled, input.PinnedOnUI);
                account.IsEnabled = input.IsEnabled;
                account.IsDefault = input.IsDefault;
                _appData.UpdateAccount(account);
            }
            else
            {
                account = new Account
                {
                    Name = input.Name,
                    AccountType = input.AccountType,
                    AccountLimit = input.AccountLimit,
                    MaximumSpending = input.MaximumSpending,
                    MinimumPayment = input.MinimumPayment,
                    SpentAmount = input.SpentAmount,
                    Balance = input.Balance,
                    MonthlyDueDate = input.MonthlyDueDate,
                    DeductSource = input.DeductSource,
                    InterestRate = input.InterestRate,
                    PinnedOnUI = ResolvePinnedOnUiForCreation(input.IsEnabled, input.PinnedOnUI),
                    IsEnabled = input.IsEnabled,
                    IsDefault = input.IsDefault
                };
                await _appData.AddAccountAsync(account);
            }

            await _appData.SaveChangesAsync();

            if (!EditingId.HasValue)
                WeakReferenceMessenger.Default.Send(new NotificationEntityCreatedMessage(NotificationEntityKind.Account, account.Id));

            if (!EditingId.HasValue &&
                account.AccountType == AccountType.Credit &&
                account.MonthlyDueDate.HasValue &&
                account.DeductSource.HasValue)
            {
                var paymentTag = await ResolvePaymentOrTransferTagAsync(_appData);
                await _appData.AddRecurringTransactionAsync(new RecurringTransaction
                {
                    Name = $"Payment to {account.Name}",
                    Amount = Math.Max(account.SpentAmount, 0m),
                    RecurringPeriod = RecurringPeriod.Monthly,
                    RecurringTime = account.MonthlyDueDate.Value,
                    Type = RecurringTransactionType.Expense,
                    Category = ExpenseCategory.Needs,
                    SourceId = account.DeductSource.Value,
                    TagId = paymentTag?.Id,
                    GoalId = null,
                    IsEnabled = true
                });
                await _appData.SaveChangesAsync();
            }

            var afterSnapshot = AccountMemorySnapshot.Create(account);
            var autoTransactionSnapshot = await TryCreateBalanceUpdateTransactionAsync(
                _appData,
                account,
                previousSpentAmount,
                input.SpentAmount,
                EditingId.HasValue);

            ILogMemoryAction sourceAction = EditingId.HasValue
                ? new EditAccountMemoryAction(beforeSnapshot, afterSnapshot)
                : new AddAccountMemoryAction(afterSnapshot);

            ILogMemoryAction historyAction = autoTransactionSnapshot is null
                ? sourceAction
                : new CompositeLogMemoryAction(
                    sourceAction.Description,
                    [
                        sourceAction,
                        new AddTransactionMemoryAction(
                            autoTransactionSnapshot,
                            shouldAdjustAccountTotals: false)
                    ]);

            WeakReferenceMessenger.Default.Send(new RecordLogMemoryMessage(historyAction));
            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(
                DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications));

            await _mainViewModel.ReloadCurrentDataAsync();
            FloatingNotificationPublisher.Success(
                input.Name,
                IsEditMode ? "Account details were updated." : "Account is ready to use.",
                true,
                NotificationAction);
            return AddAccountResult.Success(true);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(
                WeakReferenceMessenger.Default,
                exception,
                IsEditMode ? "update account" : "create account");
            return AddAccountResult.Failure(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static decimal CalculateBalanceUpdateAmount(
        AccountType sourceType,
        bool isEdit,
        decimal currentSpentAmount,
        decimal previousSpentAmount)
    {
        if (sourceType != AccountType.Credit)
            return 0m;

        return isEdit
            ? currentSpentAmount - previousSpentAmount
            : currentSpentAmount;
    }

    private static async Task<Tag> ResolveBalanceUpdateTagAsync(IAppDataService appData)
    {
        var tags = await appData.GetTagsAsync();
        var existingTag = tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, BalanceUpdateTagName, StringComparison.OrdinalIgnoreCase));
        if (existingTag is not null)
            return existingTag;

        var balanceUpdateTag = new Tag
        {
            Name = BalanceUpdateTagName,
            HexCode = BalanceUpdateTagColor
        };

        await appData.AddTagAsync(balanceUpdateTag);
        await appData.SaveChangesAsync();
        return balanceUpdateTag;
    }

    private static async Task<Tag?> ResolvePaymentOrTransferTagAsync(IAppDataService appData)
    {
        var tags = await appData.GetTagsAsync();
        return tags
            .OrderByDescending(tag => string.Equals(tag.Name, "Payment", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(tag => string.Equals(tag.Name, "Transfer", StringComparison.OrdinalIgnoreCase))
            .ThenBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static async Task<TransactionMemorySnapshot?> TryCreateBalanceUpdateTransactionAsync(
        IAppDataService appData,
        Account account,
        decimal previousSpentAmount,
        decimal currentSpentAmount,
        bool isEdit)
    {
        var triggerAmount = CalculateBalanceUpdateAmount(
            account.AccountType,
            isEdit,
            currentSpentAmount,
            previousSpentAmount);
        if (triggerAmount <= 0m)
            return null;

        var balanceUpdateTag = await ResolveBalanceUpdateTagAsync(appData);
        var transaction = new Transaction
        {
            Type = TransactionType.Expense,
            Name = $"Balance Update",
            Amount = triggerAmount,
            OccurredOn = DateTime.Now,
            Notes = string.Empty,
            ExpenseCategory = ExpenseCategory.Needs,
            SourceAccountId = account.Id,
            TagId = balanceUpdateTag.Id
        };

        await appData.AddTransactionAsync(transaction);
        await appData.SaveChangesAsync();

        return TransactionMemorySnapshot.Create(transaction);
    }

    private bool TryBuildInput(
        out AddAccountInput input,
        out string validationMessage,
        out AddAccountFailurePresentation failurePresentation)
    {
        input = default;
        validationMessage = string.Empty;
        failurePresentation = AddAccountFailurePresentation.Dialog;

        var name = (NameText ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            validationMessage = "Please enter an account name.";
            return false;
        }

        if (name.Length > MaxNameLength)
        {
            validationMessage = $"Account name cannot exceed {MaxNameLength} characters.";
            return false;
        }

        if (name.Any(char.IsControl))
        {
            validationMessage = "Account name cannot contain control characters.";
            return false;
        }

        var primaryAmount = PrimaryAmountText;
        var spentAmount = SpentAmountText;
        var accountLimit = AccountLimitText;
        var maximumSpending = GetEffectiveMaximumSpending();
        var minimumPayment = IsCredit ? MinimumPaymentText : 0m;
        decimal? interestRate = IsSaving ? ApyText : null;

        if (primaryAmount < 0m || spentAmount < 0m || accountLimit < 0m ||
            maximumSpending < 0m || minimumPayment < 0m ||
            (interestRate.HasValue && interestRate.Value < 0m))
        {
            validationMessage = "Values must be zero or greater.";
            return false;
        }

        if (SelectedAccountType == AccountType.Credit && accountLimit <= 0m)
        {
            validationMessage = "Credit accounts require an account limit greater than zero.";
            return false;
        }

        if (IsCredit && spentAmount > accountLimit)
        {
            validationMessage = "Spent amount cannot exceed the account limit.";
            failurePresentation = AddAccountFailurePresentation.ToastWarning;
            return false;
        }

        if (IsCredit && minimumPayment <= 0m)
        {
            validationMessage = "Minimum payment must be greater than zero.";
            return false;
        }

        if (IsSaving && interestRate.HasValue && interestRate.Value <= 0m)
        {
            validationMessage = "APY must be greater than zero.";
            return false;
        }

        if (IsSaving && interestRate.HasValue && interestRate.Value > PercentageMaximum)
        {
            validationMessage = "APY cannot exceed 100%.";
            failurePresentation = AddAccountFailurePresentation.ToastWarning;
            return false;
        }

        if (IsCredit && minimumPayment > PercentageMaximum)
        {
            validationMessage = "Minimum payment cannot exceed 100%.";
            failurePresentation = AddAccountFailurePresentation.ToastWarning;
            return false;
        }

        if (IsCredit && maximumSpending > 0m)
        {
            if (maximumSpending > accountLimit)
            {
                validationMessage = "Maximum spending cannot exceed the account limit.";
                failurePresentation = AddAccountFailurePresentation.ToastWarning;
                return false;
            }
        }

        if (SelectedAccountType is AccountType.Checking or AccountType.Cash &&
            maximumSpending > primaryAmount)
        {
            validationMessage = "Maximum spending cannot exceed the current balance.";
            failurePresentation = AddAccountFailurePresentation.ToastWarning;
            return false;
        }

        int? monthlyDueDate = null;
        if (IsCredit)
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
        }

        input = new AddAccountInput(
            name,
            SelectedAccountType,
            IsCredit ? 0m : primaryAmount,
            IsCredit ? spentAmount : 0m,
            IsCredit ? accountLimit : 0m,
            maximumSpending,
            IsCredit ? minimumPayment : null,
            monthlyDueDate,
            IsCredit ? SelectedDeductSource : null,
            interestRate,
            PinnedOnUI,
            IsEnabled,
            IsDefault);

        return true;
    }

    private static bool TryParseMonthlyDueDate(string text, out int monthlyDueDate)
    {
        monthlyDueDate = 0;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out monthlyDueDate) &&
               monthlyDueDate is >= MonthlyDueDateHelper.MinMonthlyDay and <= MonthlyDueDateHelper.MaxMonthlyDay;
    }

    private static string GetDefaultMonthlyDueDateText()
    {
        return MonthlyDueDateHelper.Normalize(DateTime.Today.Day)?.ToString(CultureInfo.InvariantCulture) ??
               MonthlyDueDateHelper.MinMonthlyDay.ToString(CultureInfo.InvariantCulture);
    }

    private async Task LoadKnownAccountNamesAsync(CancellationToken cancellationToken)
    {
        _knownAccountNames.Clear();

        var existingSources = await _appData.GetAccountsAsync(cancellationToken);
        foreach (var source in existingSources)
        {
            if (source.Id == (EditingId ?? int.MinValue))
                continue;

            var normalizedName = NormalizeSourceName(source.Name);
            if (normalizedName.Length > 0)
                _knownAccountNames.Add(normalizedName);
        }
    }

    private bool HasDuplicateSourceName(string name)
    {
        var normalizedName = NormalizeSourceName(name);
        return normalizedName.Length > 0 && _knownAccountNames.Contains(normalizedName);
    }

    private static string NormalizeSourceName(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static bool ResolvePinnedOnUiForCreation(bool isEnabled, bool requestedPinnedOnUi)
    {
        return isEnabled && requestedPinnedOnUi;
    }

    private static bool ResolvePinnedOnUiFromEnabledState(bool previousIsEnabled, bool nextIsEnabled, bool requestedPinnedOnUi)
    {
        if (!nextIsEnabled)
            return false;

        if (previousIsEnabled == nextIsEnabled)
            return requestedPinnedOnUi;

        return true;
    }

    private bool AreRequiredFieldsFilled()
    {
        if (!HasValidNameValue(NameText))
            return false;

        if (IsCredit && AccountLimitText <= 0m)
            return false;

        if (IsCredit && !TryParseMonthlyDueDate(MonthlyDueDateText, out _))
            return false;

        if (IsCredit && !SelectedDeductSource.HasValue)
            return false;

        return true;
    }

    private static bool HasValidNameValue(string? value)
    {
        var trimmedName = value?.Trim() ?? string.Empty;
        return trimmedName.Length > 0 &&
               trimmedName.Length <= MaxNameLength &&
               !trimmedName.Any(char.IsControl);
    }

    private void RefreshActiveValidation(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (propertyName == nameof(NameText) && _isNameValidationActive)
                ValidateProperty(NameText, nameof(NameText));

            if (propertyName == nameof(MaximumSpendingText) && _isMaximumSpendingValidationActive)
                ValidateProperty(MaximumSpendingText, nameof(MaximumSpendingText));

            if (propertyName == nameof(SpentAmountText) && _isSpentAmountValidationActive)
                ValidateProperty(SpentAmountText, nameof(SpentAmountText));

            if (propertyName == nameof(MinimumPaymentText) && _isMinimumPaymentValidationActive)
                ValidateProperty(MinimumPaymentText, nameof(MinimumPaymentText));

            if (propertyName == nameof(ApyText) && _isApyValidationActive)
                ValidateProperty(ApyText, nameof(ApyText));
        }
    }

    private void ResetValidationState()
    {
        _isMaximumSpendingModified = false;
        ClearValidationState();
    }

    private void ClearValidationState()
    {
        _isApyValidationActive = false;
        _isMaximumSpendingValidationActive = false;
        _isMinimumPaymentValidationActive = false;
        _isNameValidationActive = false;
        _isSpentAmountValidationActive = false;
        ClearErrors();
        OnPropertyChanged(nameof(NameValidationHint));
        OnPropertyChanged(nameof(MaximumSpendingValidationHint));
        OnPropertyChanged(nameof(SpentAmountValidationHint));
        OnPropertyChanged(nameof(MinimumPaymentValidationHint));
        OnPropertyChanged(nameof(ApyValidationHint));
        OnPropertyChanged(nameof(CanSave));
    }

    private string GetValidationHint(string propertyName)
    {
        var message = GetErrors(propertyName)
            .OfType<ValidationResult>()
            .Select(result => result.ErrorMessage)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        return propertyName switch
        {
            nameof(NameText) => GetNameValidationHint(message),
            nameof(MaximumSpendingText) => "Exceeds Limit",
            nameof(SpentAmountText) => "Exceeds Limit",
            nameof(MinimumPaymentText) => message.Contains("greater than zero", StringComparison.OrdinalIgnoreCase)
                ? "Required"
                : "Invalid Rate",
            nameof(ApyText) => message.Contains("greater than zero", StringComparison.OrdinalIgnoreCase)
                ? "Required"
                : "Invalid Rate",
            _ => string.Empty
        };
    }

    private static string GetNameValidationHint(string message)
    {
        if (message.Contains("enter an account name", StringComparison.OrdinalIgnoreCase))
            return "Required";

        if (message.Contains("exceed", StringComparison.OrdinalIgnoreCase))
            return "Too Long";

        if (message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            return "Existing Name";

        return "Invalid Name";
    }

    private decimal GetEffectiveMaximumSpending()
    {
        return MaximumSpendingText;
    }

    private void SyncMaximumSpendingToPlaceholder()
    {
        if (!_isMaximumSpendingModified)
            MaximumSpendingText = GetMaximumSpendingPlaceholderValue();
    }

    private decimal GetMaximumSpendingPlaceholderValue()
    {
        return SelectedAccountType switch
        {
            AccountType.Credit => AccountLimitText,
            AccountType.Checking or AccountType.Cash => PrimaryAmountText,
            _ => 0m
        };
    }

    private static string FormatPlaceholderAmount(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private FormState CaptureState()
    {
        return new FormState(
            NameText ?? string.Empty,
            PrimaryAmountText,
            SpentAmountText,
            AccountLimitText,
            MinimumPaymentText,
            ApyText,
            PinnedOnUI,
            IsEnabled,
            IsDefault);
    }

    private void NotifyFormStateChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasChanges));
    }

    public static ValidationResult? ValidateNameText(string value, ValidationContext validationContext)
    {
        var viewModel = (AddAccountVM)validationContext.ObjectInstance;
        var trimmedName = value?.Trim() ?? string.Empty;

        if (trimmedName.Length == 0)
            return new ValidationResult("Please enter an account name.");

        if (trimmedName.Length > MaxNameLength)
            return new ValidationResult($"Account name cannot exceed {MaxNameLength} characters.");

        if (trimmedName.Any(char.IsControl))
            return new ValidationResult("Account name cannot contain control characters.");

        if (viewModel.HasDuplicateSourceName(trimmedName))
            return new ValidationResult($"An account named \"{trimmedName}\" already exists.");

        return ValidationResult.Success;
    }

    public static ValidationResult? ValidateMaximumSpendingText(decimal value, ValidationContext validationContext)
    {
        var viewModel = (AddAccountVM)validationContext.ObjectInstance;
        if (value < 0m)
            return new ValidationResult("Maximum spending must be zero or greater.");

        if (value == 0m)
            return ValidationResult.Success;

        if (viewModel.SelectedAccountType is AccountType.Checking or AccountType.Cash &&
            value > viewModel.PrimaryAmountText)
            return new ValidationResult("Maximum spending cannot exceed the current balance.");

        if (viewModel.SelectedAccountType == AccountType.Credit &&
            value > viewModel.AccountLimitText)
            return new ValidationResult("Maximum spending cannot exceed the account limit.");

        return ValidationResult.Success;
    }

    public static ValidationResult? ValidateSpentAmountText(decimal value, ValidationContext validationContext)
    {
        var viewModel = (AddAccountVM)validationContext.ObjectInstance;
        if (value < 0m)
            return new ValidationResult("Spent amount must be zero or greater.");

        return viewModel.SelectedAccountType == AccountType.Credit &&
               viewModel.AccountLimitText > 0m &&
               value > viewModel.AccountLimitText
            ? new ValidationResult("Spent amount cannot exceed the account limit.")
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateMinimumPaymentText(decimal value, ValidationContext validationContext)
    {
        var viewModel = (AddAccountVM)validationContext.ObjectInstance;
        if (!viewModel.IsCredit)
            return ValidationResult.Success;

        if (value <= 0m)
            return new ValidationResult("Minimum payment must be greater than zero.");

        return value > PercentageMaximum
            ? new ValidationResult("Minimum payment cannot exceed 100%.")
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateApyText(decimal value, ValidationContext validationContext)
    {
        var viewModel = (AddAccountVM)validationContext.ObjectInstance;
        if (!viewModel.IsSaving)
            return ValidationResult.Success;

        if (value <= 0m)
            return new ValidationResult("APY must be greater than zero.");

        return value > PercentageMaximum
            ? new ValidationResult("APY cannot exceed 100%.")
            : ValidationResult.Success;
    }

    public enum AddAccountFailurePresentation
    {
        Dialog,
        ToastWarning
    }





}
