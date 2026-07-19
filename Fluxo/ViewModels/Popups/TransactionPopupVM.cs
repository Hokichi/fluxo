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
using Fluxo.Helper.MainWindow;
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

namespace Fluxo.ViewModels.Popups;

public partial class TransactionPopupVM : ObservableValidator, IDisposable
{
    private const int DefaultVisibleTagSlots = 4;
    private const int NoAccountId = -1;
    private const int NoTagId = -1;
    private const int NoSavingGoalId = -1;
    private const decimal SimilarAmountTolerance = 0.05m;

    private readonly List<AccountVM> _availableAccounts = [];
    private IReadOnlyList<AccountVM>? _accountsOverride;
    private Func<RecurringDraftSaveInput, Task<TransactionPopupSubmissionResult>>? _saveRecurringDraftAsync;
    private readonly List<SavingGoalVM> _orderedGoals = [];
    private readonly IMessenger _messenger;
    private readonly TransactionPersistenceHelper _persistence;
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
    private readonly Dictionary<object, ProcessingTransactionHelper.State> _processingStates = [];
    private readonly Dictionary<object, FormState> _processingSnapshots = [];
    private readonly Dictionary<object, int> _processingTransactionIds = [];
    private int _currentProcessingIndex;
    private int? _currentProcessingRecurringTransactionId;
    private bool _isTransactionStateInitialized;
    private bool _isInitialized;
    private TransactionPopupRequest _request = TransactionPopupRequest.Add();
    private bool _useRecurringDraftMessages;
    private bool _isDisposed;

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

    public TransactionPopupVM(IAppDataService appData, IMessenger messenger)
    {
        _appData = appData;
        _messenger = messenger;
        _persistence = new TransactionPersistenceHelper(appData, messenger);
        _messenger.Register<TransactionPopupVM, TransactionPopupRefreshRequestedMessage>(this,
            static (recipient, message) =>
            {
                if (recipient.ViewedTransaction?.Id == message.TransactionId)
                    message.Reply(recipient.RefreshViewedTransactionAsync());
            });
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

        ResetForm(false);
        _initialState = CaptureState();
    }

    public void Configure(TransactionPopupRequest request)
    {
        if (_isInitialized)
            throw new InvalidOperationException("The transaction popup is already initialized.");

        _request = request;
        _accountsOverride = request.Accounts;
        _useRecurringDraftMessages = request.UseRecurringDraftMessages;
    }

    internal void ConfigureCatalogs(
        IReadOnlyList<AccountVM> accounts,
        IReadOnlyList<TagVM> tags,
        IReadOnlyList<SavingGoalVM> goals)
    {
        _accountsOverride = accounts;
        LoadChoices(accounts, tags, goals);
    }

    internal void ConfigureRecurringDraftSave(
        Func<RecurringDraftSaveInput, Task<TransactionPopupSubmissionResult>> saveAsync) =>
        _saveRecurringDraftAsync = saveAsync;

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
    public decimal CategoryToBe => TransactionCalculationHelper.CalculateCategoryToBe(CategoryCurrent, AmountText, IsRepayment);
    public decimal AccountCurrent => TransactionCalculationHelper.GetAccountCurrent(SelectedAccount);
    public decimal AccountToBe => TransactionCalculationHelper.CalculateAccountToBe(SelectedAccount, IsIncome, AmountText);
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
    public bool HasChanges => _isChangeTrackingInitialized && !LoadedTransaction.Equals(PendingTransaction);
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
    public bool ShowRecurringWeekdayInput => IsRecurringTransactionMode && RecurringTransactionValidationHelper.IsWeekdayPeriod(SelectedRecurringPeriod);
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
        EnsureTransactionState();
        _initialState = CaptureState();
        _isChangeTrackingInitialized = true;
        OnPropertyChanged(nameof(HasChanges));
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
    public bool IsProcessingComplete => IsProcessingSession && _processingStates.Values.All(state => state != ProcessingTransactionHelper.State.Pending);
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

    public async Task<TransactionPopupSubmissionResult> SaveCurrentAndAdvanceAsync(
        bool allowMaximumSpendingOverflow = false)
    {
        if (!IsProcessingSession)
            return await SaveAsync(false, allowMaximumSpendingOverflow);

        if (!TryBuildTransactionInput(out _, out var validationMessage))
            return TransactionPopupSubmissionResult.Failure(validationMessage);

        var current = CurrentProcessingTarget!;
        _processingSnapshots[current] = CaptureState();
        var result = await SaveAsync(false, allowMaximumSpendingOverflow);
        if (!result.IsSuccess)
            return result;
        if (result.TransactionId is > 0)
            _processingTransactionIds[current] = result.TransactionId.Value;
        _processingStates[current] = ProcessingTransactionHelper.State.Processed;
        MoveToNextPending();
        NotifyProcessingChanged();
        return TransactionPopupSubmissionResult.Success();
    }

    public void NavigatePreviousProcessing()
    {
        if (!IsProcessingSession || CurrentProcessingTarget is null)
            return;

        _processingSnapshots[CurrentProcessingTarget] = CaptureState();
        var targets = ProcessingTargets.ToList();
        var previousIndex = ProcessingTransactionHelper.FindPreviousProcessedIndex(
            targets.Select(target => _processingStates[target]).ToList(), _currentProcessingIndex);
        if (previousIndex < 0)
            return;

        var previous = targets[previousIndex];
        _processingStates[previous] = ProcessingTransactionHelper.State.Pending;
        _currentProcessingIndex = previousIndex;
        LoadProcessingCurrent();
        NotifyProcessingChanged();
    }

    public bool SkipCurrentProcessing()
    {
        if (!IsProcessingSession || CurrentProcessingTarget is null)
            return false;

        _processingStates[CurrentProcessingTarget] = ProcessingTransactionHelper.State.Skipped;
        var hasNext = MoveToNextPending();
        NotifyProcessingChanged();
        return hasNext;
    }

    public async Task<TransactionPopupSubmissionResult> PersistProcessedItemsAsync()
    {
        ClearProcessing();
        return TransactionPopupSubmissionResult.Success();
    }

    public void InitializeRepayment(AccountVM? target = null)
    {
        CanChangeRepaymentAccount = target is null;
        SelectedRepaymentAccount = target is null
            ? RepaymentAccounts.FirstOrDefault()
            : RepaymentAccounts.FirstOrDefault(account => account.Id == target.Id) ?? target;

        var deductSourceId = SelectedRepaymentAccount?.DeductSource;
        SelectedAccount = Accounts.FirstOrDefault(account => account.Id == deductSourceId) ??
                          Accounts.FirstOrDefault();
        IsRepayment = true;
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

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
            return true;

        await ReloadChoicesAsync(cancellationToken);
        if (!await ApplyRequestAsync(cancellationToken))
            return false;
        EnsureTransactionState();
        _isInitialized = true;
        return true;
    }

    public void RequestSplit()
    {
        if (ViewedTransaction is { Id: > 0 } transaction)
            _messenger.Send(new TransactionSplitRequestedMessage(transaction.Id));
    }

    public void RequestAddTag() =>
        _messenger.Send(new TransactionPopupAddTagRequestedMessage(ViewedTransaction?.Id ?? 0, this));

    private async Task<bool> ApplyRequestAsync(CancellationToken cancellationToken)
    {
        switch (_request.Kind)
        {
            case TransactionPopupRequestKind.AddTransaction:
                if (_request.Draft is { } draft)
                    InitializeFromDraft(draft);
                break;
            case TransactionPopupRequestKind.AddRecurringTransaction:
                InitializeRecurringMode(_request.LockRecurringMode);
                break;
            case TransactionPopupRequestKind.EditRecurringTransaction when _request.RecurringTransactionId is { } id:
                if (!await InitializeFromRecurringTransactionAsync(id, cancellationToken))
                    return false;
                break;
            case TransactionPopupRequestKind.ViewTransaction when _request.Transaction is { } transaction:
                InitializeView(transaction);
                await LoadChildTransactionsAsync(transaction.Id, cancellationToken);
                break;
            case TransactionPopupRequestKind.EditTransaction when _request.Transaction is { } transaction:
                InitializeView(transaction);
                await LoadChildTransactionsAsync(transaction.Id, cancellationToken);
                await BeginEditingViewedTransactionAsync();
                break;
            case TransactionPopupRequestKind.Repayment:
                InitializeRepayment(_request.Account);
                break;
            case TransactionPopupRequestKind.RepaymentProcessing:
                if (_request.Accounts is not { Count: > 0 }) return false;
                InitializeRepaymentProcessing(_request.Accounts ?? []);
                break;
            case TransactionPopupRequestKind.GoalProcessing:
                if (_request.Goals is not { Count: > 0 }) return false;
                InitializeGoalProcessing(_request.Goals ?? []);
                break;
            case TransactionPopupRequestKind.RecurringProcessing:
                if (_request.RecurringTransactions is not { Count: > 0 }) return false;
                InitializeRecurringProcessing(_request.RecurringTransactions ?? []);
                break;
            case TransactionPopupRequestKind.RecurringDraft:
                if (_request.RecurringDraft is { } recurringDraft)
                    InitializeFromRecurringDraft(recurringDraft);
                else
                    InitializeRecurringMode(isLocked: true);
                break;
            default:
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _messenger.Unregister<TransactionPopupRefreshRequestedMessage>(this);
    }

    private async Task<bool> RefreshViewedTransactionAsync()
    {
        if (ViewedTransaction is not { Id: > 0 } viewed)
            return false;

        var transaction = await TransactionDetailTargetResolver.ResolveAsync(viewed.Id, _appData);
        if (transaction is null)
            return false;

        await ReloadChoicesAsync(CancellationToken.None);
        InitializeView(transaction);
        await LoadChildTransactionsAsync(transaction.Id, CancellationToken.None);
        BeginChangeTracking();
        return true;
    }

    private async Task LoadChildTransactionsAsync(int parentTransactionId, CancellationToken cancellationToken)
    {
        var children = (await _appData.GetTransactionsAsync(cancellationToken))
            .Where(transaction => transaction.ParentTransactionId == parentTransactionId && !transaction.IsForDeletion)
            .Select(transaction => new TransactionDetailChildTransactionVM
            {
                Id = transaction.Id,
                Name = transaction.Name,
                Amount = transaction.Amount,
                OccurredOn = transaction.OccurredOn,
                Category = transaction.ExpenseCategory ?? ExpenseCategory.Needs,
                AccountName = transaction.Account?.Name ?? string.Empty,
                TagName = transaction.Tag?.Name ?? string.Empty,
                TagHexCode = transaction.Tag?.HexCode ?? string.Empty,
                Notes = transaction.Notes,
                IsIoU = transaction.IsIoU
            });
        InitializeChildTransactions(children);
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
        {
            SyncGoalUpdateName();
            SyncGeneratedPendingTransaction();
        }

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
        var state = AddTransactionHelper.CreateState(
            new AddTransactionHelper.Input(
                draft.IsExpense, draft.IsGoal, draft.Name, draft.AmountText, draft.AccountId,
                draft.Date, draft.Note, draft.Category, draft.TagId, draft.GoalId, draft.IsIoU,
                draft.ShouldAffectBalance, draft.IsExcludedFromBudget, draft.LockTransactionType),
            Accounts, _orderedTags, Goals);

        IsExpense = state.Input.IsExpense;
        IsGoal = state.Input.IsGoal;
        AmountText = state.Input.Amount;
        NameText = state.Input.Name;
        NoteText = state.Input.Note;
        IsIoU = state.Input.IsIoU;
        ShouldAffectBalance = state.Input.ShouldAffectBalance;
        IsExcludedFromBudget = state.Input.IsExcludedFromBudget;
        SelectedDate = state.Input.Date.Date;
        SelectedExpenseCategory = state.Input.Category ?? ExpenseCategory.Needs;
        SelectedAccount = state.SelectedAccount;
        SelectedTag = state.SelectedTag;
        SelectedGoal = state.SelectedGoal;
        IsMoreTagsOpen = false;
        if (IsGoal)
            SyncGoalUpdateName();
        _isTransactionTypeLocked = state.Input.LockTransactionType;
        OnPropertyChanged(nameof(CanChangeTransactionType));
    }

    public void InitializeView(TransactionVM transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var state = ViewTransactionHelper.CreateState(transaction, Accounts, Goals, RepaymentAccounts);
        LoadedTransaction = state.LoadedTransaction;
        PendingTransaction = state.PendingTransaction;
        _isTransactionStateInitialized = true;
        var loaded = state.LoadedTransaction;
        SetPopupPurpose(TransactionPopupPurpose.ViewTransaction);

        IsExpense = state.IsExpense;
        IsGoal = state.IsGoal;
        IsRepayment = state.IsRepayment;
        NameText = loaded.Name;
        AmountText = loaded.Amount;
        NoteText = loaded.Notes;
        SelectedDate = loaded.OccurredOn.Date;
        SelectedExpenseCategory = loaded.ExpenseCategory ?? ExpenseCategory.Needs;
        IsPinned = loaded.IsPinned;
        IsIoU = loaded.IsIoU;
        ShouldAffectBalance = loaded.ShouldAffectBalance;
        IsExcludedFromBudget = loaded.IsExcludedFromBudget;
        SelectedAccount = state.SelectedAccount;
        SelectedTag = state.SelectedTag;
        SelectedGoal = state.SelectedGoal;
        SelectedRepaymentAccount = state.SelectedRepaymentAccount;
        PendingTransaction = TransactionMappingHelper.CreatePending(LoadedTransaction);
        ViewedTransaction = loaded;
        _isTransactionTypeLocked = true;
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
        LoadedTransaction.Id = 0;
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
        PendingTransaction = TransactionMappingHelper.CreatePending(LoadedTransaction);
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
        SyncPendingTransactionFromForm();
        var input = EditTransactionHelper.CreateInput(PendingTransaction);
        return new TransactionEditInput(input.Name, input.Amount, input.IsPinned, input.Note, input.Date,
            input.Category, input.AccountId, input.TagId, input.IsIoU, input.ShouldAffectBalance,
            input.IsExcludedFromBudget);
    }

    public TransactionPopupDraft CreateViewedTransactionDraft()
    {
        var transaction = ViewedTransaction ?? throw new InvalidOperationException("No transaction is being viewed.");
        var draft = EditTransactionHelper.CreateDraft(transaction);
        return new TransactionPopupDraft(draft.IsExpense, draft.Name, draft.Amount, draft.AccountId,
            draft.Date, draft.Note, draft.Category, draft.TagId, draft.IsGoal, draft.GoalId, draft.IsIoU,
            draft.IsExcludedFromBudget, ShouldAffectBalance: draft.ShouldAffectBalance);
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
        if (value)
            SeedGeneratedAddBaseline();
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
        if (value)
            SeedGeneratedAddBaseline();
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
        if (IsRepayment)
            SyncGeneratedPendingTransaction();
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

    public async Task<TransactionPopupSubmissionResult> SaveAsync(
        bool resetAfterSave,
        bool allowMaximumSpendingOverflow = false)
    {
        if (IsSaving)
            return TransactionPopupSubmissionResult.Failure("A transaction is already being saved.");

        EnsureTransactionState();

        if (!TryBuildTransactionInput(out var input, out var validationMessage))
            return TransactionPopupSubmissionResult.Failure(validationMessage);

        IsSaving = true;

        try
        {
            if (input.IsRecurring && (_saveRecurringDraftAsync is not null || _useRecurringDraftMessages))
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

                var draftInput = new RecurringDraftSaveInput(
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
                    input.IsInstallments ? input.InstallmentEndDate : null);
                var draftSaveResult = _saveRecurringDraftAsync is not null
                    ? await _saveRecurringDraftAsync(draftInput)
                    : await SendRecurringDraftSaveRequestAsync(draftInput);
                if (!draftSaveResult.IsSuccess)
                    return draftSaveResult;

                if (resetAfterSave)
                {
                    await ReloadChoicesAsync(CancellationToken.None);
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

            if (LoadedTransaction.Id == 0)
            {
                var spendingValidation = TransactionValidationHelper.ValidateSpendingAmount(
                    input.IsExpense || input.IsRepayment,
                    input.IsGoal,
                    effectiveSaveAmount,
                    account);
                if (!spendingValidation.IsValid)
                    return TransactionPopupSubmissionResult.Failure(spendingValidation.ErrorMessage);
            }

            var invalidationScope = DashboardDataInvalidationScope.Budget | DashboardDataInvalidationScope.Notifications;
            int? persistedTransactionId = null;

            if (!input.IsRecurring)
            {
                if (LoadedTransaction.Id == 0 && input.IsGoal)
                {
                    if (input.GoalId is null)
                        return TransactionPopupSubmissionResult.Failure("Please choose a goal.");
                    if (!GoalUpdateTransactionSupport.IsEligibleGoalSourceType(account.AccountType))
                        return TransactionPopupSubmissionResult.Failure("Goal updates can only be taken from Cash or Checking.");
                    if (!input.IsEffectivelyExcludedFromBudget)
                    {
                        var budgetPolicyResult = await ApplyExpenseBudgetPolicyAsync(
                            ExpenseCategory.Savings, input.Amount, input.Date);
                        if (!budgetPolicyResult.IsSuccess)
                            return budgetPolicyResult;
                    }
                }
                else if (LoadedTransaction.Id == 0 && input.IsExpense && !input.IsEffectivelyExcludedFromBudget)
                {
                    var budgetPolicyResult = await ApplyExpenseBudgetPolicyAsync(
                        input.Category!.Value, input.Amount, input.Date);
                    if (!budgetPolicyResult.IsSuccess)
                        return budgetPolicyResult;
                }

                SyncPendingTransactionFromForm();
                PendingTransaction.Amount = effectiveSaveAmount;
                PendingTransaction.OccurredOn = input.Date;
                var persistenceResult = await _persistence.SaveAsync(
                    LoadedTransaction,
                    PendingTransaction,
                    new TransactionPersistenceHelper.SaveOptions(
                        AllowMaximumSpendingOverflow: allowMaximumSpendingOverflow,
                        IsRepayment: input.IsRepayment,
                        RelatedRecurringTransactionId: input.RelatedRecurringTransactionId,
                        SuppressNotificationInvalidation: IsProcessingSession));
                if (persistenceResult.RequiresConfirmation)
                    return TransactionPopupSubmissionResult.Confirmation(persistenceResult.ErrorMessage);
                if (!persistenceResult.IsSuccess)
                    return TransactionPopupSubmissionResult.Failure(persistenceResult.ErrorMessage);
                persistedTransactionId = persistenceResult.TransactionId;
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
                    _messenger.Send(new NotificationEntityCreatedMessage(NotificationEntityKind.RecurringTransaction, recurring.Id));
            }
            if (IsProcessingSession)
                invalidationScope &= ~DashboardDataInvalidationScope.Notifications;

            if (input.IsRecurring)
                _messenger.Send(new DashboardDataInvalidatedMessage(invalidationScope));

            if (resetAfterSave)
            {
                await ReloadChoicesAsync(CancellationToken.None);
                await EnsureTagsLoadedAsync();
                ResetForm(true);
            }

            var savedType = input.IsGoal ? "Goal contribution" : input.IsExpense ? "Expense" : "Income";
            FloatingNotificationPublisher.Success(
                input.Name, $"{savedType} was recorded.", true, "Added");
            return TransactionPopupSubmissionResult.Success(persistedTransactionId);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "save transaction");
            return TransactionPopupSubmissionResult.Failure(string.Empty);
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task<TransactionPopupSubmissionResult> DeleteAsync()
    {
        if (IsSaving)
            return TransactionPopupSubmissionResult.Failure("A transaction is already being saved.");
        if (LoadedTransaction.Id <= 0)
            return TransactionPopupSubmissionResult.Failure("Unable to load this transaction.");

        IsSaving = true;
        try
        {
            var result = await _persistence.DeleteAsync(LoadedTransaction);
            if (!result.IsSuccess)
                return TransactionPopupSubmissionResult.Failure(result.ErrorMessage);

            return TransactionPopupSubmissionResult.Success();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception,
                "delete transaction");
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
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception,
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

    private async Task ReloadChoicesAsync(CancellationToken cancellationToken)
    {
        var accounts = _accountsOverride ?? (await _appData.GetAccountsAsync(cancellationToken))
            .Select(ProjectAccount)
            .ToArray();
        var tags = ProjectNonSystemTags(await _appData.GetTagsAsync(cancellationToken)).ToArray();
        if (tags.Length == 0)
            tags = _orderedTags.ToArray();
        var goals = (await _appData.GetSavingGoalsAsync(cancellationToken))
            .Select(ProjectSavingGoal)
            .ToArray();
        if (goals.Length == 0)
            goals = _orderedGoals.ToArray();
        LoadChoices(accounts, tags, goals);
    }

    private void LoadChoices(
        IReadOnlyList<AccountVM> accounts,
        IReadOnlyList<TagVM> tags,
        IReadOnlyList<SavingGoalVM> goals)
    {
        _availableAccounts.Clear();
        _availableAccounts.AddRange(accounts.Where(source => source.IsEnabled));
        ReplaceCollection(
            RepaymentAccounts,
            _availableAccounts
                .Where(source => source.AccountType == AccountType.Credit)
                .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase));

        _orderedTags.Clear();
        _orderedTags.AddRange(OrderNonSystemTags(tags
            .GroupBy(tag => tag.Id)
            .Select(group => group.First())));

        _orderedGoals.Clear();
        _orderedGoals.AddRange(goals
            .GroupBy(goal => goal.Id)
            .Select(group => group.First())
            .OrderBy(goal => goal.Name));

        ReplaceCollection(Goals, _orderedGoals);
        RefreshTagCollections();
        RefreshAccounts();
        ResetForm(false);
        _ = RefreshExpenseCategoryAvailabilityAsync();
    }

    private async Task<TransactionPopupSubmissionResult> SendRecurringDraftSaveRequestAsync(
        RecurringDraftSaveInput input)
    {
        var message = _messenger.Send(new RecurringDraftSaveRequestedMessage(input));
        return message.HasReceivedResponse
            ? await message.Response
            : TransactionPopupSubmissionResult.Failure("Unable to save the recurring transaction draft.");
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
                option.IsEnabled = TransactionCalculationHelper.GetCategoryState(snapshot, option.Value).Remaining > 0m;
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
        var categoryState = TransactionCalculationHelper.GetCategoryState(snapshot, category);
        var validation = TransactionValidationHelper.ValidateCategoryBudget(
            allocation.OverspendPolicy, categoryState, category, amount);
        if (!validation.IsValid)
            return TransactionPopupSubmissionResult.Failure(validation.ErrorMessage);

        if (allocation.OverspendPolicy == OverspendPolicy.SoftDebt)
        {
            var debtDelta = BudgetAllocationCalculator.CalculateSoftDebtDelta(categoryState.Remaining, amount);
            if (debtDelta > 0m)
            {
                TransactionCalculationHelper.AddDebtDelta(allocation, category, debtDelta);
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
            TransactionCalculationHelper.CalculateSpentByCategory(expenseLogs, currentPeriod),
            TransactionCalculationHelper.CalculateSpentByCategory(expenseLogs, previousPeriod),
            allocationDate,
            await CalculateBudgetAvailableBaseAsync(allocation));
    }

    private async Task<decimal> CalculateBudgetAvailableBaseAsync(BudgetAllocation allocation)
    {
        if (allocation.AllocationLimit > 0m)
            return allocation.AllocationLimit;

        var accounts = await _appData.GetAccountsAsync();
        var balanceBackedSourceIds = accounts
            .Where(account => account.AccountType != AccountType.Credit)
            .Select(account => account.Id)
            .ToHashSet();
        var balanceBackedExpenses = BudgetEffectiveTransactionFilter
            .Select(await _appData.GetTransactionsAsync())
            .Where(transaction => transaction.Type == TransactionType.Expense &&
                                  balanceBackedSourceIds.Contains(transaction.SourceAccountId))
            .Sum(transaction => transaction.Amount);
        return accounts.Sum(account => account.Balance) + balanceBackedExpenses;
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
        SyncPendingTransactionFromForm();
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
            return TransactionCalculationHelper.GetCategoryState(
                BuildBudgetAllocationSnapshotAsync(allocation, SelectedDate).GetAwaiter().GetResult(),
                SelectedExpenseCategory).Spent;
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
                CalculateBudgetAvailableBaseAsync(allocation).GetAwaiter().GetResult());

            return spent + AmountText > allowance ? "Over Daily Allowance" : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void EnsureTransactionState()
    {
        if (_isTransactionStateInitialized)
            return;

        SetTransactionState(new TransactionVM());
        SyncPendingTransactionFromForm();
        LoadedTransaction = TransactionMappingHelper.CreateLoaded(PendingTransaction);
    }

    private void SetTransactionState(TransactionVM source)
    {
        LoadedTransaction = TransactionMappingHelper.CreateLoaded(source);
        PendingTransaction = TransactionMappingHelper.CreatePending(LoadedTransaction);
        _isTransactionStateInitialized = true;
    }

    private void SeedGeneratedAddBaseline()
    {
        if (!IsGeneratedAddMode)
            return;

        SetTransactionState(CreateGeneratedAddTransaction());
    }

    private void SyncGeneratedPendingTransaction()
    {
        SyncPendingTransactionFromForm();
    }

    private void SyncPendingTransactionFromForm()
    {
        if (!_isTransactionStateInitialized)
            return;

        PendingTransaction.Type = IsExpense || IsGoal || IsRepayment ? TransactionType.Expense : TransactionType.Income;
        PendingTransaction.SourceAccountId = SelectedAccount?.Id ?? 0;
        PendingTransaction.GoalId = IsGoal ? SelectedGoal?.Id : null;
        PendingTransaction.RepaymentAccountId = IsRepayment ? SelectedRepaymentAccount?.Id : null;
        PendingTransaction.Account = SelectedAccount ?? new AccountVM();
        PendingTransaction.Name = NameText;
        PendingTransaction.Amount = AmountText;
        PendingTransaction.OccurredOn = SelectedDate.Date;
        PendingTransaction.Notes = NoteText;
        PendingTransaction.ExpenseCategory = IsGoal ? ExpenseCategory.Savings : IsExpense ? SelectedExpenseCategory : null;
        PendingTransaction.Tag = IsGoal || IsRepayment ? null : SelectedTag;
        PendingTransaction.IsPinned = IsPinned;
        PendingTransaction.IsIoU = IsIoU;
        PendingTransaction.ShouldAffectBalance = ShouldAffectBalance;
        PendingTransaction.IsExcludedFromBudget = IsBudgetExcluded;
    }

    private bool IsGeneratedAddMode => _popupPurpose == TransactionPopupPurpose.AddNewTransaction &&
                                       !IsRecurringTransactionMode &&
                                       !IsProcessingSession &&
                                       (IsGoal || IsRepayment);

    private TransactionVM CreateGeneratedAddTransaction() => new()
    {
        Type = TransactionType.Expense,
        SourceAccountId = SelectedAccount?.Id ?? 0,
        GoalId = IsGoal ? SelectedGoal?.Id : null,
        RepaymentAccountId = IsRepayment ? SelectedRepaymentAccount?.Id : null,
        Account = SelectedAccount ?? new AccountVM(),
        Name = NameText,
        Amount = AmountText,
        OccurredOn = SelectedDate,
        Notes = NoteText,
        ExpenseCategory = IsGoal ? ExpenseCategory.Savings : null,
        IsExcludedFromBudget = IsBudgetExcluded
    };

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
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception,
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
        return !IsInstallments || RecurringTransactionValidationHelper.ValidateInstallments(
            SelectedRecurringPeriod, RecurringTimeText, InstallmentEndDate, StartDate).IsValid;
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
        return ToValidationResult(TransactionValidationHelper.ValidateName(value, viewModel.IsGoal));
    }

    public static ValidationResult? ValidateAmountText(decimal value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        var basicValidation = TransactionValidationHelper.ValidateAmount(
            value, viewModel._isRepaymentAmountInvalid,
            viewModel.IsExpense || viewModel.IsRepayment, viewModel.IsGoal, null);
        if (!basicValidation.IsValid || viewModel.SelectedAccount is null)
            return ToValidationResult(basicValidation);

        var amountToValidate = value;
        if (viewModel.IsInstallments)
        {
            var installmentValidation = RecurringTransactionValidationHelper.ValidateInstallments(
                viewModel.SelectedRecurringPeriod, viewModel.RecurringTimeText,
                viewModel.InstallmentEndDate, viewModel.StartDate);
            if (!installmentValidation.IsValid)
                return ValidationResult.Success;
            amountToValidate = TransactionCalculationHelper.CalculateInstallmentAmount(
                value, installmentValidation.OccurrenceCount);
        }

        var amountValidation = TransactionValidationHelper.ValidateAmount(
            amountToValidate, viewModel._isRepaymentAmountInvalid,
            viewModel.IsExpense || viewModel.IsRepayment,
            viewModel.IsGoal,
            viewModel.LoadedTransaction?.Id > 0 ? null : viewModel.SelectedAccount);
        if (!amountValidation.IsValid)
            return ToValidationResult(amountValidation);

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

            var result = TransactionValidationHelper.ValidateTagSpending(
                IsExpense, IsRecurring, IsExcludedFromBudget, tag, currentTagSpending, amount);
            validationMessage = result.ErrorMessage ?? string.Empty;
            return result.IsValid;
        }
        catch
        {
            return true;
        }
    }

    public static ValidationResult? ValidateSelectedAccount(AccountVM? value, ValidationContext validationContext)
    {
        _ = validationContext;
        return ToValidationResult(TransactionValidationHelper.ValidateAccount(value));
    }

    public static ValidationResult? ValidateSelectedTag(TagVM? value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        return ToValidationResult(TransactionValidationHelper.ValidateTag(value, viewModel.IsExpense));
    }

    public static ValidationResult? ValidateSelectedGoal(SavingGoalVM? value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        return ToValidationResult(TransactionValidationHelper.ValidateGoal(value, viewModel.IsGoal));
    }

    public static ValidationResult? ValidateRecurringTimeText(string value, ValidationContext validationContext)
    {
        var viewModel = (TransactionPopupVM)validationContext.ObjectInstance;
        if (!viewModel.IsRecurringTransactionMode)
            return ValidationResult.Success;
        var result = RecurringTransactionValidationHelper.ValidateTime(viewModel.SelectedRecurringPeriod, value);
        return result.IsValid ? ValidationResult.Success : new ValidationResult(result.ErrorMessage);
    }

    private static ValidationResult? ToValidationResult(TransactionValidationHelper.Result result) =>
        result.IsValid ? ValidationResult.Success : new ValidationResult(result.ErrorMessage);

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

    internal static AccountVM ProjectAccount(Account account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        AccountType = account.AccountType,
        AccountLimit = account.AccountLimit,
        MaximumSpending = account.MaximumSpending,
        MinimumPayment = account.MinimumPayment,
        SpentAmount = account.SpentAmount,
        Balance = account.Balance,
        MonthlyDueDate = account.MonthlyDueDate,
        DeductSource = account.DeductSource,
        InterestRate = account.InterestRate,
        PinnedOnUI = account.PinnedOnUI,
        IsEnabled = account.IsEnabled,
        IsDefault = account.IsDefault
    };

    internal static SavingGoalVM ProjectSavingGoal(SavingGoal goal) => new()
    {
        Id = goal.Id,
        Name = goal.Name,
        TargetAmount = goal.TargetAmount,
        CurrentAmount = goal.CurrentAmount,
        SavingEndDate = goal.SavingEndDate,
        CreatedOn = goal.CreatedOn
    };

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

    public readonly record struct TransactionPopupSubmissionResult(
        bool IsSuccess,
        string? ErrorMessage,
        int? TransactionId = null,
        bool RequiresConfirmation = false)
    {
        public static TransactionPopupSubmissionResult Success(int? transactionId = null)
        {
            return new TransactionPopupSubmissionResult(true, null, transactionId, false);
        }

        public static TransactionPopupSubmissionResult Failure(string? errorMessage)
        {
            return new TransactionPopupSubmissionResult(false, errorMessage, null, false);
        }

        public static TransactionPopupSubmissionResult Confirmation(string? message)
        {
            return new TransactionPopupSubmissionResult(false, message, null, true);
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
        _processingTransactionIds.Clear();
        _currentProcessingIndex = 0;
        foreach (var target in targets)
            _processingStates[target] = ProcessingTransactionHelper.State.Pending;
        ProcessingStepCount = targets.Count;
        CurrentProcessingStep = targets.Count == 0 ? 0 : 1;
    }

    private bool MoveToNextPending()
    {
        var targets = ProcessingTargets.ToList();
        var index = ProcessingTransactionHelper.FindNextPendingIndex(
            targets.Select(target => _processingStates[target]).ToList(), _currentProcessingIndex);
        if (index < 0)
            return false;

        _currentProcessingIndex = index;
        LoadProcessingCurrent();
        return true;
    }

    private void LoadProcessingCurrent()
    {
        if (CurrentProcessingTarget is not { } target)
            return;

        _currentProcessingRecurringTransactionId = null;
        if (_processingSnapshots.TryGetValue(target, out var snapshot))
        {
            LoadProcessingTarget(target, snapshot);
            RestoreProcessingTransactionState(target);
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
        RestoreProcessingTransactionState(target);
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

    private void RestoreProcessingTransactionState(object target)
    {
        EnsureTransactionState();
        SyncPendingTransactionFromForm();
        var loaded = TransactionMappingHelper.CreateLoaded(PendingTransaction);
        loaded.Id = _processingTransactionIds.GetValueOrDefault(target);
        LoadedTransaction = loaded;
        PendingTransaction.Id = 0;
        PendingTransaction.LoggedOn = default;
        PendingTransaction.ParentTransactionId = null;
        PendingTransaction.IsForDeletion = false;
    }

    private void ClearProcessing()
    {
        _processingRepayments.Clear();
        _processingGoals.Clear();
        _processingRecurringTransactions.Clear();
        _processingStates.Clear();
        _processingSnapshots.Clear();
        _processingTransactionIds.Clear();
        _currentProcessingIndex = 0;
        _currentProcessingRecurringTransactionId = null;
        ProcessingStepCount = 0;
        CurrentProcessingStep = 0;
        SetPopupPurpose(TransactionPopupPurpose.AddNewTransaction);
        NotifyProcessingChanged();
    }

    private void NotifyProcessingChanged()
    {
        var navigableTargets = ProcessingTargets.Where(target => _processingStates[target] != ProcessingTransactionHelper.State.Skipped).ToList();
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

    private int? _editingRecurringTransactionId;

    public void InitializeRecurringMode(bool isLocked)
    {
        var state = AddRecurringTransactionHelper.CreateState(isLocked);
        SetPopupPurpose(TransactionPopupPurpose.AddRecurringTransaction);
        _isTransactionTypeLocked = state.IsTransactionTypeLocked;
        OnPropertyChanged(nameof(CanChangeTransactionType));
        IsRecurringModeLocked = state.IsRecurringModeLocked;
        IsInstallments = state.IsInstallments;
        IsRecurring = state.IsRecurring;
    }

    public async Task<bool> InitializeFromRecurringTransactionAsync(int recurringTransactionId, CancellationToken cancellationToken = default)
    {
        var recurring = await _appData.GetRecurringTransactionByIdAsync(recurringTransactionId, cancellationToken);
        if (recurring is null)
            return false;

        var state = EditRecurringTransactionHelper.CreateState(
            new EditRecurringTransactionHelper.Input(
                recurring.Id, recurring.Type, recurring.Name, recurring.Amount, recurring.RecurringPeriod,
                recurring.RecurringTime, recurring.SourceId, recurring.Category, recurring.TagId,
                recurring.GoalId, recurring.IsExcludedFromBudget),
            Accounts, _orderedTags, Goals);
        _editingRecurringTransactionId = state.EditingRecurringTransactionId;
        SetPopupPurpose(TransactionPopupPurpose.EditRecurringTransaction);
        _isTransactionTypeLocked = true;
        OnPropertyChanged(nameof(CanChangeTransactionType));
        IsRecurringModeLocked = true;
        IsInstallments = false;
        IsRecurring = true;
        IsExpense = state.IsExpense;
        IsGoal = state.IsGoal;
        NameText = state.Name;
        AmountText = state.Amount;
        SelectedExpenseCategory = state.Category;
        SelectedRecurringPeriod = state.RecurringPeriod;
        RecurringTimeText = state.RecurringTimeText;
        SelectedAccount = state.SelectedAccount;
        IsExcludedFromBudget = state.IsExcludedFromBudget;
        SelectedTag = state.SelectedTag;
        SelectedGoal = state.SelectedGoal;
        return true;
    }

    public void InitializeFromRecurringDraft(RecurringDraftSnapshot draft)
    {
        var state = EditRecurringTransactionHelper.CreateState(
            new EditRecurringTransactionHelper.Input(
                draft.EditingRecurringTransactionId, draft.Type, draft.Name, draft.Amount,
                draft.RecurringPeriod, draft.RecurringTime, draft.AccountId, draft.Category,
                draft.TagId, draft.GoalId, false),
            Accounts, _orderedTags, Goals);
        _editingRecurringTransactionId = state.EditingRecurringTransactionId;
        SetPopupPurpose(draft.EditingRecurringTransactionId is > 0
            ? TransactionPopupPurpose.EditRecurringTransaction
            : TransactionPopupPurpose.AddRecurringTransaction);
        _isTransactionTypeLocked = true;
        OnPropertyChanged(nameof(CanChangeTransactionType));
        IsRecurringModeLocked = true;
        IsInstallments = false;
        IsRecurring = true;
        IsExpense = state.IsExpense;
        IsGoal = state.IsGoal;
        NameText = state.Name;
        AmountText = state.Amount;
        SelectedExpenseCategory = state.Category;
        SelectedRecurringPeriod = state.RecurringPeriod;
        RecurringTimeText = state.RecurringTimeText;
        SelectedAccount = state.SelectedAccount;
        SelectedTag = state.SelectedTag;
        SelectedGoal = state.SelectedGoal;
    }

    internal static bool TryNormalizeRecurringTime(RecurringPeriod period, string text, out int recurringTime) =>
        RecurringTransactionValidationHelper.TryNormalizeTime(period, text, out recurringTime);

    private static string GetDefaultRecurringTimeText(RecurringPeriod period) =>
        RecurringTransactionValidationHelper.GetDefaultTimeText(period, DateTime.Today);

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

        var installmentAmount = TransactionCalculationHelper.CalculateInstallmentAmount(AmountText, count);
        var amountText = MoneyFormatUtility.ToFullText(installmentAmount, CultureInfo.CurrentCulture);
        var recurrenceLabel = RecurringTransactionValidationHelper.FormatScheduleLabel(
            SelectedRecurringPeriod, RecurringTimeText, CultureInfo.CurrentCulture);
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

        amount = TransactionCalculationHelper.CalculateInstallmentAmount(input.Amount, count);
        return true;
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
        var result = RecurringTransactionValidationHelper.ValidateInstallments(
            period, recurringTimeText, endDate, today);
        count = result.OccurrenceCount;
        validationMessage = result.ErrorMessage ?? string.Empty;
        return result.IsValid;
    }

    private static string GetRecurringTimeValidationMessage(RecurringPeriod period) =>
        RecurringTransactionValidationHelper.GetTimeValidationMessage(period);

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

}
