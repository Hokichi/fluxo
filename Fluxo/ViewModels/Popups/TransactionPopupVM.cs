using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Helpers.Transaction;
using Fluxo.Resources.CustomControls;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Transactions;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups.Helpers;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using System.Globalization;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.ViewModels.Popups;

public partial class TransactionPopupVM : ObservableValidator
{
    private const int DefaultVisibleTagSlots = 4;
    private const int MaxNameLength = 256;
    private const int NoAccountId = -1;
    private const int NoTagId = -1;
    private const int NoSavingGoalId = -1;
    private const decimal SimilarAmountTolerance = 0.05m;

    private readonly List<AccountVM> _availableAccounts = [];
    private readonly IReadOnlyList<AccountVM>? _accountsOverride;
    private readonly Func<RecurringDraftSaveInput, Task<TransactionPopupSubmissionResult>>? _saveRecurringDraftAsync;
    private readonly List<SavingGoalVM> _orderedGoals = [];
    private readonly MainVM _mainViewModel;
    private readonly List<TagVM> _orderedTags = [];
    private readonly IAppDataService _appData;
    private FormState _initialState;
    private bool _isChangeTrackingInitialized;
    private bool _isAmountValidationActive;
    private string _amountWarningHint = string.Empty;
    private bool _isNameValidationActive;
    private TransactionPopupPurpose _popupPurpose = TransactionPopupPurpose.AddNewTransaction;
    private bool _isTransactionTypeLocked;
    private bool _isRepaymentAmountInvalid;
    private int _transactionNameSuggestionRequestVersion;
    private readonly List<AccountVM> _processingRepayments = [];
    private readonly List<SavingGoalVM> _processingGoals = [];
    private readonly List<RecurringTransactionVM> _processingRecurringTransactions = [];
    private readonly Dictionary<object, ProcessingState> _processingStates = [];
    private readonly Dictionary<object, FormState> _processingSnapshots = [];
    private int _currentProcessingIndex;
    private int? _currentProcessingRecurringTransactionId;
    private bool _isTransactionStateInitialized;

    [ObservableProperty]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateAmountText))]
    private decimal _amountText;

    [ObservableProperty] private bool _isExpense = true;
    [ObservableProperty] private bool _isGoal;
    [ObservableProperty] private bool _isRepayment;
    [ObservableProperty] private bool _isMoreTagsOpen;
    [ObservableProperty] private bool _isSaving;
    private bool _isUpdatingTagCollections;
    private int _visibleTagSlots = DefaultVisibleTagSlots;

    [ObservableProperty]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateNameText))]
    private string _nameText = string.Empty;

    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private bool _isRecurring;
    [ObservableProperty] private bool _isInstallments;
    [ObservableProperty] private DateTime _installmentEndDate = DateTime.Today;
    [ObservableProperty] private bool _isPinned;
    [ObservableProperty] private bool _isIoU;
    [ObservableProperty] private bool _shouldAffectBalance;
    [ObservableProperty] private bool _isExcludedFromBudget;
    [ObservableProperty] private bool _isHistoryOpen = true;
    [ObservableProperty] private AddNewTransactionHistoryItemVM? _selectedPinnedHistoryItem;
    [ObservableProperty] private AddNewTransactionHistoryItemVM? _selectedHistoryItem;
    [ObservableProperty] private RecurringPeriod _selectedRecurringPeriod = RecurringPeriod.Monthly;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateRecurringTimeText))]
    private string _recurringTimeText = string.Empty;

    [ObservableProperty] private bool _isRecurringModeLocked;
    [ObservableProperty] private ExpenseCategory _selectedExpenseCategory = ExpenseCategory.Needs;
    [ObservableProperty] private bool _canChangeRepaymentAccount = true;
    [ObservableProperty] private AccountVM? _selectedRepaymentAccount;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateSelectedGoal))]
    private SavingGoalVM? _selectedGoal;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateSelectedAccount))]
    private AccountVM? _selectedAccount;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateSelectedTag))]
    private TagVM? _selectedTag;

    public TransactionPopupVM(
        MainVM mainViewModel,
        IAppDataService appData,
        IReadOnlyList<AccountVM>? accountsOverride = null,
        Func<RecurringDraftSaveInput, Task<TransactionPopupSubmissionResult>>? saveRecurringDraftAsync = null)
    {
        _mainViewModel = mainViewModel;
        _appData = appData;
        _accountsOverride = accountsOverride;
        _saveRecurringDraftAsync = saveRecurringDraftAsync;
        ErrorsChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(CanSave));

            if (e.PropertyName == nameof(NameText))
                OnPropertyChanged(nameof(NameValidationHint));

            if (e.PropertyName == nameof(AmountText))
            {
                OnPropertyChanged(nameof(AmountValidationHint));
                NotifyAmountPresentationChanged();
            }
        };
        AccountsView = AccountComboBoxViewFactory.CreateGroupedByProperty(
            Accounts,
            nameof(AccountVM.TypeDisplayName));

        ReloadChoicesFromMainViewModel();
        ResetForm(false);
        _initialState = CaptureState();
    }

    public IReadOnlyList<ExpenseCategoryOption> ExpenseCategories { get; } =
    [
        new("Needs", ExpenseCategory.Needs),
        new("Wants", ExpenseCategory.Wants),
        new("Invest", ExpenseCategory.Savings)
    ];

    public ObservableCollection<AccountVM> Accounts { get; } = [];
    public ICollectionView AccountsView { get; }
    public ObservableCollection<AccountVM> RepaymentAccounts { get; } = [];
    public ObservableCollection<SavingGoalVM> Goals { get; } = [];
    public ObservableCollection<TagVM> VisibleTags { get; } = [];
    public ObservableCollection<TagVM> OverflowTags { get; } = [];
    public ObservableCollection<TransactionDetailChildTransactionVM> ChildTransactions { get; } = [];
    public ObservableCollection<AddNewTransactionSuggestion> TransactionNameSuggestions { get; } = [];
    public AddNewTransactionHistoryListVM PinnedHistory { get; } = new();
    public AddNewTransactionHistoryListVM TransactionHistory { get; } = new();
    public bool IsNeedsCategory { get => SelectedExpenseCategory == ExpenseCategory.Needs; set { if (value) SelectedExpenseCategory = ExpenseCategory.Needs; } }
    public bool IsWantsCategory { get => SelectedExpenseCategory == ExpenseCategory.Wants; set { if (value) SelectedExpenseCategory = ExpenseCategory.Wants; } }
    public bool IsInvestCategory { get => SelectedExpenseCategory == ExpenseCategory.Savings; set { if (value) SelectedExpenseCategory = ExpenseCategory.Savings; } }
    public bool ShowCategoryImpact => !HasChildTransactions && !IsViewOnly && !IsRecurringTransactionMode && AmountText > 0m && !IsUnpostedIoUMode &&
                                      (IsRepayment || (IsExpense && !IsExcludedFromBudget));
    public bool ShowAccountImpact => !HasChildTransactions && !IsViewOnly && !IsRecurringTransactionMode && AmountText > 0m && !IsUnpostedIoUMode && SelectedAccount is not null;
    public decimal CategoryCurrent => IsRepayment
        ? SelectedRepaymentAccount?.SpentAmount ?? 0m
        : ShowCategoryImpact ? GetCategoryCurrentAmount() : 0m;
    public decimal CategoryToBe => IsRepayment
        ? Math.Max(0m, CategoryCurrent - AmountText)
        : CategoryCurrent + AmountText;
    public decimal AccountCurrent => SelectedAccount is { } account
        ? account.IsCredit ? account.SpentAmount : account.Balance
        : 0m;
    public decimal AccountToBe => SelectedAccount is { } account
        ? account.IsCredit
            ? AccountCurrent + (IsIncome ? -AmountText : AmountText)
            : AccountCurrent + (IsIncome ? AmountText : -AmountText)
        : 0m;
    public IReadOnlyList<TransactionWarning> TransactionWarnings => BuildTransactionWarnings();

    public IReadOnlyList<RecurringPeriod> RecurringPeriods { get; } =
    [
        RecurringPeriod.None,
        RecurringPeriod.Weekly,
        RecurringPeriod.Biweekly,
        RecurringPeriod.Monthly
    ];

    public IReadOnlyList<RecurringTimeOption> WeekdayOptions { get; } =
    [
        new("Monday", "1"),
        new("Tuesday", "2"),
        new("Wednesday", "3"),
        new("Thursday", "4"),
        new("Friday", "5"),
        new("Saturday", "6"),
        new("Sunday", "7")
    ];

    public bool CanSave => !IsSaving && IsCurrentInputValid();
    public bool HasChanges => _isChangeTrackingInitialized && HasPendingTransactionInputChanges();
    public bool HasTransactionNameSuggestions => TransactionNameSuggestions.Count > 0;
    public bool IsRecurringTransactionMode => IsRecurring || IsInstallments;
    public bool IsRegularMode
    {
        get => !IsRecurring && !IsInstallments && !IsIoU;
        set { if (value) ClearTransactionModes(); }
    }
    public bool IsUnpostedIoUMode
    {
        get => IsIoU && !ShouldAffectBalance;
        set
        {
            if (!value) return;
            ClearTransactionModes();
            IsIoU = true;
            IsExcludedFromBudget = true;
        }
    }
    public bool IsPostedIoUMode
    {
        get => IsIoU && ShouldAffectBalance;
        set
        {
            if (!value) return;
            ClearTransactionModes();
            IsIoU = true;
            ShouldAffectBalance = true;
            IsExcludedFromBudget = true;
        }
    }
    public string TransactionModeDescription =>
        IsRecurring ? "A repeating transaction that occurs on a selected date" :
        IsInstallments ? "A repeating transaction that is split over time" :
        IsPostedIoUMode ? "A transaction marked as debt/IoU and affects the accounts" :
        IsUnpostedIoUMode ? "A transaction marked as debt/IoU but doesn't affect the accounts" :
        "A one-time transaction";
    public bool ShowRecurringDayInput => IsRecurringTransactionMode;
    public bool ShowRecurringNoneInput => IsRecurringTransactionMode && SelectedRecurringPeriod == RecurringPeriod.None;
    public bool ShowRecurringWeekdayInput => IsRecurringTransactionMode && IsWeekdayRecurringPeriod(SelectedRecurringPeriod);
    public bool ShowRecurringMonthlyInput => IsRecurringTransactionMode && SelectedRecurringPeriod == RecurringPeriod.Monthly;
    public bool ShowDateSelector => !IsRecurringTransactionMode;
    public bool ShowInstallmentEndDate => IsInstallments;
    public bool CanUseInstallments => !IsGoal && CanToggleRecurring;
    public bool CanUseIoU => !IsGoal && CanToggleRecurring;
    public bool CanToggleBudgetExclusion => !IsGoal && !IsRepayment && !IsIoU && !IsIncome;
    public bool IsBudgetExcluded
    {
        get => IsGoal || IsRepayment || IsIoU || IsIncome || IsExcludedFromBudget;
        set { if (CanToggleBudgetExclusion) IsExcludedFromBudget = value; }
    }
    public string DateOrRecurrenceLabel => IsRecurringTransactionMode ? "Recurrence" : "Date";
    public string InstallmentSummaryText => BuildInstallmentSummaryText();
    public bool CanToggleRecurring => !IsRecurringModeLocked && !IsRepayment;
    public bool CanUseHistory => true;
    public bool ShowHistoryPanel => _popupPurpose == TransactionPopupPurpose.AddNewTransaction && IsHistoryOpen;
    public bool HasChildTransactions => ChildTransactions.Count > 0;
    public bool ShowChildTransactionsPanel => (IsViewOnly || IsEditingViewedTransaction) && HasChildTransactions;
    public bool ShowSidePanel => ShowHistoryPanel || ShowChildTransactionsPanel;
    public bool CanEditTransactionName => !IsGoal && !IsRepayment;
    public bool CanEditCategory => IsExpense && !IsRepayment;
    public bool CanEditTags => !IsGoal && !IsRepayment && !HasChildTransactions;
    public bool CanChangeTransactionType => !_isTransactionTypeLocked;
    public bool CanEditViewedTransaction => IsViewOnly && ViewedTransaction?.Tag?.IsSystemTag != true;
    public bool CanCloneViewedTransaction => CanEditViewedTransaction;
    public bool CanSplitViewedTransaction => CanEditViewedTransaction;
    public bool CanDeleteViewedTransaction => IsViewOnly;
    public bool CanPinTransaction => _popupPurpose == TransactionPopupPurpose.AddNewTransaction &&
                                     !IsRecurringTransactionMode &&
                                     !IsRepayment;
    public string IoUTooltip => IsExpense ? "Set as lend" : "Set as debt";

    public string PopupTitle => _popupPurpose switch
    {
        TransactionPopupPurpose.Processing => "Payment Processing",
        TransactionPopupPurpose.ViewTransaction => "Transaction Detail",
        TransactionPopupPurpose.EditTransaction => "Modify Transaction",
        TransactionPopupPurpose.AddRecurringTransaction => "Add Recurring Transaction",
        TransactionPopupPurpose.EditRecurringTransaction => "Edit Recurring Transaction",
        _ => "Add New Transaction"
    };

    public bool IsViewOnly => _popupPurpose == TransactionPopupPurpose.ViewTransaction;
    public bool IsEditingViewedTransaction => _popupPurpose == TransactionPopupPurpose.EditTransaction;
    public bool CanContinue => _popupPurpose is TransactionPopupPurpose.AddNewTransaction or TransactionPopupPurpose.AddRecurringTransaction;
    public bool CanDiscard => _popupPurpose is TransactionPopupPurpose.EditRecurringTransaction or TransactionPopupPurpose.EditTransaction;
    public TransactionVM LoadedTransaction { get; private set; } = null!;
    public TransactionVM PendingTransaction { get; private set; } = null!;
    public TransactionVM? ViewedTransaction { get; private set; }

    public bool ShowNoteField => !IsGoal && !IsRepayment;
    public bool ShowGoalField => IsGoal;
    public bool ShowRepaymentAccountField => IsRepayment;
    public bool ShowCategoryField => IsExpense && !IsExcludedFromBudget;
    public bool ShowCategoryOrRepaymentField => ShowCategoryField || IsRepayment;
    public bool ShouldExpandAccountField => !ShowCategoryOrRepaymentField;
    public bool ShowTransactionModes => !IsRepayment;
    public string CategoryFieldLabel => IsRepayment ? "Credit Account" : "Category";
    public string NameValidationHint => GetValidationHint(nameof(NameText));
    public string AmountValidationHint => GetValidationHint(nameof(AmountText));
    public string AmountWarningHint => _amountWarningHint;
    public string AmountFieldHint => string.IsNullOrWhiteSpace(AmountValidationHint)
        ? AmountWarningHint
        : AmountValidationHint;
    public bool IsAmountWarning => string.IsNullOrWhiteSpace(AmountValidationHint) &&
                                   !string.IsNullOrWhiteSpace(AmountWarningHint);

    public void BeginChangeTracking()
    {
        _initialState = CaptureState();
        _isChangeTrackingInitialized = true;
        NotifyFormStateChanged();
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

        var persistedTags = ProjectNonSystemTags(allTags).ToList();

        if (persistedTags.Count == 0)
            return;

        var selectedTagId = SelectedTag?.Id;

        _orderedTags.Clear();
        _orderedTags.AddRange(persistedTags);

        RefreshTagCollections();
        SelectedTag = selectedTagId is null
            ? _orderedTags.FirstOrDefault()
            : _orderedTags.FirstOrDefault(tag => tag.Id == selectedTagId.Value) ?? _orderedTags.FirstOrDefault();

        if (_popupPurpose is TransactionPopupPurpose.AddNewTransaction or TransactionPopupPurpose.EditTransaction &&
            SelectedTag is { } selectedTag && _orderedTags.FirstOrDefault()?.Id != selectedTag.Id)
            PromoteTagToVisibleStart(selectedTag);
    }

    public bool IsIncome
    {
        get => !IsExpense && !IsGoal && !IsRepayment;
        set
        {
            if (value == IsIncome)
                return;

            if (value)
            {
                var resetExclusion = IsGoal || IsRepayment;
                IsGoal = false;
                IsExpense = false;
                IsRepayment = false;
                if (resetExclusion)
                    IsExcludedFromBudget = false;
            }
            else if (IsIncome)
            {
                IsExpense = true;
            }
        }
    }

    public bool HasMoreTags => OverflowTags.Count > 0;
    public bool IsProcessingSession => ProcessingTargets.Any();
    public bool IsProcessingComplete => IsProcessingSession && _processingStates.Values.All(state => state != ProcessingState.Pending);
    public bool CanSkipProcessing => IsProcessingSession && CurrentProcessingTarget is not null;
    public PopupMode PopupMode => _popupPurpose == TransactionPopupPurpose.Processing
        ? PopupMode.BackNext
        : IsViewOnly ? PopupMode.Functional : PopupMode.SaveDiscard;
    public int CurrentProcessingStep { get; private set; } = 1;
    public int ProcessingStepCount { get; private set; }
    public int? CurrentProcessingRecurringTransactionId => _currentProcessingRecurringTransactionId;

    public void InitializeRepaymentProcessing(IReadOnlyList<AccountVM> accounts)
    {
        InitializeProcessing(accounts);
        _processingRepayments.AddRange(accounts);
        LoadProcessingCurrent();
        SetPopupPurpose(TransactionPopupPurpose.Processing);
        NotifyProcessingChanged();
    }

    public void InitializeGoalProcessing(IReadOnlyList<SavingGoalVM> goals)
    {
        InitializeProcessing(goals);
        _processingGoals.AddRange(goals);
        LoadProcessingCurrent();
        SetPopupPurpose(TransactionPopupPurpose.Processing);
        NotifyProcessingChanged();
    }

    public void InitializeRecurringProcessing(IReadOnlyList<RecurringTransactionVM> recurringTransactions)
    {
        InitializeProcessing(recurringTransactions);
        _processingRecurringTransactions.AddRange(recurringTransactions);
        LoadProcessingCurrent();
        SetPopupPurpose(TransactionPopupPurpose.Processing);
        NotifyProcessingChanged();
    }

    public async Task<TransactionPopupSubmissionResult> SaveCurrentAndAdvanceAsync()
    {
        if (!IsProcessingSession)
            return await SaveAsync(false);

        if (!TryBuildTransactionInput(out _, out var validationMessage))
            return TransactionPopupSubmissionResult.Failure(validationMessage);

        var current = CurrentProcessingTarget!;
        _processingSnapshots[current] = CaptureState();
        _processingStates[current] = ProcessingState.Processed;
        MoveToNextPending();
        NotifyProcessingChanged();
        return TransactionPopupSubmissionResult.Success();
    }

    public void NavigatePreviousProcessing()
    {
        if (!IsProcessingSession || CurrentProcessingTarget is null)
            return;

        _processingSnapshots[CurrentProcessingTarget] = CaptureState();
        var previousIndex = Enumerable.Range(0, _currentProcessingIndex).LastOrDefault(index =>
            _processingStates[ProcessingTargets.ElementAt(index)] == ProcessingState.Processed);
        if (_currentProcessingIndex == 0 || _processingStates[ProcessingTargets.ElementAt(previousIndex)] != ProcessingState.Processed)
            return;

        var previous = ProcessingTargets.ElementAt(previousIndex);
        _processingStates[previous] = ProcessingState.Pending;
        _currentProcessingIndex = previousIndex;
        LoadProcessingCurrent();
        NotifyProcessingChanged();
    }

    public bool SkipCurrentProcessing()
    {
        if (!IsProcessingSession || CurrentProcessingTarget is null)
            return false;

        _processingStates[CurrentProcessingTarget] = ProcessingState.Skipped;
        var hasNext = MoveToNextPending();
        NotifyProcessingChanged();
        return hasNext;
    }

    public async Task<TransactionPopupSubmissionResult> PersistProcessedItemsAsync()
    {
        var processed = ProcessingTargets.Where(target => _processingStates[target] == ProcessingState.Processed).ToList();
        foreach (var target in processed)
        {
            if (!_processingSnapshots.TryGetValue(target, out var snapshot))
                continue;

            _currentProcessingIndex = ProcessingTargets.ToList().IndexOf(target);
            LoadProcessingTarget(target, snapshot);
            var result = await SaveAsync(false);
            if (!result.IsSuccess)
                return result;
        }

        ClearProcessing();
        await _mainViewModel.ReloadCurrentDataAsync(reloadNotifications: true);
        return TransactionPopupSubmissionResult.Success();
    }

    public void InitializeRepayment(AccountVM? target = null)
    {
        ReloadChoicesFromMainViewModel();
        CanChangeRepaymentAccount = target is null;
        IsRepayment = true;
        SelectedRepaymentAccount = target is null
            ? RepaymentAccounts.FirstOrDefault()
            : RepaymentAccounts.FirstOrDefault(account => account.Id == target.Id) ?? target;

        var deductSourceId = SelectedRepaymentAccount?.DeductSource;
        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == deductSourceId) ??
                          Accounts.FirstOrDefault();

        if (target is not null)
            AmountText = target.SpentAmount;
    }

    public bool TryGetRepaymentCorrection(out decimal correctedAmount)
    {
        correctedAmount = SelectedRepaymentAccount?.SpentAmount ?? 0m;
        return IsRepayment && correctedAmount > 0m && AmountText > correctedAmount;
    }

    public void AcceptRepaymentCorrection()
    {
        if (SelectedRepaymentAccount is not null)
            AmountText = SelectedRepaymentAccount.SpentAmount;
        _isRepaymentAmountInvalid = false;
        ValidateProperty(AmountText, nameof(AmountText));
    }

    public void RejectRepaymentCorrection()
    {
        _isRepaymentAmountInvalid = true;
        ValidateProperty(AmountText, nameof(AmountText));
    }

    partial void OnAmountTextChanged(decimal value)
    {
        _isRepaymentAmountInvalid = false;
        RefreshActiveValidation(nameof(AmountText));
        OnPropertyChanged(nameof(InstallmentSummaryText));
        RefreshAmountWarning();
        NotifyFormStateChanged();
    }

    partial void OnIsRecurringChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(RecurringTimeText))
            RecurringTimeText = GetDefaultRecurringTimeText(SelectedRecurringPeriod);

        if (value && IsInstallments)
            IsInstallments = false;

        if (!CanPinTransaction)
            IsPinned = false;
        if (value && IsIoU)
            IsIoU = false;

        if (!CanUseIoU)
            IsIoU = false;

        OnPropertyChanged(nameof(ShowRecurringDayInput));
        OnPropertyChanged(nameof(ShowRecurringNoneInput));
        OnPropertyChanged(nameof(ShowRecurringWeekdayInput));
        OnPropertyChanged(nameof(ShowRecurringMonthlyInput));
        OnPropertyChanged(nameof(ShowDateSelector));
        OnPropertyChanged(nameof(IsRecurringTransactionMode));
        OnPropertyChanged(nameof(ShowInstallmentEndDate));
        OnPropertyChanged(nameof(DateOrRecurrenceLabel));
        OnPropertyChanged(nameof(InstallmentSummaryText));
        OnPropertyChanged(nameof(CanPinTransaction));
        OnPropertyChanged(nameof(CanUseIoU));
        OnPropertyChanged(nameof(IsRegularMode));
        OnPropertyChanged(nameof(TransactionModeDescription));
        RefreshActiveValidation(nameof(AmountText));
        RefreshAmountWarning();
        NotifyFormStateChanged();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTransactionState();
        return Task.CompletedTask;
    }

    partial void OnIsInstallmentsChanged(bool value)
    {
        if (value)
        {
            IsRecurring = false;
            if (IsIoU)
                IsIoU = false;
        }

        if (!CanPinTransaction)
            IsPinned = false;
        if (!CanUseIoU)
            IsIoU = false;

        OnPropertyChanged(nameof(IsRecurringTransactionMode));
        OnPropertyChanged(nameof(ShowRecurringDayInput));
        OnPropertyChanged(nameof(ShowRecurringNoneInput));
        OnPropertyChanged(nameof(ShowRecurringWeekdayInput));
        OnPropertyChanged(nameof(ShowRecurringMonthlyInput));
        OnPropertyChanged(nameof(ShowDateSelector));
        OnPropertyChanged(nameof(DateOrRecurrenceLabel));
        OnPropertyChanged(nameof(ShowInstallmentEndDate));
        OnPropertyChanged(nameof(InstallmentSummaryText));
        OnPropertyChanged(nameof(CanPinTransaction));
        OnPropertyChanged(nameof(CanUseIoU));
        OnPropertyChanged(nameof(IsRegularMode));
        OnPropertyChanged(nameof(TransactionModeDescription));
        RefreshActiveValidation(nameof(AmountText));
        OnPropertyChanged(nameof(IsRegularMode));
        NotifyFormStateChanged();
    }

    partial void OnIsExcludedFromBudgetChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBudgetExcluded));
        NotifyLayoutStateChanged();
        RefreshActiveValidation(nameof(AmountText));
        RefreshAmountWarning();
        _ = RefreshExpenseCategoryAvailabilityAsync();
        NotifyFormStateChanged();
    }

    partial void OnInstallmentEndDateChanged(DateTime value)
    {
        OnPropertyChanged(nameof(InstallmentSummaryText));
        RefreshActiveValidation(nameof(AmountText));
        NotifyFormStateChanged();
    }

    partial void OnSelectedRecurringPeriodChanged(RecurringPeriod value)
    {
        RecurringTimeText = GetDefaultRecurringTimeText(value);
        OnPropertyChanged(nameof(ShowRecurringNoneInput));
        OnPropertyChanged(nameof(ShowRecurringWeekdayInput));
        OnPropertyChanged(nameof(ShowRecurringMonthlyInput));
        OnPropertyChanged(nameof(InstallmentSummaryText));
        RefreshActiveValidation(nameof(AmountText));
        NotifyFormStateChanged();
    }

    partial void OnRecurringTimeTextChanged(string value)
    {
        OnPropertyChanged(nameof(InstallmentSummaryText));
        RefreshActiveValidation(nameof(AmountText));
        NotifyFormStateChanged();
    }

    partial void OnIsRecurringModeLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanToggleRecurring));
        OnPropertyChanged(nameof(CanUseInstallments));
        OnPropertyChanged(nameof(CanUseIoU));
    }

    partial void OnIsIoUChanged(bool value)
    {
        if (!value)
            ShouldAffectBalance = false;

        if (value)
        {
            if (IsRecurring)
                IsRecurring = false;
            if (IsInstallments)
                IsInstallments = false;
        }

        OnPropertyChanged(nameof(IsRegularMode));
        OnPropertyChanged(nameof(IsUnpostedIoUMode));
        OnPropertyChanged(nameof(IsPostedIoUMode));
        OnPropertyChanged(nameof(TransactionModeDescription));
        OnPropertyChanged(nameof(CanToggleBudgetExclusion));
        NotifyFormStateChanged();
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
        OnPropertyChanged(nameof(CanToggleBudgetExclusion));
        NotifyFormStateChanged();
    }

    partial void OnIsPinnedChanged(bool value) => NotifyFormStateChanged();

    partial void OnIsMoreTagsOpenChanged(bool value) => NotifyFormStateChanged();

    partial void OnIsSavingChanged(bool value) => NotifyFormStateChanged();

    partial void OnNameTextChanged(string value)
    {
        RefreshActiveValidation(nameof(NameText));
        NotifyFormStateChanged();
        _ = RefreshTransactionNameSuggestionsAsync();
    }

    partial void OnNoteTextChanged(string value) => NotifyFormStateChanged();

    partial void OnSelectedDateChanged(DateTime value)
    {
        RefreshActiveValidation(nameof(AmountText));
        RefreshAmountWarning();
        NotifyFormStateChanged();
    }

    partial void OnSelectedExpenseCategoryChanged(ExpenseCategory value) => NotifyFormStateChanged();

    partial void OnSelectedGoalChanged(SavingGoalVM? value)
    {
        if (IsGoal)
            SyncGoalUpdateName();

        ResetHistoryLists();
        if (IsHistoryOpen)
            _ = LoadHistoryAsync();

        NotifyFormStateChanged();
    }

    partial void OnSelectedAccountChanged(AccountVM? value)
    {
        RefreshActiveValidation(nameof(AmountText));
        NotifyFormStateChanged();
    }

    public void ValidateNameField()
    {
        _isNameValidationActive = true;
        ValidateProperty(NameText, nameof(NameText));
    }

    public void ValidateAmountField()
    {
        _isAmountValidationActive = true;
        ValidateProperty(AmountText, nameof(AmountText));
    }

    public void ActivateAmountValidation()
    {
        if (_isAmountValidationActive)
            return;

        ValidateAmountField();
    }

    [RelayCommand(CanExecute = nameof(CanToggleRecurring))]
    public void HandleRecurringModeClick()
    {
        if (!CanToggleRecurring)
            return;

        ClearTransactionModes();
        IsRecurring = true;
    }

    [RelayCommand(CanExecute = nameof(CanUseInstallments))]
    public void HandleInstallmentsModeClick()
    {
        if (!CanUseInstallments)
            return;

        ClearTransactionModes();
        IsInstallments = true;
    }

    [RelayCommand(CanExecute = nameof(CanUseIoU))]
    public void HandleIoUModeClick()
    {
        if (!CanUseIoU)
            return;

        ClearTransactionModes();
        IsIoU = true;
    }

    [RelayCommand]
    public void HandleExcludeModeClick()
    {
        ClearTransactionModes();
        IsExcludedFromBudget = true;
    }

    [RelayCommand]
    public void HandleExcludedIoUModeClick()
    {
        ClearTransactionModes();
        IsIoU = true;
        IsExcludedFromBudget = true;
    }

    private void ClearTransactionModes()
    {
        IsRecurring = false;
        IsInstallments = false;
        IsIoU = false;
        IsExcludedFromBudget = false;
    }

    public void InitializeFromDraft(TransactionPopupDraft draft)
    {
        ReloadChoicesFromMainViewModel();

        IsExpense = draft.IsExpense;
        IsGoal = draft.IsGoal;
        AmountText = draft.AmountText;
        NameText = draft.Name;
        NoteText = draft.Note;
        IsIoU = draft.IsIoU;
        ShouldAffectBalance = draft.ShouldAffectBalance;
        IsExcludedFromBudget = draft.IsExcludedFromBudget;
        SelectedDate = draft.Date.Date;
        SelectedExpenseCategory = draft.Category ?? ExpenseCategory.Needs;
        SelectedAccount = draft.AccountId is null
            ? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault(source => source.Id == draft.AccountId.Value) ??
              Accounts.FirstOrDefault();
        SelectedTag = draft.TagId is null
            ? _orderedTags.FirstOrDefault()
            : _orderedTags.FirstOrDefault(tag => tag.Id == draft.TagId.Value) ?? _orderedTags.FirstOrDefault();
        SelectedGoal = draft.GoalId is null
            ? Goals.FirstOrDefault()
            : Goals.FirstOrDefault(goal => goal.Id == draft.GoalId.Value) ?? Goals.FirstOrDefault();
        IsMoreTagsOpen = false;
        if (IsGoal)
            SyncGoalUpdateName();
        _isTransactionTypeLocked = draft.LockTransactionType;
        OnPropertyChanged(nameof(CanChangeTransactionType));
    }

    public void InitializeView(TransactionVM transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        SetTransactionState(transaction);
        var loaded = LoadedTransaction;

        ReloadChoicesFromMainViewModel();
        IsExpense = loaded.Type == TransactionType.Expense;
        IsGoal = loaded.GoalId is not null;
        IsRepayment = loaded.RepaymentAccountId is not null;
        NameText = loaded.Name;
        AmountText = loaded.Amount;
        NoteText = loaded.Notes;
        SelectedDate = loaded.OccurredOn.Date;
        SelectedExpenseCategory = loaded.ExpenseCategory ?? ExpenseCategory.Needs;
        IsPinned = loaded.IsPinned;
        IsIoU = loaded.IsIoU;
        ShouldAffectBalance = loaded.ShouldAffectBalance;
        IsExcludedFromBudget = loaded.IsExcludedFromBudget;
        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == loaded.SourceAccountId) ?? loaded.Account;
        SelectedTag = loaded.Tag;
        ViewedTransaction = loaded;
        _isTransactionTypeLocked = true;
        SetPopupPurpose(TransactionPopupPurpose.ViewTransaction);
        RefreshTagCollections();
        ClearViewModeFeedback();
        OnPropertyChanged(nameof(CanChangeTransactionType));
        OnPropertyChanged(nameof(IsViewOnly));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(PopupMode));
        OnPropertyChanged(nameof(CanEditViewedTransaction));
        OnPropertyChanged(nameof(CanCloneViewedTransaction));
        OnPropertyChanged(nameof(CanSplitViewedTransaction));
    }

    public void SwitchToCloneAddMode()
    {
        EnsureTransactionState();
        PendingTransaction.Id = 0;
        PendingTransaction.LoggedOn = default;
        PendingTransaction.ParentTransactionId = null;
        PendingTransaction.IsForDeletion = false;
        SetPopupPurpose(TransactionPopupPurpose.AddNewTransaction);
        BeginChangeTracking();
    }

    public void InitializeChildTransactions(IEnumerable<TransactionDetailChildTransactionVM> childTransactions)
    {
        ChildTransactions.Clear();
        foreach (var childTransaction in childTransactions)
            ChildTransactions.Add(childTransaction);

        OnPropertyChanged(nameof(HasChildTransactions));
        OnPropertyChanged(nameof(ShowChildTransactionsPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(ShowCategoryImpact));
        OnPropertyChanged(nameof(ShowAccountImpact));
    }

    public async Task BeginEditingViewedTransactionAsync()
    {
        if (ViewedTransaction is null || !CanEditViewedTransaction)
            return;

        SetPopupPurpose(TransactionPopupPurpose.EditTransaction);
        await EnsureTagsLoadedAsync();
        if (SelectedTag is { } selectedTag && _orderedTags.FirstOrDefault()?.Id != selectedTag.Id)
            PromoteTagToVisibleStart(selectedTag);
        else
            RefreshTagCollections();
        BeginChangeTracking();
    }

    public void DiscardEditingViewedTransaction()
    {
        if (ViewedTransaction is not { } transaction)
            return;

        InitializeView(transaction);
        BeginChangeTracking();
    }

    public TransactionEditInput CreateTransactionEditInput()
    {
        if (ViewedTransaction is null || SelectedAccount is null || SelectedTag is null)
            throw new InvalidOperationException("The transaction edit is incomplete.");

        return new TransactionEditInput(
            NameText,
            AmountText,
            IsPinned,
            NoteText,
            SelectedDate,
            SelectedExpenseCategory,
            SelectedAccount.Id,
            SelectedTag.Id,
            IsIoU,
            ShouldAffectBalance,
            IsExcludedFromBudget);
    }

    public TransactionPopupDraft CreateViewedTransactionDraft()
    {
        var transaction = ViewedTransaction ?? throw new InvalidOperationException("No transaction is being viewed.");
        return new TransactionPopupDraft(
            transaction.Type == TransactionType.Expense,
            transaction.Name,
            transaction.Amount,
            transaction.SourceAccountId,
            transaction.OccurredOn,
            transaction.Notes,
            transaction.ExpenseCategory,
            transaction.Tag?.Id,
            transaction.GoalId is not null,
            transaction.GoalId,
            transaction.IsIoU,
            transaction.IsExcludedFromBudget,
            ShouldAffectBalance: transaction.ShouldAffectBalance);
    }

    partial void OnIsExpenseChanged(bool value)
    {
        var resetExclusion = value && (IsGoal || IsRepayment);
        if (value)
        {
            IsGoal = false;
            IsRepayment = false;
            if (resetExclusion)
                IsExcludedFromBudget = false;
        }

        OnPropertyChanged(nameof(IsIncome));
        OnPropertyChanged(nameof(CanUseHistory));
        OnPropertyChanged(nameof(CanEditTransactionName));
        OnPropertyChanged(nameof(CanEditCategory));
        NotifyLayoutStateChanged();
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(ShowNoteField));
        OnPropertyChanged(nameof(ShowGoalField));
        OnPropertyChanged(nameof(CanUseInstallments));
        OnPropertyChanged(nameof(CanUseIoU));
        OnPropertyChanged(nameof(InstallmentSummaryText));
        OnPropertyChanged(nameof(IoUTooltip));
        OnPropertyChanged(nameof(IsBudgetExcluded));
        OnPropertyChanged(nameof(CanToggleBudgetExclusion));

        if (!CanUseIoU)
            IsIoU = false;

        if (!value || IsGoal)
            IsMoreTagsOpen = false;

        RefreshAccounts();
        ClearNameValidation();
        RefreshActiveValidation(nameof(AmountText));
        RefreshAmountWarning();
        _ = RefreshTransactionNameSuggestionsAsync();
        ResetHistoryLists();
        if (IsHistoryOpen)
            _ = LoadHistoryAsync();
        NotifyFormStateChanged();
    }

    partial void OnIsGoalChanged(bool value)
    {
        if (value)
        {
            IsExpense = false;
            IsRepayment = false;
        }
        else if (IsIncome || IsExpense)
        {
            NameText = string.Empty;
        }

        OnPropertyChanged(nameof(IsIncome));
        OnPropertyChanged(nameof(CanUseHistory));
        OnPropertyChanged(nameof(CanEditTransactionName));
        OnPropertyChanged(nameof(CanEditCategory));
        NotifyLayoutStateChanged();
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(ShowNoteField));
        OnPropertyChanged(nameof(ShowGoalField));
        OnPropertyChanged(nameof(CanUseInstallments));
        OnPropertyChanged(nameof(CanUseIoU));
        OnPropertyChanged(nameof(InstallmentSummaryText));
        OnPropertyChanged(nameof(IoUTooltip));
        OnPropertyChanged(nameof(IsBudgetExcluded));
        OnPropertyChanged(nameof(CanToggleBudgetExclusion));

        if (value)
        {
            IsInstallments = false;
            IsIoU = false;
            IsMoreTagsOpen = false;
            SyncGoalUpdateName();
        }

        RefreshAccounts();
        ClearNameValidation();
        RefreshActiveValidation(nameof(AmountText));
        RefreshAmountWarning();
        _ = RefreshTransactionNameSuggestionsAsync();
        ResetHistoryLists();
        if (IsHistoryOpen)
            _ = LoadHistoryAsync();
        NotifyFormStateChanged();
    }

    partial void OnIsRepaymentChanged(bool value)
    {
        if (value)
        {
            IsExpense = false;
            IsGoal = false;
            ClearTransactionModes();
            IsPinned = false;
            SelectedRepaymentAccount ??= RepaymentAccounts.FirstOrDefault();
            LoadRepaymentAmount();
            SyncRepaymentName();
        }
        else if (IsIncome || IsExpense)
        {
            NameText = string.Empty;
        }

        OnPropertyChanged(nameof(IsIncome));
        OnPropertyChanged(nameof(CanToggleRecurring));
        OnPropertyChanged(nameof(CanUseInstallments));
        OnPropertyChanged(nameof(CanUseIoU));
        OnPropertyChanged(nameof(CanPinTransaction));
        OnPropertyChanged(nameof(CanEditTransactionName));
        OnPropertyChanged(nameof(CanEditCategory));
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(ShowNoteField));
        OnPropertyChanged(nameof(ShowRepaymentAccountField));
        NotifyLayoutStateChanged();
        OnPropertyChanged(nameof(CategoryFieldLabel));
        OnPropertyChanged(nameof(IsBudgetExcluded));
        OnPropertyChanged(nameof(CanToggleBudgetExclusion));
        RefreshAccounts();
        NotifyFormStateChanged();
    }

    private void NotifyLayoutStateChanged()
    {
        OnPropertyChanged(nameof(ShowCategoryField));
        OnPropertyChanged(nameof(ShowCategoryOrRepaymentField));
        OnPropertyChanged(nameof(ShouldExpandAccountField));
        OnPropertyChanged(nameof(ShowTransactionModes));
    }

    partial void OnSelectedRepaymentAccountChanged(AccountVM? oldValue, AccountVM? newValue)
    {
        _isRepaymentAmountInvalid = false;
        if (IsRepayment)
            LoadRepaymentAmount();
        if (IsRepayment)
            SyncRepaymentName();
        NotifyFormStateChanged();
    }

    partial void OnSelectedPinnedHistoryItemChanged(AddNewTransactionHistoryItemVM? value)
    {
        if (value is null)
            return;

        SelectedHistoryItem = null;
        ApplyHistoryItem(value);
    }

    partial void OnSelectedHistoryItemChanged(AddNewTransactionHistoryItemVM? value)
    {
        if (value is null)
            return;

        SelectedPinnedHistoryItem = null;
        ApplyHistoryItem(value);
    }

    public void ApplyTransactionNameSuggestion(AddNewTransactionSuggestion suggestion)
    {
        NameText = suggestion.Name;
        AmountText = suggestion.Amount;
        NoteText = suggestion.Note;
        SelectedAccount = Accounts.FirstOrDefault(source => source.Id == suggestion.AccountId) ??
                                 SelectedAccount;

        if (IsExpense)
        {
            SelectedExpenseCategory = suggestion.Category ?? SelectedExpenseCategory;
            SelectedTag = suggestion.TagId is int tagId
                ? _orderedTags.FirstOrDefault(tag => tag.Id == tagId) ?? SelectedTag
                : SelectedTag;
        }

        ClearTransactionNameSuggestions();
        NotifyFormStateChanged();
    }

    partial void OnSelectedTagChanged(TagVM? value)
    {
        if (value is null && IsViewOnly && ViewedTransaction?.Tag is { } viewedTag)
        {
            SelectedTag = viewedTag;
            return;
        }

        RefreshActiveValidation(nameof(AmountText));
        NotifyFormStateChanged();

        if (_isUpdatingTagCollections || value is null)
            return;

        if ((ViewedTransaction is null && _popupPurpose == TransactionPopupPurpose.AddNewTransaction ||
             _popupPurpose == TransactionPopupPurpose.EditTransaction ||
             OverflowTags.Any(tag => tag.Id == value.Id)) &&
            _orderedTags.FirstOrDefault()?.Id != value.Id)
            PromoteTagToVisibleStart(value);

        IsMoreTagsOpen = false;
    }

    public async Task<TransactionPopupSubmissionResult> SaveAsync(bool resetAfterSave)
    {
        if (IsSaving)
            return TransactionPopupSubmissionResult.Failure("A transaction is already being saved.");

        if (!TryBuildTransactionInput(out var input, out var validationMessage))
            return TransactionPopupSubmissionResult.Failure(validationMessage);

        IsSaving = true;

        try
        {
            if (input.IsRecurring && _saveRecurringDraftAsync is not null)
            {
                if (!TryNormalizeRecurringTime(input.RecurringPeriod, input.RecurringTimeText, out var recurringTime))
                    return TransactionPopupSubmissionResult.Failure(GetRecurringTimeValidationMessage(input.RecurringPeriod));

                if (!TryResolveRecurringSaveAmount(input, out var recurringAmount, out var recurringAmountValidationMessage))
                    return TransactionPopupSubmissionResult.Failure(recurringAmountValidationMessage);

                var recurringType = input.IsGoal
                    ? RecurringTransactionType.GoalUpdate
                    : input.IsExpense
                        ? RecurringTransactionType.Expense
                        : RecurringTransactionType.Income;

                var recurringName = input.IsGoal && input.GoalId is not null
                    ? BuildGoalUpdateName((await _appData.GetSavingGoalByIdAsync(input.GoalId.Value))?.Name ?? string.Empty)
                    : input.IsInstallments
                        ? BuildInstallmentRecurringName(input.Name)
                        : BuildExpenseName(input.Name, input.Note, input.IsExpense ? "Recurring Expense" : "Recurring Income");

                var draftSaveResult = await _saveRecurringDraftAsync(new RecurringDraftSaveInput(
                    input.EditingRecurringTransactionId,
                    recurringType,
                    recurringName,
                    recurringAmount,
                    input.RecurringPeriod,
                    recurringTime,
                    input.AccountId,
                    input.IsExpense ? input.Category : null,
                    input.TagId,
                    input.GoalId,
                    input.IsInstallments ? input.InstallmentEndDate : null));
                if (!draftSaveResult.IsSuccess)
                    return draftSaveResult;

                if (resetAfterSave)
                {
                    ReloadChoicesFromMainViewModel();
                    await EnsureTagsLoadedAsync();
                    ResetForm(true);
                }

                var isEdit = input.EditingRecurringTransactionId is > 0;
                FloatingNotificationPublisher.Success(
                    input.Name,
                    isEdit ? "Recurring transaction was updated." : "Recurring transaction was added.",
                    true,
                    isEdit ? "Updated" : "Added");
                return TransactionPopupSubmissionResult.Success();
            }

            var account = await _appData.GetAccountByIdAsync(input.AccountId);
            if (account is null)
                return TransactionPopupSubmissionResult.Failure("Please select a valid account.");

            if (!TryResolveRecurringSaveAmount(input, out var effectiveSaveAmount, out var recurringAmountMessage))
                return TransactionPopupSubmissionResult.Failure(recurringAmountMessage);

            if (!TryValidateSpendingAmountAgainstSource(input.IsExpense || input.IsRepayment, input.IsGoal, effectiveSaveAmount, account, out var spendingValidationMessage))
                return TransactionPopupSubmissionResult.Failure(spendingValidationMessage);

            var invalidationScope = DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications;

            if (input.IsRepayment)
            {
                var target = await _appData.GetAccountByIdAsync(input.RepaymentAccountId!.Value);
                if (target is null || target.AccountType != AccountType.Credit)
                    return TransactionPopupSubmissionResult.Failure("Please select a valid credit account.");

                var tag = await ResolveBalanceUpdateTagAsync();
                if (input.Amount > target.SpentAmount)
                    return TransactionPopupSubmissionResult.Failure("Invalid Repayment");

                var pair = RepaymentTransactionSupport.Create(
                    account,
                    target,
                    input.Amount,
                    input.Date,
                    tag,
                    input.Name);
                await _appData.AddTransactionAsync(pair.Expense);
                await _appData.AddTransactionAsync(pair.Income);
                _appData.UpdateAccount(account);
                _appData.UpdateAccount(target);
                await _appData.SaveChangesAsync();
                WeakReferenceMessenger.Default.Send(new RecordLogMemoryMessage(
                    new CompositeLogMemoryAction(
                        "Repayment",
                        [
                            new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(pair.Expense)),
                            new AddTransactionMemoryAction(TransactionMemorySnapshot.Create(pair.Income))
                        ])));
            }
            else if (input.IsRecurring)
            {
                if (!TryNormalizeRecurringTime(input.RecurringPeriod, input.RecurringTimeText, out var recurringTime))
                    return TransactionPopupSubmissionResult.Failure(GetRecurringTimeValidationMessage(input.RecurringPeriod));

                var recurringType = input.IsGoal
                    ? RecurringTransactionType.GoalUpdate
                    : input.IsExpense
                        ? RecurringTransactionType.Expense
                        : RecurringTransactionType.Income;

                RecurringTransaction recurring;
                if (input.EditingRecurringTransactionId is > 0)
                {
                    recurring = await _appData.GetRecurringTransactionByIdAsync(input.EditingRecurringTransactionId.Value) ?? new RecurringTransaction();
                }
                else
                {
                    recurring = new RecurringTransaction();
                }

                recurring.Name = input.IsGoal && input.GoalId is not null
                    ? BuildGoalUpdateName((await _appData.GetSavingGoalByIdAsync(input.GoalId.Value))?.Name ?? string.Empty)
                    : input.IsInstallments
                        ? BuildInstallmentRecurringName(input.Name)
                        : BuildExpenseName(input.Name, input.Note, input.IsExpense ? "Recurring Expense" : "Recurring Income");
                recurring.Amount = effectiveSaveAmount;
                recurring.RecurringPeriod = input.RecurringPeriod;
                recurring.RecurringTime = recurringTime;
                recurring.Type = recurringType;
                recurring.Category = input.IsExpense ? input.Category : null;
                recurring.SourceId = input.AccountId;
                recurring.TagId = input.IsGoal ? null : input.TagId;
                recurring.GoalId = input.IsGoal ? input.GoalId : null;
                recurring.IsExcludedFromBudget = input.IsEffectivelyExcludedFromBudget;
                recurring.IsEnabled = true;
                recurring.EndDate = input.IsInstallments ? input.InstallmentEndDate : null;

                if (input.EditingRecurringTransactionId is > 0)
                    _appData.UpdateRecurringTransaction(recurring);
                else
                    await _appData.AddRecurringTransactionAsync(recurring);

                await _appData.SaveChangesAsync();
                if (input.EditingRecurringTransactionId is not > 0)
                    WeakReferenceMessenger.Default.Send(new NotificationEntityCreatedMessage(NotificationEntityKind.RecurringTransaction, recurring.Id));
            }
            else if (input.IsGoal)
            {
                if (input.GoalId is null)
                    return TransactionPopupSubmissionResult.Failure("Please choose a goal.");

                if (!GoalUpdateTransactionSupport.IsEligibleGoalSourceType(account.AccountType))
                    return TransactionPopupSubmissionResult.Failure("Goal updates can only be taken from Cash or Checking.");

                var goal = await _appData.GetSavingGoalByIdAsync(input.GoalId.Value);
                if (goal is null)
                    return TransactionPopupSubmissionResult.Failure("Please select a valid goal.");

                if (!input.IsEffectivelyExcludedFromBudget)
                {
                    var budgetPolicyResult = await ApplyExpenseBudgetPolicyAsync(
                        ExpenseCategory.Savings,
                        input.Amount,
                        input.Date);
                    if (!budgetPolicyResult.IsSuccess)
                        return budgetPolicyResult;
                }

                var goalUpdateTag = await GoalUpdateTransactionSupport.ResolveGoalUpdateTagAsync(_appData);
                var transaction = new Transaction
                {
                    Type = TransactionType.Expense,
                    Name = BuildGoalUpdateName(goal.Name),
                    Amount = input.Amount,
                    OccurredOn = input.Date,
                    Notes = $"Goal update for {goal.Name}",
                    ExpenseCategory = ExpenseCategory.Savings,
                    SourceAccountId = account.Id,
                    GoalId = goal.Id,
                    TagId = goalUpdateTag.Id,
                    IsPinned = false,
                    IsExcludedFromBudget = input.IsEffectivelyExcludedFromBudget
                    ,RelatedRecurringTransactionId = input.RelatedRecurringTransactionId
                };

                goal.CurrentAmount += input.Amount;

                await _appData.AddTransactionAsync(transaction);
                _appData.UpdateSavingGoal(goal);

                ApplyExpenseToAccount(account, input.Amount);
                _appData.UpdateAccount(account);

                await _appData.SaveChangesAsync();
                WeakReferenceMessenger.Default.Send(
                    new RecordLogMemoryMessage(new AddTransactionMemoryAction(
                        TransactionMemorySnapshot.Create(transaction))));

                invalidationScope |= DashboardDataInvalidationScope.SavingGoals;
            }
            else if (input.IsExpense)
            {
                var tag = await _appData.GetTagByIdAsync(input.TagId!.Value);
                if (tag is null)
                    return TransactionPopupSubmissionResult.Failure("Please select a valid tag.");

                if (!input.IsEffectivelyExcludedFromBudget)
                {
                    var budgetPolicyResult = await ApplyExpenseBudgetPolicyAsync(input.Category!.Value, input.Amount, input.Date);
                    if (!budgetPolicyResult.IsSuccess)
                        return budgetPolicyResult;
                }

                var transaction = new Transaction
                {
                    Type = TransactionType.Expense,
                    Name = BuildExpenseName(input.Name, input.Note, tag.Name),
                    Amount = input.Amount,
                    OccurredOn = input.Date,
                    Notes = input.Note,
                    ExpenseCategory = input.Category!.Value,
                    SourceAccountId = account.Id,
                    TagId = tag.Id,
                    IsPinned = input.IsPinned,
                    IsIoU = input.IsIoU,
                    ShouldAffectBalance = input.ShouldAffectBalance,
                    IsExcludedFromBudget = input.IsEffectivelyExcludedFromBudget
                    ,RelatedRecurringTransactionId = input.RelatedRecurringTransactionId
                };

                await _appData.AddTransactionAsync(transaction);

                if (transaction.AffectsAccountBalance)
                {
                    ApplyExpenseToAccount(account, input.Amount);
                    _appData.UpdateAccount(account);
                }

                await _appData.SaveChangesAsync();
                WeakReferenceMessenger.Default.Send(
                    new RecordLogMemoryMessage(new AddTransactionMemoryAction(
                        TransactionMemorySnapshot.Create(transaction))));
            }
            else
            {
                var transaction = new Transaction
                {
                    Type = TransactionType.Income,
                    Name = input.Name,
                    Amount = input.Amount,
                    OccurredOn = input.Date,
                    Notes = input.Note,
                    SourceAccountId = account.Id,
                    TagId = input.TagId,
                    IsPinned = input.IsPinned,
                    IsIoU = input.IsIoU,
                    ShouldAffectBalance = input.ShouldAffectBalance,
                    IsExcludedFromBudget = input.IsEffectivelyExcludedFromBudget
                };

                await _appData.AddTransactionAsync(transaction);

                if (transaction.AffectsAccountBalance)
                {
                    ApplyIncomeToAccount(account, input.Amount);
                    _appData.UpdateAccount(account);
                }

                await _appData.SaveChangesAsync();
                WeakReferenceMessenger.Default.Send(
                    new RecordLogMemoryMessage(new AddTransactionMemoryAction(
                        TransactionMemorySnapshot.Create(transaction))));
            }

            if (IsProcessingSession)
                invalidationScope &= ~DashboardDataInvalidationScope.Notifications;

            WeakReferenceMessenger.Default.Send(new DashboardDataInvalidatedMessage(invalidationScope));

            await _mainViewModel.ReloadCurrentDataAsync(reloadNotifications: !IsProcessingSession);

            if (resetAfterSave)
            {
                ReloadChoicesFromMainViewModel();
                await EnsureTagsLoadedAsync();
                ResetForm(true);
            }

            var savedType = input.IsGoal ? "Goal contribution" : input.IsExpense ? "Expense" : "Income";
            FloatingNotificationPublisher.Success(
                input.Name, $"{savedType} was recorded.", true, "Added");
            return TransactionPopupSubmissionResult.Success();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception, "save transaction");
            return TransactionPopupSubmissionResult.Failure(string.Empty);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task<bool> HasSimilarTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBuildTransactionInput(out var input, out _) || input.IsRecurring)
            return false;

        try
        {
            var candidateName = await ResolveSimilarTransactionNameAsync(input, cancellationToken);
            if (string.IsNullOrWhiteSpace(candidateName))
                return false;

            var transactions = (await _appData.GetTransactionsAsync(cancellationToken))
                .Where(transaction => transaction.OccurredOn.Date == input.Date.Date);

            if (!input.IsExpense && !input.IsGoal)
            {
                var incomeLogs = transactions.Where(transaction => transaction.Type == TransactionType.Income);
                return incomeLogs.Any(log => IsSimilarIncomeTransaction(log, input, candidateName));
            }

            var expenseLogs = transactions.Where(transaction => transaction.Type == TransactionType.Expense);
            var goalUpdateTagIds = await GetGoalUpdateTagIdsAsync(cancellationToken);
            return expenseLogs.Any(log => IsSimilarExpenseTransaction(
                log,
                input,
                candidateName,
                input.IsGoal,
                goalUpdateTagIds));
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception,
                "check for similar transactions");
            return false;
        }
    }

    public bool HasValidEntryToPersistOnClose()
    {
        return TryBuildTransactionInput(out _, out _);
    }

    public void ResetForm(bool keepCurrentType)
    {
        if (!keepCurrentType)
        {
            IsExpense = true;
            IsGoal = false;
            IsRepayment = false;
        }

        AmountText = 0m;
        NameText = string.Empty;
        NoteText = string.Empty;
        SelectedDate = DateTime.Today;
        StartDate = DateTime.Today;
        InstallmentEndDate = DateTime.Today;
        IsInstallments = false;
        IsRecurring = false;
        IsPinned = false;
        IsIoU = false;
        ShouldAffectBalance = false;
        IsExcludedFromBudget = false;
        IsRecurringModeLocked = false;
        CanChangeRepaymentAccount = true;
        _isTransactionTypeLocked = false;
        SetPopupPurpose(TransactionPopupPurpose.AddNewTransaction);
        OnPropertyChanged(nameof(CanChangeTransactionType));
        SelectedRecurringPeriod = RecurringPeriod.Monthly;
        RecurringTimeText = GetDefaultRecurringTimeText(SelectedRecurringPeriod);
        SelectedExpenseCategory = ExpenseCategory.Needs;
        SelectedAccount = Accounts.FirstOrDefault(account => account.IsDefault) ?? Accounts.FirstOrDefault();
        SelectedTag = _orderedTags.FirstOrDefault();
        SelectedGoal = Goals.FirstOrDefault();
        SelectedRepaymentAccount = RepaymentAccounts.FirstOrDefault();
        IsMoreTagsOpen = false;
        ClearTransactionNameSuggestions();
    }

    public void SetVisibleTagSlots(int visibleTagSlots)
    {
        var normalizedSlots = Math.Max(0, visibleTagSlots);
        if (_visibleTagSlots == normalizedSlots)
            return;

        _visibleTagSlots = normalizedSlots;
        RefreshTagCollections();
    }

    private bool TryBuildTransactionInput(out QuickTransactionInput input, out string validationMessage)
    {
        input = default;
        validationMessage = string.Empty;

        _isNameValidationActive = true;
        _isAmountValidationActive = true;
        ValidateAllProperties();
        if (HasErrors)
        {
            validationMessage = GetFirstValidationMessage();
            return false;
        }

        ExpenseCategory? category = null;
        int? tagId = null;
        int? goalId = null;

        if (IsGoal)
        {
            if (SelectedGoal is null)
            {
                validationMessage = "Please choose a goal.";
                return false;
            }

            goalId = SelectedGoal.Id;
        }
        else if (!IsGoal && !IsRepayment)
        {
            if (SelectedTag is null)
            {
                validationMessage = "Please choose a tag.";
                return false;
            }

            category = IsExpense ? SelectedExpenseCategory : null;
            tagId = SelectedTag.Id;
        }

        if (SelectedAccount is null)
        {
            validationMessage = "Please choose a account.";
            return false;
        }

        if (IsRepayment && SelectedRepaymentAccount is null)
        {
            validationMessage = "Please choose a credit account.";
            return false;
        }

        if (IsInstallments && !TryResolveInstallmentCount(
                SelectedRecurringPeriod,
                RecurringTimeText,
                InstallmentEndDate,
                StartDate,
                out _,
                out validationMessage))
        {
            return false;
        }

        input = new QuickTransactionInput(
            IsExpense,
            IsGoal,
            IsRepayment,
            !IsRepayment && IsRecurringTransactionMode,
            !IsRepayment && IsInstallments,
            !IsRepayment && IsPinned,
            !IsRepayment && IsIoU,
            !IsRepayment && ShouldAffectBalance,
            IsBudgetExcluded,
            _editingRecurringTransactionId,
            SelectedRecurringPeriod,
            NameText.Trim(),
            AmountText,
            SelectedAccount.Id,
            SelectedDate.Date.Add(DateTime.Now.TimeOfDay),
            InstallmentEndDate.Date,
            RecurringTimeText.Trim(),
            NoteText.Trim(),
            category,
            tagId,
            goalId,
            SelectedRepaymentAccount?.Id,
            _currentProcessingRecurringTransactionId);

        return true;
    }

    private void ReloadChoicesFromMainViewModel()
    {
        _availableAccounts.Clear();
        var sourceCatalog = _accountsOverride ?? _mainViewModel.BudgetPanel.Accounts;
        _availableAccounts.AddRange(sourceCatalog.Where(source => source.IsEnabled));
        ReplaceCollection(
            RepaymentAccounts,
            _availableAccounts
                .Where(source => source.AccountType == AccountType.Credit)
                .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase));

        _orderedTags.Clear();
        _orderedTags.AddRange(OrderNonSystemTags(_mainViewModel.BudgetPanel.Tags
            .Concat(_mainViewModel.BudgetPanel.OtherTags)
            .GroupBy(tag => tag.Id)
            .Select(group => group.First())));

        _orderedGoals.Clear();
        _orderedGoals.AddRange(_mainViewModel.SavingGoalsPanel.SavingGoals
            .GroupBy(goal => goal.Id)
            .Select(group => group.First())
            .OrderBy(goal => goal.Name));

        ReplaceCollection(Goals, _orderedGoals);
        RefreshTagCollections();
        RefreshAccounts();
        _ = RefreshExpenseCategoryAvailabilityAsync();
    }

    private async Task RefreshExpenseCategoryAvailabilityAsync()
    {
        if (IsExcludedFromBudget)
        {
            SetAllExpenseCategoriesEnabled(true);
            return;
        }

        try
        {
            var allocation = await _appData.GetBudgetAllocationAsync();
            if (allocation.OverspendPolicy != OverspendPolicy.HardStop)
            {
                SetAllExpenseCategoriesEnabled(true);
                return;
            }

            var snapshot = await BuildBudgetAllocationSnapshotAsync(allocation, DateTime.Today);
            foreach (var option in ExpenseCategories)
                option.IsEnabled = GetCategoryState(snapshot, option.Value).Remaining > 0m;
        }
        catch
        {
            SetAllExpenseCategoriesEnabled(true);
        }
    }

    private async Task<TransactionPopupSubmissionResult> ApplyExpenseBudgetPolicyAsync(
        ExpenseCategory category,
        decimal amount,
        DateTime expenseDate)
    {
        var allocation = await _appData.GetBudgetAllocationAsync();
        if (allocation.OverspendPolicy == OverspendPolicy.Ignore)
            return TransactionPopupSubmissionResult.Success();

        var snapshot = await BuildBudgetAllocationSnapshotAsync(allocation, expenseDate);
        var categoryState = GetCategoryState(snapshot, category);

        if (allocation.OverspendPolicy == OverspendPolicy.HardStop &&
            BudgetAllocationCalculator.WouldHardStop(categoryState, amount))
        {
            return TransactionPopupSubmissionResult.Failure(
                $"{GetExpenseCategoryLabel(category)} budget is exhausted for this allocation period.");
        }

        if (allocation.OverspendPolicy == OverspendPolicy.SoftDebt)
        {
            var debtDelta = BudgetAllocationCalculator.CalculateSoftDebtDelta(categoryState.Remaining, amount);
            if (debtDelta > 0m)
            {
                AddDebtDelta(allocation, category, debtDelta);
                _appData.UpdateBudgetAllocation(allocation);
            }
        }

        return TransactionPopupSubmissionResult.Success();
    }

    private async Task<BudgetAllocationSnapshot> BuildBudgetAllocationSnapshotAsync(
        BudgetAllocation allocation,
        DateTime allocationDate)
    {
        var expenseLogs = (await _appData.GetTransactionsAsync())
            .Where(transaction => transaction.Type == TransactionType.Expense);
        var currentPeriod = BudgetAllocationCalculator.ResolveCurrentPeriod(
            allocation.AllocationPeriod,
            allocationDate,
            allocation.PeriodStart);
        var previousPeriod = BudgetAllocationCalculator.ResolvePreviousPeriod(
            allocation.AllocationPeriod,
            allocationDate,
            allocation.PeriodStart);

        return BudgetAllocationCalculator.CalculateSnapshot(
            allocation,
            CalculateSpentByCategory(expenseLogs, currentPeriod),
            CalculateSpentByCategory(expenseLogs, previousPeriod),
            allocationDate,
            _mainViewModel.BudgetPanel.TotalIncomeAmount);
    }

    private static IReadOnlyDictionary<ExpenseCategory, decimal> CalculateSpentByCategory(
        IEnumerable<Transaction> expenseLogs,
        BudgetAllocationPeriod period)
    {
        return expenseLogs
            .Where(log => !log.IsForDeletion)
            .Where(log => !log.IsExcludedFromBudget)
            .Where(log => log.OccurredOn.Date >= period.Start && log.OccurredOn.Date <= period.End)
            .Where(log => log.ExpenseCategory.HasValue)
            .GroupBy(log => log.ExpenseCategory!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));
    }

    private static BudgetAllocationCategoryState GetCategoryState(
        BudgetAllocationSnapshot snapshot,
        ExpenseCategory category)
    {
        return category switch
        {
            ExpenseCategory.Wants => snapshot.Wants,
            ExpenseCategory.Savings => snapshot.Invest,
            _ => snapshot.Needs
        };
    }

    private static void AddDebtDelta(BudgetAllocation allocation, ExpenseCategory category, decimal debtDelta)
    {
        switch (category)
        {
            case ExpenseCategory.Wants:
                allocation.WantsDebt += debtDelta;
                break;

            case ExpenseCategory.Savings:
                allocation.InvestDebt += debtDelta;
                break;

            default:
                allocation.NeedsDebt += debtDelta;
                break;
        }
    }

    private static string GetExpenseCategoryLabel(ExpenseCategory category)
    {
        return category switch
        {
            ExpenseCategory.Wants => "Wants",
            ExpenseCategory.Savings => "Invest",
            _ => "Needs"
        };
    }

    private void SetAllExpenseCategoriesEnabled(bool isEnabled)
    {
        foreach (var option in ExpenseCategories)
            option.IsEnabled = isEnabled;
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
            if (IsViewOnly)
            {
                ReplaceCollection(VisibleTags, SelectedTag is null ? [] : [SelectedTag]);
                ReplaceCollection(OverflowTags, []);
                OnPropertyChanged(nameof(HasMoreTags));
                IsMoreTagsOpen = false;
                return;
            }

            ReplaceCollection(VisibleTags, _orderedTags.Take(_visibleTagSlots));
            ReplaceCollection(OverflowTags, _orderedTags.Skip(_visibleTagSlots));

            OnPropertyChanged(nameof(HasMoreTags));
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

    private static string BuildExpenseName(string name, string note, string fallbackName)
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

    private static string BuildGoalUpdateName(string goalName)
    {
        var trimmedGoalName = goalName.Trim();
        return string.IsNullOrWhiteSpace(trimmedGoalName)
            ? GoalUpdateTransactionSupport.GoalUpdateTagName
            : $"{GoalUpdateTransactionSupport.GoalUpdateTagName}: {trimmedGoalName}";
    }

    private static string BuildGoalUpdateDisplayName(string goalName)
    {
        var trimmedGoalName = goalName.Trim();
        return string.IsNullOrWhiteSpace(trimmedGoalName)
            ? GoalUpdateTransactionSupport.GoalUpdateTagName
            : $"{GoalUpdateTransactionSupport.GoalUpdateTagName} for {trimmedGoalName}";
    }

    private async Task<string> ResolveSimilarTransactionNameAsync(
        QuickTransactionInput input,
        CancellationToken cancellationToken)
    {
        if (input.IsGoal)
        {
            if (input.GoalId is null)
                return string.Empty;

            var goal = await _appData.GetSavingGoalByIdAsync(input.GoalId.Value, cancellationToken);
            return goal is null ? string.Empty : BuildGoalUpdateName(goal.Name);
        }

        return input.Name.Trim();
    }

    private async Task<HashSet<int>> GetGoalUpdateTagIdsAsync(CancellationToken cancellationToken)
    {
        var tags = await _appData.GetTagsAsync(cancellationToken);
        return tags
            .Where(tag => string.Equals(
                tag.Name?.Trim(),
                GoalUpdateTransactionSupport.GoalUpdateTagName,
                StringComparison.OrdinalIgnoreCase))
            .Select(tag => tag.Id)
            .ToHashSet();
    }

    private static bool IsSimilarExpenseTransaction(
        Transaction log,
        QuickTransactionInput input,
        string candidateName,
        bool candidateIsGoalUpdate,
        IReadOnlySet<int> goalUpdateTagIds)
    {
        if (log.IsForDeletion ||
            log.SourceAccountId != input.AccountId ||
            log.Type != TransactionType.Expense ||
            !IsSameTransactionName(log.Name, candidateName) ||
            !IsSimilarAmount(log.Amount, input.Amount))
        {
            return false;
        }

        return IsGoalUpdateExpenseLog(log, goalUpdateTagIds) == candidateIsGoalUpdate;
    }

    private static bool IsSimilarIncomeTransaction(
        Transaction log,
        QuickTransactionInput input,
        string candidateName)
    {
        return log.Type == TransactionType.Income && log.SourceAccountId == input.AccountId &&
               IsSameTransactionName(log.Name, candidateName) &&
               IsSimilarAmount(log.Amount, input.Amount);
    }

    private static bool IsGoalUpdateExpenseLog(Transaction log, IReadOnlySet<int> goalUpdateTagIds)
    {
        var tagName = log.Tag?.Name;
        if (!string.IsNullOrWhiteSpace(tagName))
            return string.Equals(
                tagName.Trim(),
                GoalUpdateTransactionSupport.GoalUpdateTagName,
                StringComparison.OrdinalIgnoreCase);

        if (log.TagId is > 0 && goalUpdateTagIds.Count > 0)
            return goalUpdateTagIds.Contains(log.TagId.Value);

        var expenseName = log.Name.Trim();
        return string.Equals(expenseName, GoalUpdateTransactionSupport.GoalUpdateTagName, StringComparison.OrdinalIgnoreCase) ||
               expenseName?.StartsWith($"{GoalUpdateTransactionSupport.GoalUpdateTagName}:", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSameTransactionName(string? existingName, string candidateName)
    {
        return string.Equals(
            existingName?.Trim(),
            candidateName.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSimilarAmount(decimal existingAmount, decimal candidateAmount)
    {
        existingAmount = Math.Abs(existingAmount);
        candidateAmount = Math.Abs(candidateAmount);

        if (existingAmount <= 0m || candidateAmount <= 0m)
            return existingAmount == candidateAmount;

        var lowerAmount = Math.Min(existingAmount, candidateAmount);
        var higherAmount = Math.Max(existingAmount, candidateAmount);
        return (higherAmount - lowerAmount) / lowerAmount <= SimilarAmountTolerance;
    }

    private async Task RefreshTransactionNameSuggestionsAsync()
    {
        var requestVersion = ++_transactionNameSuggestionRequestVersion;
        var query = NameText?.Trim() ?? string.Empty;
        if (query.Length < 3 || IsGoal)
        {
            ClearTransactionNameSuggestions();
            return;
        }

        try
        {
            var transactions = await _appData.GetTransactionsAsync();

            if (requestVersion != _transactionNameSuggestionRequestVersion)
                return;

            var suggestions = BuildTransactionNameSuggestions(
                transactions.Where(transaction => transaction.Type == TransactionType.Expense),
                transactions.Where(transaction => transaction.Type == TransactionType.Income),
                IsExpense,
                query);
            ReplaceCollection(TransactionNameSuggestions, suggestions);
            OnPropertyChanged(nameof(HasTransactionNameSuggestions));
        }
        catch
        {
            if (requestVersion == _transactionNameSuggestionRequestVersion)
                ClearTransactionNameSuggestions();
        }
    }

    private void ClearTransactionNameSuggestions()
    {
        _transactionNameSuggestionRequestVersion++;

        if (TransactionNameSuggestions.Count == 0)
        {
            OnPropertyChanged(nameof(HasTransactionNameSuggestions));
            return;
        }

        TransactionNameSuggestions.Clear();
        OnPropertyChanged(nameof(HasTransactionNameSuggestions));
    }

    internal static IEnumerable<AddNewTransactionSuggestion> BuildTransactionNameSuggestions(
        IEnumerable<Transaction> expenseLogs,
        IEnumerable<Transaction> incomeLogs,
        bool isExpense,
        string query)
    {
        var normalizedQuery = query.Trim();
        if (normalizedQuery.Length < 3)
            return [];

        return isExpense
            ? BuildExpenseTransactionNameSuggestions(expenseLogs, normalizedQuery)
            : BuildIncomeTransactionNameSuggestions(incomeLogs, normalizedQuery);
    }

    private static IEnumerable<AddNewTransactionSuggestion> BuildExpenseTransactionNameSuggestions(
        IEnumerable<Transaction> expenseLogs,
        string query)
    {
        return expenseLogs
            .Where(log => !log.IsForDeletion)
            .Where(log => log.Type == TransactionType.Expense && !string.IsNullOrWhiteSpace(log.Name))
            .Where(log => log.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .Select(log => new AddNewTransactionSuggestion(
                log.Name,
                log.Amount,
                log.SourceAccountId,
                log.Account?.Name ?? string.Empty,
                log.Notes,
                log.ExpenseCategory,
                log.TagId,
                null));
    }

    private static IEnumerable<AddNewTransactionSuggestion> BuildIncomeTransactionNameSuggestions(
        IEnumerable<Transaction> incomeLogs,
        string query)
    {
        return incomeLogs
            .Where(log => log.Type == TransactionType.Income && !string.IsNullOrWhiteSpace(log.Name))
            .Where(log => log.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .Select(log => new AddNewTransactionSuggestion(
                log.Name,
                log.Amount,
                log.SourceAccountId,
                log.Account?.Name ?? string.Empty,
                log.Notes,
                null,
                null,
                null));
    }

    private static bool TryValidateSpendingAmountAgainstSource(
        bool isExpense,
        bool isGoal,
        decimal amount,
        AccountVM source,
        out string validationMessage)
    {
        if (amount <= 0m)
        {
            validationMessage = "Please enter a valid amount greater than zero.";
            return false;
        }

        if (!isExpense && !isGoal)
        {
            validationMessage = string.Empty;
            return true;
        }

        if (!TryValidateMaximumSpending(source.MaximumSpending, source.AccountType, source.SpentAmount, source.MoneyOut, amount, out validationMessage))
            return false;

        return TryValidateSpendingCapacity(source.AccountType, source.Balance, source.AccountLimit, source.SpentAmount, amount, out validationMessage);
    }

    private static bool TryValidateSpendingAmountAgainstSource(
        bool isExpense,
        bool isGoal,
        decimal amount,
        Account source,
        out string validationMessage)
    {
        if (amount <= 0m)
        {
            validationMessage = "Please enter a valid amount greater than zero.";
            return false;
        }

        if (!isExpense && !isGoal)
        {
            validationMessage = string.Empty;
            return true;
        }

        var persistedMoneyOut = GetPersistedMoneyOut(source);
        if (!TryValidateMaximumSpending(source.MaximumSpending, source.AccountType, source.SpentAmount, persistedMoneyOut, amount, out validationMessage))
            return false;

        return TryValidateSpendingCapacity(source.AccountType, source.Balance, source.AccountLimit, source.SpentAmount, amount, out validationMessage);
    }

    private static decimal GetPersistedMoneyOut(Account source)
    {
        var moneyOutProperty = source.GetType().GetProperty("MoneyOut");
        if (moneyOutProperty is not null)
        {
            var rawValue = moneyOutProperty.GetValue(source);
            if (rawValue is decimal moneyOut)
                return moneyOut;
        }

        return source.SpentAmount;
    }

    private static bool TryValidateMaximumSpending(
        decimal maximumSpending,
        AccountType sourceType,
        decimal spentAmount,
        decimal moneyOut,
        decimal amount,
        out string validationMessage)
    {
        if (maximumSpending <= 0m)
        {
            validationMessage = string.Empty;
            return true;
        }

        var projectedSpending = sourceType == AccountType.Credit
            ? spentAmount + amount
            : moneyOut + amount;

        if (projectedSpending <= maximumSpending)
        {
            validationMessage = string.Empty;
            return true;
        }

        validationMessage = "Amount exceeds this source's maximum spending limit.";
        return false;
    }

    private static bool TryValidateSpendingCapacity(
        AccountType sourceType,
        decimal balance,
        decimal accountLimit,
        decimal spentAmount,
        decimal amount,
        out string validationMessage)
    {
        if (sourceType == AccountType.Credit)
        {
            if (spentAmount + amount <= accountLimit)
            {
                validationMessage = string.Empty;
                return true;
            }

            validationMessage = "Amount exceeds this source's account limit.";
            return false;
        }

        if (amount <= balance)
        {
            validationMessage = string.Empty;
            return true;
        }

        validationMessage = "Amount exceeds this source's available balance.";
        return false;
    }

    private static void ApplyExpenseToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount += amount;
            return;
        }

        account.Balance -= amount;
    }

    private static void ApplyIncomeToAccount(Account account, decimal amount)
    {
        if (account.AccountType == AccountType.Credit)
        {
            account.SpentAmount = Math.Max(0m, account.SpentAmount - amount);
            return;
        }

        account.Balance += amount;
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();

        foreach (var item in items)
            target.Add(item);
    }

    private string GetFirstValidationMessage()
    {
        var messages = new[]
                 {
                     nameof(NameText),
                     nameof(AmountText),
                     nameof(SelectedAccount),
                     nameof(SelectedGoal),
                     nameof(SelectedTag),
                     nameof(RecurringTimeText)
                 }
            .SelectMany(propertyName => GetErrors(propertyName)
                .OfType<ValidationResult>()
                .Select(result => result.ErrorMessage))
            .Concat(GetErrors().OfType<ValidationResult>().Select(result => result.ErrorMessage))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return messages.Length == 0
            ? "Please fix the highlighted fields."
            : string.Join(Environment.NewLine, messages!);
    }

    private FormState CaptureState()
    {
        return new FormState(
            IsExpense,
            IsGoal,
            IsRepayment,
            IsRecurring,
            IsInstallments,
            IsPinned,
            IsIoU,
            ShouldAffectBalance,
            IsExcludedFromBudget,
            SelectedRecurringPeriod,
            NameText ?? string.Empty,
            AmountText,
            RecurringTimeText ?? string.Empty,
            NoteText ?? string.Empty,
            SelectedDate.Date,
            InstallmentEndDate.Date,
            SelectedExpenseCategory,
            SelectedAccount?.Id ?? NoAccountId,
            SelectedTag?.Id ?? NoTagId,
            SelectedGoal?.Id ?? NoSavingGoalId,
            SelectedRepaymentAccount?.Id ?? NoAccountId);
    }

    private void NotifyFormStateChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ShowCategoryImpact));
        OnPropertyChanged(nameof(ShowAccountImpact));
        OnPropertyChanged(nameof(CategoryCurrent));
        OnPropertyChanged(nameof(CategoryToBe));
        OnPropertyChanged(nameof(AccountCurrent));
        OnPropertyChanged(nameof(AccountToBe));
        OnPropertyChanged(nameof(TransactionWarnings));
        OnPropertyChanged(nameof(IsNeedsCategory));
        OnPropertyChanged(nameof(IsWantsCategory));
        OnPropertyChanged(nameof(IsInvestCategory));
    }

    private decimal GetCategoryCurrentAmount()
    {
        try
        {
            var allocation = _appData.GetBudgetAllocationAsync().GetAwaiter().GetResult();
            return GetCategoryState(BuildBudgetAllocationSnapshotAsync(allocation, SelectedDate).GetAwaiter().GetResult(), SelectedExpenseCategory).Spent;
        }
        catch { return 0m; }
    }

    private IReadOnlyList<TransactionWarning> BuildTransactionWarnings() => GetErrors()
        .OfType<ValidationResult>()
        .Select(result => result.ErrorMessage)
        .Where(message => !string.IsNullOrWhiteSpace(message))
        .Distinct(StringComparer.Ordinal)
        .Select(message => new TransactionWarning(message!, false))
        .ToArray();

    private void RefreshAmountWarning()
    {
        var warning = GetDailyAllowanceWarning();
        if (_amountWarningHint != warning)
        {
            _amountWarningHint = warning;
            OnPropertyChanged(nameof(AmountWarningHint));
        }

        NotifyAmountPresentationChanged();
    }

    private void NotifyAmountPresentationChanged()
    {
        OnPropertyChanged(nameof(AmountFieldHint));
        OnPropertyChanged(nameof(IsAmountWarning));
    }

    private string GetDailyAllowanceWarning()
    {
        if (!IsExpense || IsRecurring || IsExcludedFromBudget || AmountText <= 0m)
            return string.Empty;

        try
        {
            var allocation = _appData.GetBudgetAllocationAsync().GetAwaiter().GetResult();
            var spent = BudgetEffectiveTransactionFilter
                .Select(_appData.GetTransactionsAsync().GetAwaiter().GetResult())
                .Where(transaction => transaction.Type == TransactionType.Expense &&
                                      transaction.OccurredOn.Date == SelectedDate.Date)
                .Sum(transaction => transaction.Amount);
            var allowance = BudgetAllocationCalculator.CalculateDailyAllowance(
                allocation,
                SelectedDate.Date,
                _mainViewModel.BudgetPanel.TotalIncomeAmount);

            return spent + AmountText > allowance ? "Over Daily Allowance" : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool HasPendingTransactionInputChanges()
    {
        var initialInput = new PendingTransactionInputState(
            IsGoal || IsRepayment ? string.Empty : _initialState.NameText ?? string.Empty,
            _initialState.AmountText);
        var currentInput = new PendingTransactionInputState(
            IsGoal || IsRepayment ? string.Empty : NameText ?? string.Empty,
            AmountText);

        return HasPendingTransactionInputValue(currentInput) && !currentInput.Equals(initialInput);
    }

    private static bool HasPendingTransactionInputValue(PendingTransactionInputState state)
    {
        return !string.IsNullOrWhiteSpace(state.NameText) || state.AmountText > 0m;
    }

    private void EnsureTransactionState()
    {
        if (!_isTransactionStateInitialized)
            SetTransactionState(new TransactionVM());
    }

    private void SetTransactionState(TransactionVM source)
    {
        LoadedTransaction = TransactionMappingHelper.CreateLoaded(source);
        PendingTransaction = TransactionMappingHelper.CreatePending(LoadedTransaction);
        _isTransactionStateInitialized = true;
    }

    [RelayCommand]
    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUseHistory)
        {
            ResetHistoryLists();
            return;
        }

        try
        {
            if (IsGoal)
            {
                var expenseLogs = await _appData.GetTransactionsAsync(cancellationToken);
                PinnedHistory.Reset([]);
                TransactionHistory.Reset(SelectedGoal is null
                    ? []
                    : AddNewTransactionHistoryBuilder.BuildGoalUpdateHistory(expenseLogs, SelectedGoal.Name));
                return;
            }

            if (IsExpense)
            {
                var expenseLogs = await _appData.GetTransactionsAsync(cancellationToken);
                PinnedHistory.Reset(AddNewTransactionHistoryBuilder.BuildPinnedExpenses(expenseLogs));
                TransactionHistory.Reset(AddNewTransactionHistoryBuilder.BuildExpenseHistory(expenseLogs));
                return;
            }

            var incomeLogs = await _appData.GetTransactionsAsync(cancellationToken);
            PinnedHistory.Reset(AddNewTransactionHistoryBuilder.BuildPinnedIncomes(incomeLogs));
            TransactionHistory.Reset(AddNewTransactionHistoryBuilder.BuildIncomeHistory(incomeLogs));
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(WeakReferenceMessenger.Default, exception,
                "load transaction history");
            ResetHistoryLists();
        }
    }

    private void ResetHistoryLists()
    {
        SelectedPinnedHistoryItem = null;
        SelectedHistoryItem = null;
        PinnedHistory.Reset([]);
        TransactionHistory.Reset([]);
    }

    private void ApplyHistoryItem(AddNewTransactionHistoryItemVM item)
    {
        if (item.IsGoalUpdate)
            IsGoal = true;
        else
        {
            IsExpense = item.IsExpense;
            IsGoal = false;
        }

        NameText = item.Name;
        AmountText = item.Amount;
        NoteText = item.Note;
        SelectedDate = item.Date.Date;
        IsPinned = item.IsPinned;
        SelectedAccount = Accounts.FirstOrDefault(source => source.Id == item.AccountId) ??
                                 SelectedAccount;

        if (item.IsExpense)
        {
            SelectedExpenseCategory = item.Category ?? SelectedExpenseCategory;
            SelectedTag = item.TagId is int tagId
                ? _orderedTags.FirstOrDefault(tag => tag.Id == tagId) ?? SelectedTag
                : SelectedTag;
        }

        ClearTransactionNameSuggestions();
        NotifyFormStateChanged();
    }

    private bool IsCurrentInputValid()
    {
        return IsValidationSuccess(ValidateNameText(NameText, CreateValidationContext()))
               && IsValidationSuccess(ValidateAmountText(AmountText, CreateValidationContext()))
               && IsValidationSuccess(ValidateSelectedAccount(SelectedAccount, CreateValidationContext()))
               && IsValidationSuccess(ValidateSelectedTag(SelectedTag, CreateValidationContext()))
               && IsValidationSuccess(ValidateSelectedGoal(SelectedGoal, CreateValidationContext()))
               && IsValidationSuccess(ValidateRecurringTimeText(RecurringTimeText, CreateValidationContext()))
               && IsInstallmentInputValid();
    }

    private ValidationContext CreateValidationContext()
    {
        return new ValidationContext(this);
    }

    private static bool IsValidationSuccess(ValidationResult? result)
    {
        return result is null || result == ValidationResult.Success;
    }

    private void RefreshActiveValidation(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (propertyName == nameof(NameText) && _isNameValidationActive)
                ValidateProperty(NameText, nameof(NameText));

            if (propertyName == nameof(AmountText) && _isAmountValidationActive)
                ValidateProperty(AmountText, nameof(AmountText));
        }
    }

    private void ClearNameValidation()
    {
        _isNameValidationActive = false;
        ClearErrors(nameof(NameText));
        OnPropertyChanged(nameof(NameValidationHint));
        OnPropertyChanged(nameof(CanSave));
    }

    private void ClearViewModeFeedback()
    {
        _isNameValidationActive = false;
        _isAmountValidationActive = false;
        ClearErrors(nameof(NameText));
        ClearErrors(nameof(AmountText));
        ClearErrors(nameof(SelectedAccount));
        ClearErrors(nameof(SelectedTag));
        ClearErrors(nameof(SelectedGoal));
        ClearErrors(nameof(RecurringTimeText));
        _amountWarningHint = string.Empty;
        OnPropertyChanged(nameof(NameValidationHint));
        OnPropertyChanged(nameof(AmountValidationHint));
        OnPropertyChanged(nameof(AmountWarningHint));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(TransactionWarnings));
        NotifyAmountPresentationChanged();
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
            nameof(AmountText) => GetAmountValidationHint(message),
            _ => string.Empty
        };
    }

    private static string GetNameValidationHint(string message)
    {
        if (message.Contains("enter a name", StringComparison.OrdinalIgnoreCase))
            return "Required";

        if (message.Contains("exceed", StringComparison.OrdinalIgnoreCase))
            return "Too Long";

        return "Invalid Name";
    }

    private static string GetAmountValidationHint(string message)
    {
        if (message.Contains("Invalid Repayment", StringComparison.OrdinalIgnoreCase))
            return "Invalid Repayment";

        if (message.Contains("available balance", StringComparison.OrdinalIgnoreCase))
            return "Insufficient Balance";

        if (message.Contains("maximum spending", StringComparison.OrdinalIgnoreCase))
            return "Overflowing Balance";

        if (message.Contains("account limit", StringComparison.OrdinalIgnoreCase))
            return "Account Limit";

        if (message.Contains("spending limit", StringComparison.OrdinalIgnoreCase))
            return "Tag Limit";

        return "Invalid Amount";
    }

    private bool IsInstallmentInputValid()
    {
        return !IsInstallments
               || TryResolveInstallmentCount(
                   SelectedRecurringPeriod,
                   RecurringTimeText,
                   InstallmentEndDate,
                   StartDate,
                   out _,
                   out _);
    }

    private void RefreshAccounts()
    {
        var selectedAccountId = SelectedAccount?.Id;

        var filteredSources = _availableAccounts
            .Where(source =>
                IsRepayment
                    ? source.AccountType == AccountType.Checking
                    : IsGoal
                    ? GoalUpdateTransactionSupport.IsEligibleGoalSourceType(source.AccountType)
                    : IsExpense || source.AccountType != AccountType.Credit)
            .OrderBy(GetAccountTypeSortOrder)
            .ThenByDescending(GetAccountWithinTypeSortValue)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ReplaceCollection(Accounts, filteredSources);

        SelectedAccount = selectedAccountId is null
            ? Accounts.FirstOrDefault(account => account.IsDefault) ?? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault(source => source.Id == selectedAccountId.Value) ??
              Accounts.FirstOrDefault(account => account.IsDefault) ?? Accounts.FirstOrDefault();
    }

    public static ValidationResult? ValidateNameText(string value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        if (viewModel.IsGoal)
            return ValidationResult.Success;

        var trimmedName = value?.Trim() ?? string.Empty;

        if (trimmedName.Length == 0)
            return new ValidationResult("Please enter a name.");

        if (trimmedName.Length > MaxNameLength)
            return new ValidationResult($"Name cannot exceed {MaxNameLength} characters.");

        if (trimmedName.Any(char.IsControl))
            return new ValidationResult("Name cannot contain control characters.");

        return ValidationResult.Success;
    }

    public static ValidationResult? ValidateAmountText(decimal value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        if (viewModel._isRepaymentAmountInvalid)
            return new ValidationResult("Invalid Repayment");

        if (value <= 0m)
            return new ValidationResult("Please enter a valid amount greater than zero.");

        if (viewModel.SelectedAccount is null)
            return ValidationResult.Success;

        var amountToValidate = value;
        if (viewModel.IsInstallments)
        {
            if (!TryResolveInstallmentCount(
                viewModel.SelectedRecurringPeriod,
                viewModel.RecurringTimeText,
                viewModel.InstallmentEndDate,
                viewModel.StartDate,
                out var installmentCount,
                out _))
            {
                return ValidationResult.Success;
            }

            amountToValidate = CalculateInstallmentAmount(value, installmentCount);
        }

        if (!TryValidateSpendingAmountAgainstSource(viewModel.IsExpense || viewModel.IsRepayment, viewModel.IsGoal, amountToValidate, viewModel.SelectedAccount, out var validationMessage))
            return new ValidationResult(validationMessage);

        if (!viewModel.TryValidateSpendingAmountAgainstTagLimit(amountToValidate, out var tagLimitValidationMessage))
            return new ValidationResult(tagLimitValidationMessage);

        return ValidationResult.Success;
    }

    private bool TryValidateSpendingAmountAgainstTagLimit(decimal amount, out string validationMessage)
    {
        validationMessage = string.Empty;

        if (!IsExpense || IsRecurring || IsExcludedFromBudget || SelectedTag is not { SpendingLimit: > 0m } tag)
            return true;

        try
        {
            var allocation = _appData.GetBudgetAllocationAsync().GetAwaiter().GetResult();
            var currentPeriod = BudgetAllocationCalculator.ResolveCurrentPeriod(
                allocation.AllocationPeriod,
                SelectedDate.Date,
                allocation.PeriodStart);
            var currentTagSpending = _appData.GetTransactionsAsync().GetAwaiter().GetResult()
                .Where(log => log.Type == TransactionType.Expense && !log.IsForDeletion && !log.IsExcludedFromBudget)
                .Where(log => log.OccurredOn.Date >= currentPeriod.Start && log.OccurredOn.Date <= currentPeriod.End)
                .Where(log => log.TagId == tag.Id || log.Tag?.Id == tag.Id)
                .Sum(log => log.Amount);

            if (currentTagSpending + amount <= tag.SpendingLimit.Value)
                return true;

            validationMessage = $"{tag.Name} spending limit exceeded.";
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static ValidationResult? ValidateSelectedAccount(AccountVM? value, ValidationContext validationContext)
    {
        _ = validationContext;
        return value is null
            ? new ValidationResult("Please choose a account.")
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateSelectedTag(TagVM? value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        if (!viewModel.IsExpense)
            return ValidationResult.Success;

        return value is null
            ? new ValidationResult("Please choose a tag.")
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateSelectedGoal(SavingGoalVM? value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        if (!viewModel.IsGoal)
            return ValidationResult.Success;

        return value is null
            ? new ValidationResult("Please choose a goal.")
            : ValidationResult.Success;
    }

    public static ValidationResult? ValidateRecurringTimeText(string value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        if (!viewModel.IsRecurringTransactionMode || viewModel.SelectedRecurringPeriod == RecurringPeriod.None)
            return ValidationResult.Success;

        return TryNormalizeRecurringTime(viewModel.SelectedRecurringPeriod, value?.Trim() ?? string.Empty, out _)
            ? ValidationResult.Success
            : new ValidationResult(GetRecurringTimeValidationMessage(viewModel.SelectedRecurringPeriod));
    }

    internal static IEnumerable<TagVM> ProjectNonSystemTags(IEnumerable<Tag> tags)
    {
        return tags
            .Where(tag => !tag.IsSystemTag)
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .Select(tag => new TagVM
            {
                Id = tag.Id,
                Name = tag.Name,
                HexCode = tag.HexCode,
                IsSystemTag = false,
                SpendingLimit = tag.SpendingLimit
            });
    }

    internal static IEnumerable<TagVM> OrderNonSystemTags(IEnumerable<TagVM> tags)
    {
        return tags
            .Where(tag => !tag.IsSystemTag)
            .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
    }

    internal static int GetAccountTypeSortOrder(AccountVM source)
    {
        return source.AccountType switch
        {
            AccountType.Checking => 0,
            AccountType.Cash => 1,
            AccountType.Credit => 2,
            AccountType.Saving => 3,
            _ => 5
        };
    }

    internal static decimal GetAccountWithinTypeSortValue(AccountVM source)
    {
        return source.AccountType switch
        {
            AccountType.Checking or AccountType.Cash => source.Balance,
            AccountType.Credit => source.AccountLimit - source.SpentAmount,
            AccountType.Saving => source.Balance,
            _ => source.Balance
        };
    }

    public sealed partial class ExpenseCategoryOption : ObservableObject
    {
        public ExpenseCategoryOption(string label, ExpenseCategory value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public ExpenseCategory Value { get; }

        [ObservableProperty]
        private bool _isEnabled = true;
    }

    public sealed record TransactionWarning(string Message, bool IsWarning);

    public sealed record AddNewTransactionSuggestion(
        string Name,
        decimal Amount,
        int AccountId,
        string AccountName,
        string Note,
        ExpenseCategory? Category,
        int? TagId,
        DateTime? Date);

    public readonly record struct TransactionEditInput(
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

    public readonly record struct TransactionPopupSubmissionResult(bool IsSuccess, string? ErrorMessage)
    {
        public static TransactionPopupSubmissionResult Success()
        {
            return new TransactionPopupSubmissionResult(true, null);
        }

        public static TransactionPopupSubmissionResult Failure(string? errorMessage)
        {
            return new TransactionPopupSubmissionResult(false, errorMessage);
        }
    }

    public readonly record struct TransactionPopupDraft(
        bool IsExpense,
        string Name,
        decimal AmountText,
        int? AccountId,
        DateTime Date,
        string Note,
        ExpenseCategory? Category,
        int? TagId,
        bool IsGoal = false,
        int? GoalId = null,
        bool IsIoU = false,
        bool IsExcludedFromBudget = false,
        bool LockTransactionType = false,
        bool ShouldAffectBalance = false);

    public readonly record struct RecurringDraftSaveInput(
        int? EditingRecurringTransactionId,
        RecurringTransactionType Type,
        string Name,
        decimal Amount,
        RecurringPeriod RecurringPeriod,
        int RecurringTime,
        int AccountId,
        ExpenseCategory? Category,
        int? TagId,
        int? GoalId,
        DateTime? EndDate);

    public readonly record struct RecurringDraftSnapshot(
        int? EditingRecurringTransactionId,
        RecurringTransactionType Type,
        string Name,
        decimal Amount,
        RecurringPeriod RecurringPeriod,
        int RecurringTime,
        int AccountId,
        ExpenseCategory? Category,
        int? TagId,
        int? GoalId);

    private readonly record struct QuickTransactionInput(
        bool IsExpense,
        bool IsGoal,
        bool IsRepayment,
        bool IsRecurring,
        bool IsInstallments,
        bool IsPinned,
        bool IsIoU,
        bool ShouldAffectBalance,
        bool IsExcludedFromBudget,
        int? EditingRecurringTransactionId,
        RecurringPeriod RecurringPeriod,
        string Name,
        decimal Amount,
        int AccountId,
        DateTime Date,
        DateTime InstallmentEndDate,
        string RecurringTimeText,
        string Note,
        ExpenseCategory? Category,
        int? TagId,
        int? GoalId,
        int? RepaymentAccountId,
        int? RelatedRecurringTransactionId)
    {
        public bool IsEffectivelyExcludedFromBudget =>
            IsExcludedFromBudget || IsIoU || (!IsExpense && !IsGoal && !IsRepayment);
    }

    private enum ProcessingState { Pending, Processed, Skipped }

    private IEnumerable<object> ProcessingTargets => _processingRepayments.Cast<object>()
        .Concat(_processingGoals).Concat(_processingRecurringTransactions);

    private object? CurrentProcessingTarget => ProcessingTargets.ElementAtOrDefault(_currentProcessingIndex);

    private void InitializeProcessing<T>(IReadOnlyList<T> targets) where T : class
    {
        _processingRepayments.Clear();
        _processingGoals.Clear();
        _processingRecurringTransactions.Clear();
        _processingStates.Clear();
        _processingSnapshots.Clear();
        _currentProcessingIndex = 0;
        foreach (var target in targets)
            _processingStates[target] = ProcessingState.Pending;
        ProcessingStepCount = targets.Count;
        CurrentProcessingStep = targets.Count == 0 ? 0 : 1;
    }

    private bool MoveToNextPending()
    {
        var targets = ProcessingTargets.ToList();
        for (var index = _currentProcessingIndex + 1; index < targets.Count; index++)
        {
            if (_processingStates[targets[index]] != ProcessingState.Pending)
                continue;

            _currentProcessingIndex = index;
            LoadProcessingCurrent();
            return true;
        }

        return false;
    }

    private void LoadProcessingCurrent()
    {
        if (CurrentProcessingTarget is not { } target)
            return;

        _currentProcessingRecurringTransactionId = null;
        if (_processingSnapshots.TryGetValue(target, out var snapshot))
        {
            LoadProcessingTarget(target, snapshot);
            return;
        }

        if (target is AccountVM account)
            InitializeRepayment(account);
        else if (target is SavingGoalVM goal)
        {
            ResetForm(false);
            IsGoal = true;
            SelectedGoal = goal;
            SyncGoalUpdateName();
        }
        else if (target is RecurringTransactionVM recurring)
        {
            _currentProcessingRecurringTransactionId = recurring.Id;
            ResetForm(false); IsRecurring = false; IsExpense = recurring.Type != RecurringTransactionType.Income;
            NameText = recurring.Name; AmountText = recurring.Amount; SelectedExpenseCategory = recurring.Category ?? ExpenseCategory.Needs;
            SelectedAccount = Accounts.FirstOrDefault(account => account.Id == recurring.Source.Id);
            SelectedTag = recurring.Tag; SelectedDate = DateTime.Today;
            IsExcludedFromBudget = recurring.IsExcludedFromBudget;
        }

        SetPopupPurpose(TransactionPopupPurpose.Processing);
    }

    private void LoadProcessingTarget(object target, FormState snapshot)
    {
        _currentProcessingRecurringTransactionId = (target as RecurringTransactionVM)?.Id;
        ResetForm(false);
        IsExpense = snapshot.IsExpense;
        IsGoal = snapshot.IsGoal;
        IsRepayment = snapshot.IsRepayment;
        IsRecurring = snapshot.IsRecurring;
        IsInstallments = snapshot.IsInstallments;
        IsPinned = snapshot.IsPinned;
        IsIoU = snapshot.IsIoU;
        ShouldAffectBalance = snapshot.ShouldAffectBalance;
        IsExcludedFromBudget = snapshot.IsExcludedFromBudget;
        SelectedRecurringPeriod = snapshot.SelectedRecurringPeriod;
        NameText = snapshot.NameText;
        AmountText = snapshot.AmountText;
        RecurringTimeText = snapshot.RecurringTimeText;
        NoteText = snapshot.NoteText;
        SelectedDate = snapshot.SelectedDate;
        InstallmentEndDate = snapshot.InstallmentEndDate;
        SelectedExpenseCategory = snapshot.SelectedExpenseCategory;
        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == snapshot.SelectedAccountId);
        SelectedTag = _orderedTags.FirstOrDefault(tag => tag.Id == snapshot.SelectedTagId);
        SelectedGoal = Goals.FirstOrDefault(goal => goal.Id == snapshot.SelectedGoalId);
        SelectedRepaymentAccount = RepaymentAccounts.FirstOrDefault(account => account.Id == snapshot.SelectedRepaymentAccountId);
        SetPopupPurpose(TransactionPopupPurpose.Processing);
    }

    private void ClearProcessing()
    {
        _processingRepayments.Clear();
        _processingGoals.Clear();
        _processingRecurringTransactions.Clear();
        _processingStates.Clear();
        _processingSnapshots.Clear();
        _currentProcessingIndex = 0;
        _currentProcessingRecurringTransactionId = null;
        ProcessingStepCount = 0;
        CurrentProcessingStep = 0;
        SetPopupPurpose(TransactionPopupPurpose.AddNewTransaction);
        NotifyProcessingChanged();
    }

    private void NotifyProcessingChanged()
    {
        var navigableTargets = ProcessingTargets.Where(target => _processingStates[target] != ProcessingState.Skipped).ToList();
        ProcessingStepCount = navigableTargets.Count;
        CurrentProcessingStep = CurrentProcessingTarget is { } current
            ? Math.Max(1, navigableTargets.IndexOf(current) + 1)
            : 0;
        OnPropertyChanged(nameof(IsProcessingSession)); OnPropertyChanged(nameof(CurrentProcessingStep));
        OnPropertyChanged(nameof(ProcessingStepCount)); OnPropertyChanged(nameof(CurrentProcessingRecurringTransactionId)); OnPropertyChanged(nameof(PopupMode));
        OnPropertyChanged(nameof(CanSkipProcessing)); OnPropertyChanged(nameof(IsProcessingComplete)); OnPropertyChanged(nameof(PopupTitle));
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
    }

    private readonly record struct FormState(
        bool IsExpense,
        bool IsGoal,
        bool IsRepayment,
        bool IsRecurring,
        bool IsInstallments,
        bool IsPinned,
        bool IsIoU,
        bool ShouldAffectBalance,
        bool IsExcludedFromBudget,
        RecurringPeriod SelectedRecurringPeriod,
        string NameText,
        decimal AmountText,
        string RecurringTimeText,
        string NoteText,
        DateTime SelectedDate,
        DateTime InstallmentEndDate,
        ExpenseCategory SelectedExpenseCategory,
        int SelectedAccountId,
        int SelectedTagId,
        int SelectedGoalId,
        int SelectedRepaymentAccountId);

    private readonly record struct PendingTransactionInputState(
        string NameText,
        decimal AmountText);

    private int? _editingRecurringTransactionId;

    public void InitializeRecurringMode(bool isLocked)
    {
        SetPopupPurpose(TransactionPopupPurpose.AddRecurringTransaction);
        _isTransactionTypeLocked = isLocked;
        OnPropertyChanged(nameof(CanChangeTransactionType));
        IsRecurringModeLocked = isLocked;
        IsInstallments = false;
        IsRecurring = true;
    }

    public async Task<bool> InitializeFromRecurringTransactionAsync(int recurringTransactionId, CancellationToken cancellationToken = default)
    {
        var recurring = await _appData.GetRecurringTransactionByIdAsync(recurringTransactionId, cancellationToken);
        if (recurring is null)
            return false;

        _editingRecurringTransactionId = recurring.Id;
        SetPopupPurpose(TransactionPopupPurpose.EditRecurringTransaction);
        _isTransactionTypeLocked = true;
        OnPropertyChanged(nameof(CanChangeTransactionType));
        IsRecurringModeLocked = true;
        IsInstallments = false;
        IsRecurring = true;
        IsExpense = recurring.Type == RecurringTransactionType.Expense;
        IsGoal = recurring.Type == RecurringTransactionType.GoalUpdate;
        NameText = recurring.Name;
        AmountText = recurring.Amount;
        SelectedExpenseCategory = recurring.Category ?? ExpenseCategory.Needs;
        SelectedRecurringPeriod = recurring.RecurringPeriod;
        RecurringTimeText = recurring.RecurringPeriod == RecurringPeriod.None
            ? string.Empty
            : recurring.RecurringTime.ToString(CultureInfo.InvariantCulture);
        SelectedAccount = Accounts.FirstOrDefault(source => source.Id == recurring.SourceId) ?? Accounts.FirstOrDefault();
        IsExcludedFromBudget = recurring.IsExcludedFromBudget;
        SelectedTag = recurring.TagId is > 0 ? _orderedTags.FirstOrDefault(tag => tag.Id == recurring.TagId.Value) : _orderedTags.FirstOrDefault();
        SelectedGoal = recurring.GoalId is > 0 ? Goals.FirstOrDefault(goal => goal.Id == recurring.GoalId.Value) : Goals.FirstOrDefault();
        return true;
    }

    public void InitializeFromRecurringDraft(RecurringDraftSnapshot draft)
    {
        _editingRecurringTransactionId = draft.EditingRecurringTransactionId;
        SetPopupPurpose(draft.EditingRecurringTransactionId is > 0
            ? TransactionPopupPurpose.EditRecurringTransaction
            : TransactionPopupPurpose.AddRecurringTransaction);
        _isTransactionTypeLocked = true;
        OnPropertyChanged(nameof(CanChangeTransactionType));
        IsRecurringModeLocked = true;
        IsInstallments = false;
        IsRecurring = true;
        IsExpense = draft.Type == RecurringTransactionType.Expense;
        IsGoal = draft.Type == RecurringTransactionType.GoalUpdate;
        NameText = draft.Name;
        AmountText = draft.Amount;
        SelectedExpenseCategory = draft.Category ?? ExpenseCategory.Needs;
        SelectedRecurringPeriod = draft.RecurringPeriod;
        RecurringTimeText = draft.RecurringPeriod == RecurringPeriod.None
            ? string.Empty
            : draft.RecurringTime.ToString(CultureInfo.InvariantCulture);
        SelectedAccount = Accounts.FirstOrDefault(source => source.Id == draft.AccountId) ?? Accounts.FirstOrDefault();
        SelectedTag = draft.TagId is > 0 ? _orderedTags.FirstOrDefault(tag => tag.Id == draft.TagId.Value) : _orderedTags.FirstOrDefault();
        SelectedGoal = draft.GoalId is > 0 ? Goals.FirstOrDefault(goal => goal.Id == draft.GoalId.Value) : Goals.FirstOrDefault();
    }

    internal static bool TryNormalizeRecurringTime(RecurringPeriod period, string text, out int recurringTime)
    {
        recurringTime = 0;
        if (period == RecurringPeriod.None)
            return true;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return false;

        var max = IsWeekdayRecurringPeriod(period) ? 7 : MonthlyDueDateHelper.MaxMonthlyDay;
        if (parsed < 1 || parsed > max)
            return false;

        recurringTime = parsed;
        return true;
    }

    private static bool IsWeekdayRecurringPeriod(RecurringPeriod period)
    {
        return period is RecurringPeriod.Weekly or RecurringPeriod.Biweekly;
    }

    private static string GetDefaultRecurringTimeText(RecurringPeriod period)
    {
        if (period == RecurringPeriod.None)
            return string.Empty;

        if (IsWeekdayRecurringPeriod(period))
        {
            var day = DateTime.Today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)DateTime.Today.DayOfWeek;
            return day.ToString(CultureInfo.InvariantCulture);
        }

        return MonthlyDueDateHelper.Normalize(DateTime.Today.Day)?.ToString(CultureInfo.InvariantCulture) ?? "1";
    }

    private string BuildInstallmentSummaryText()
    {
        if (!IsInstallments)
            return string.Empty;

        if (!TryResolveInstallmentCount(
                SelectedRecurringPeriod,
                RecurringTimeText,
                InstallmentEndDate,
                StartDate,
                out var count,
                out _))
            return string.Empty;

        var installmentAmount = CalculateInstallmentAmount(AmountText, count);
        var amountText = MoneyFormatUtility.ToFullText(installmentAmount, CultureInfo.CurrentCulture);
        var recurrenceLabel = FormatRecurringScheduleLabel(SelectedRecurringPeriod, RecurringTimeText);
        var verb = IsExpense ? "paid" : "earned";
        return $"The installment will be {amountText}, {verb} every {recurrenceLabel}";
    }

    private bool TryResolveRecurringSaveAmount(
        QuickTransactionInput input,
        out decimal amount,
        out string validationMessage)
    {
        amount = input.Amount;
        validationMessage = string.Empty;

        if (!input.IsInstallments)
            return true;

        if (!TryResolveInstallmentCount(
                input.RecurringPeriod,
                input.RecurringTimeText,
                input.InstallmentEndDate,
                StartDate,
                out var count,
                out validationMessage))
            return false;

        amount = CalculateInstallmentAmount(input.Amount, count);
        return true;
    }

    private static decimal CalculateInstallmentAmount(decimal totalAmount, int count)
    {
        return decimal.Round(totalAmount / count, 2, MidpointRounding.AwayFromZero);
    }

    private static string BuildInstallmentRecurringName(string name)
    {
        return $"Installments for {name.Trim()}";
    }

    private static bool TryResolveInstallmentCount(
        RecurringPeriod period,
        string recurringTimeText,
        DateTime endDate,
        DateTime today,
        out int count,
        out string validationMessage)
    {
        count = 0;
        validationMessage = string.Empty;

        if (period == RecurringPeriod.None)
        {
            validationMessage = "Installments need a weekly, biweekly, or monthly recurrence.";
            return false;
        }

        if (!TryNormalizeRecurringTime(period, recurringTimeText?.Trim() ?? string.Empty, out var recurringTime))
        {
            validationMessage = GetRecurringTimeValidationMessage(period);
            return false;
        }

        today = today.Date;
        endDate = endDate.Date;
        if (endDate < today)
        {
            validationMessage = "Installment end date must be today or later.";
            return false;
        }

        var occurrence = FindClosestOccurrence(today, period, recurringTime);
        while (occurrence <= endDate)
        {
            count++;
            occurrence = AddOccurrence(occurrence, period);
        }

        if (count > 0)
            return true;

        validationMessage = "Installment end date must include at least one recurrence.";
        return false;
    }

    private static DateTime FindClosestOccurrence(DateTime today, RecurringPeriod period, int recurringTime)
    {
        if (period == RecurringPeriod.Monthly)
        {
            var candidate = new DateTime(today.Year, today.Month, recurringTime);
            var previous = candidate <= today ? candidate : candidate.AddMonths(-1);
            var next = candidate >= today ? candidate : candidate.AddMonths(1);
            return IsCloserToToday(next, previous, today) ? next : previous;
        }

        var previousDate = today;
        while (GetIsoDayOfWeek(previousDate.DayOfWeek) != recurringTime)
            previousDate = previousDate.AddDays(-1);

        var nextDate = today;
        while (GetIsoDayOfWeek(nextDate.DayOfWeek) != recurringTime)
            nextDate = nextDate.AddDays(1);

        return IsCloserToToday(nextDate, previousDate, today) ? nextDate : previousDate;
    }

    private static bool IsCloserToToday(DateTime candidate, DateTime comparison, DateTime today)
    {
        return Math.Abs((candidate.Date - today.Date).TotalDays)
               < Math.Abs((comparison.Date - today.Date).TotalDays);
    }

    private static DateTime AddOccurrence(DateTime occurrence, RecurringPeriod period)
    {
        return period switch
        {
            RecurringPeriod.Weekly => occurrence.AddDays(7),
            RecurringPeriod.Biweekly => occurrence.AddDays(14),
            RecurringPeriod.Monthly => occurrence.AddMonths(1),
            _ => occurrence
        };
    }

    private static string FormatRecurringScheduleLabel(RecurringPeriod period, string recurringTimeText)
    {
        if (!TryNormalizeRecurringTime(period, recurringTimeText?.Trim() ?? string.Empty, out var recurringTime))
            return string.Empty;

        if (period == RecurringPeriod.Monthly)
            return FormatOrdinal(recurringTime);

        var option = recurringTime is >= 1 and <= 7
            ? CultureInfo.CurrentCulture.DateTimeFormat.DayNames[recurringTime % 7]
            : string.Empty;
        return option;
    }

    private static string FormatOrdinal(int value)
    {
        var suffix = (value % 100) is 11 or 12 or 13
            ? "th"
            : (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };

        return value.ToString(CultureInfo.InvariantCulture) + suffix;
    }

    private static int GetIsoDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
    }

    private static string GetRecurringTimeValidationMessage(RecurringPeriod period)
    {
        return IsWeekdayRecurringPeriod(period)
            ? "Recurring weekday must be between Monday and Sunday."
            : "Recurring day must be between 1 and 28.";
    }

    public sealed record RecurringTimeOption(string Label, string Value);

    private enum TransactionPopupPurpose
    {
        AddNewTransaction,
        AddRecurringTransaction,
        EditRecurringTransaction,
        ViewTransaction,
        EditTransaction,
        Processing
    }

    private void SetPopupPurpose(TransactionPopupPurpose purpose)
    {
        if (_popupPurpose == purpose)
            return;

        _popupPurpose = purpose;
        if (!CanPinTransaction)
            IsPinned = false;

        OnPropertyChanged(nameof(PopupTitle));
        OnPropertyChanged(nameof(CanPinTransaction));
        OnPropertyChanged(nameof(IsViewOnly));
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(CanDiscard));
        OnPropertyChanged(nameof(PopupMode));
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(HasChildTransactions));
        OnPropertyChanged(nameof(ShowChildTransactionsPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(ShowCategoryImpact));
        OnPropertyChanged(nameof(ShowAccountImpact));
    }

    private void SyncGoalUpdateName()
    {
        if (SelectedGoal is not { } goal || string.IsNullOrWhiteSpace(goal.Name))
            return;

        NameText = BuildGoalUpdateDisplayName(goal.Name);
    }

    private void SyncRepaymentName()
    {
        NameText = SelectedRepaymentAccount is { } account
            ? $"Repayment to {account.Name}"
            : string.Empty;
    }

    private void LoadRepaymentAmount()
    {
        if (SelectedRepaymentAccount is { } account)
            AmountText = account.SpentAmount;
    }

    private async Task<Tag> ResolveBalanceUpdateTagAsync()
    {
        var tags = await _appData.GetTagsAsync();
        var existingTag = tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, SystemTags.BalanceUpdateName, StringComparison.OrdinalIgnoreCase));
        if (existingTag is not null)
            return existingTag;

        var balanceUpdateTag = new Tag
        {
            Name = SystemTags.BalanceUpdateName,
            HexCode = SystemTags.BalanceUpdateHexCode,
            IsSystemTag = true
        };

        await _appData.AddTagAsync(balanceUpdateTag);
        await _appData.SaveChangesAsync();
        return balanceUpdateTag;
    }
}
