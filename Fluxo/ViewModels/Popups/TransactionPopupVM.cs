using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Budgeting;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
using Fluxo.DataModels.Popups.TransactionPopup;
using Fluxo.Helpers.MainWindow;
using Fluxo.Helpers.Popups;
using Fluxo.Helpers.Transaction;
using Fluxo.Helpers.Transactions;
using Fluxo.Resources.CustomControls;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Transactions;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;

namespace Fluxo.ViewModels.Popups;

public partial class TransactionPopupVM : ObservableValidator, IDisposable
{
    private const int NoAccountId = -1;
    private const int NoSavingGoalId = -1;
    private const decimal SimilarAmountTolerance = 0.05m;
    private const string DuplicateWarningMessage = "Potentially duplicated transaction found.";

    private readonly List<AccountVM> _availableAccounts = [];
    private IReadOnlyList<AccountVM>? _accountsOverride;
    private Func<RecurringDraftSaveInput, Task<TransactionPopupSubmissionResult>>? _saveRecurringDraftAsync;
    private readonly List<SavingGoalVM> _orderedGoals = [];
    private readonly IMessenger _messenger;
    private readonly TransactionPopupMessageToken _messageToken;
    private readonly TransactionPersistenceHelper _persistence;
    private readonly List<TagVM> _orderedTags = [];
    private readonly IAppDataService _appData;
    private IReadOnlyList<Transaction> _duplicateCandidates = [];
    private IReadOnlySet<int> _duplicateGoalUpdateTagIds = new HashSet<int>();
    private bool _isRefreshingDuplicateWarnings;
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
    private readonly Dictionary<TransactionVM, TransactionFormOptions> _transactionFormOptions =
        new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<TransactionVM> _queuedTransactions = [];
    private bool _bulkQueueHasChanges;
    private bool _bulkQueueAllValid = true;
    private bool _bulkQueueHadItems;
    private int _currentProcessingIndex;
    private int? _currentProcessingRecurringTransactionId;
    private bool _isTransactionStateInitialized;
    private bool _isInitialized;
    private TransactionPopupRequest _request = TransactionPopupRequest.Add();
    private bool _useRecurringDraftMessages;
    private bool _isDisposed;
    private bool _isLoadingTransaction;
    private bool _isSyncingTransaction;
    private bool _isResettingBulkForm;
    private TransactionVM _currentRootTransaction = null!;
    private IReadOnlyDictionary<ExpenseCategory, decimal> _balanceUpdateCategoryCurrentAmounts = new Dictionary<ExpenseCategory, decimal>();
    private IReadOnlyDictionary<int, decimal> _balanceUpdateTagCurrentAmounts = new Dictionary<int, decimal>();
    public Guid AddTagOwnerToken { get; } = Guid.NewGuid();

    [ObservableProperty]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateAmountText))]
    private decimal _amountText;

    [ObservableProperty] private bool _isExpense = true;
    [ObservableProperty] private bool _isGoal;
    [ObservableProperty] private bool _isRepayment;
    [ObservableProperty] private bool _isSaving;
    private bool _isUpdatingTagCollections;

    [ObservableProperty]
    [CustomValidation(typeof(TransactionPopupVM), nameof(ValidateNameText))]
    private string _nameText = string.Empty;

    [ObservableProperty] private string _noteText = string.Empty;
    [ObservableProperty] private DateTime _selectedDate = DateTime.Now.Date;
    [ObservableProperty] private TimeSpan _selectedTime = DateTime.Now.TimeOfDay;
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
    [ObservableProperty] private TransactionPopupSidePanel _selectedSidePanel = TransactionPopupSidePanel.History;
    private TransactionVM? _selectedQueuedTransaction;
    [ObservableProperty] private bool _isBulkMode;
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
    private TagVM? _selectedTag;

    public TransactionPopupVM(IAppDataService appData, IMessenger messenger)
        : this(appData, messenger, TransactionPopupMessageToken.Default)
    {
    }

    public TransactionPopupVM(
        IAppDataService appData,
        IMessenger messenger,
        TransactionPopupMessageToken messageToken)
    {
        _appData = appData;
        _messenger = messenger;
        _messageToken = messageToken;
        _persistence = new TransactionPersistenceHelper(appData, messenger);
        _messenger.Register<TransactionPopupVM, TransactionPopupRefreshRequestedMessage>(this,
            static (recipient, message) =>
            {
                if (recipient.ViewedTransaction?.Id == message.TransactionId)
                    message.Reply(recipient.RefreshViewedTransactionAsync());
            });
        _messenger.Register<TransactionPopupVM, TransactionLoadRequestedMessage, TransactionPopupMessageToken>(
            this, messageToken, static (recipient, message) => recipient.HandleLoadRequested(message.Value));
        _messenger.Register<TransactionPopupVM, TransactionBulkQueueStateChangedMessage, TransactionPopupMessageToken>(
            this, messageToken, static (recipient, message) => recipient.HandleQueueStateChanged(message));
        _messenger.Register<TransactionPopupVM, TransactionSplitChangedMessage, TransactionPopupMessageToken>(
            this, messageToken, static (recipient, message) => recipient.HandleSplitChanged(message.Value));
        ErrorsChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(CanPersist));
            NotifyTransactionWarningsChanged();

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
        RefreshFieldFeedback();
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

    public IReadOnlyList<ExpenseCategoryOptionVM> ExpenseCategories { get; } =
    [
        new("Needs", ExpenseCategory.Needs),
        new("Wants", ExpenseCategory.Wants),
        new("Invest", ExpenseCategory.Savings)
    ];

    public ObservableCollection<AccountVM> Accounts { get; } = [];
    public ICollectionView AccountsView { get; }
    public ObservableCollection<AccountVM> RepaymentAccounts { get; } = [];
    public ObservableCollection<SavingGoalVM> Goals { get; } = [];
    public ObservableCollection<TagVM> Tags { get; } = [];
    public ObservableCollection<BalanceUpdateItem> CategoryBalanceUpdates { get; } = [];
    public ObservableCollection<BalanceUpdateItem> TagBalanceUpdates { get; } = [];
    public ObservableCollection<AddNewTransactionSuggestion> TransactionNameSuggestions { get; } = [];
    public AddNewTransactionHistoryListVM PinnedHistory { get; } = new();
    public AddNewTransactionHistoryListVM TransactionHistory { get; } = new();
    public bool IsNeedsCategory
    {
        get => CanEditCategory && !IsExcludedFromBudget && SelectedExpenseCategory == ExpenseCategory.Needs;
        set
        {
            if (!value)
                return;

            IsExcludedFromBudget = false;
            SelectedExpenseCategory = ExpenseCategory.Needs;
        }
    }

    public bool IsWantsCategory
    {
        get => CanEditCategory && !IsExcludedFromBudget && SelectedExpenseCategory == ExpenseCategory.Wants;
        set
        {
            if (!value)
                return;

            IsExcludedFromBudget = false;
            SelectedExpenseCategory = ExpenseCategory.Wants;
        }
    }

    public bool IsInvestCategory
    {
        get => CanEditCategory && !IsExcludedFromBudget && SelectedExpenseCategory == ExpenseCategory.Savings;
        set
        {
            if (!value)
                return;

            IsExcludedFromBudget = false;
            SelectedExpenseCategory = ExpenseCategory.Savings;
        }
    }

    public bool IsExcludedCategory
    {
        get => CanEditCategory && IsExcludedFromBudget;
        set
        {
            if (value)
                IsExcludedFromBudget = true;
        }
    }
    public bool ShowCategoryImpact => !IsEditingSplitNode && !IsViewOnly && !IsRecurringTransactionMode && AmountText > 0m && !IsUnpostedIoUMode &&
                                       (IsRepayment || (IsExpense && !IsExcludedFromBudget));
    public bool ShowAccountImpact => !IsEditingSplitNode && !IsViewOnly && !IsRecurringTransactionMode && AmountText > 0m && !IsUnpostedIoUMode && SelectedAccount is not null;
    public decimal CategoryCurrent => IsRepayment
        ? SelectedRepaymentAccount?.SpentAmount ?? 0m
        : ShowCategoryImpact ? GetCategoryCurrentAmount() : 0m;
    public decimal CategoryToBe => TransactionCalculationHelper.CalculateCategoryToBe(CategoryCurrent, AmountText, IsRepayment);
    public decimal AccountCurrent => TransactionCalculationHelper.GetAccountCurrent(SelectedAccount);
    public decimal AccountToBe => TransactionCalculationHelper.CalculateAccountToBe(SelectedAccount, IsIncome, AmountText);
    public bool ShowBalanceUpdate => _isTransactionStateInitialized && !IsViewOnly && !IsRecurringTransactionMode && !IsUnpostedIoUMode;
    public bool ShowBalanceUpdateCategories => ShowBalanceUpdate && CanEditCategory && !IsExcludedFromBudget &&
                                               !(_currentRootTransaction.IsIoU && _currentRootTransaction.ShouldAffectBalance);
    public bool ShowBalanceUpdateTags => ShowBalanceUpdate && CanEditTags && SelectedTag is not null;
    public string BalanceUpdateAccountName => _isTransactionStateInitialized ? _currentRootTransaction.Account.Name : string.Empty;
    public decimal BalanceUpdateAccountCurrent => _isTransactionStateInitialized
        ? TransactionCalculationHelper.GetAccountCurrent(_currentRootTransaction.Account)
        : 0m;
    public decimal BalanceUpdateAccountToBe => CalculateBalanceUpdateAccountToBe();
    public TransactionFieldFeedback NameFeedback { get; } = new();
    public TransactionFieldFeedback TimeFeedback { get; } = new();
    public TransactionFieldFeedback AmountFeedback { get; } = new();
    public TransactionFieldFeedback AccountFeedback { get; } = new();
    public TransactionFieldFeedback TagFeedback { get; } = new();
    public TransactionFieldFeedback GoalFeedback { get; } = new();
    public TransactionFieldFeedback RecurrenceFeedback { get; } = new();
    public TransactionFieldFeedback CreditAccountFeedback { get; } = new();

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

    public bool CanPersist => !IsSaving && (IsBulkMode ? _bulkQueueAllValid : IsCurrentInputValid());
    public bool HasChanges => _isChangeTrackingInitialized &&
                              (IsBulkMode
                                  ? _bulkQueueHasChanges
                                  : !LoadedTransaction.HasSameValues(_currentRootTransaction) ||
                                    !TransactionSplitHelper.AreTreesEqual(LoadedTransaction, _currentRootTransaction));
    public bool HasPendingTransactionChanges => IsEditingViewedTransaction && _isTransactionStateInitialized &&
                                                 (!TransactionMappingHelper.CreatePending(LoadedTransaction).HasSameValues(_currentRootTransaction) ||
                                                  !TransactionSplitHelper.AreTreesEqual(LoadedTransaction, _currentRootTransaction));
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
    public bool ShowSidePanelToggle => _popupPurpose == TransactionPopupPurpose.AddNewTransaction;
    public bool IsHistoryPanelSelected => SelectedSidePanel == TransactionPopupSidePanel.History;
    public bool IsPinnedPanelSelected => SelectedSidePanel == TransactionPopupSidePanel.Pinned;
    public bool IsSplitPanelSelected => !ShowSidePanelToggle || SelectedSidePanel == TransactionPopupSidePanel.Split;
    private bool HasSplitTransactions =>
        _isTransactionStateInitialized && _currentRootTransaction.ChildTransactions.Count > 0;
    private bool IsEditingSplitNode =>
        _isTransactionStateInitialized && !ReferenceEquals(PendingTransaction, _currentRootTransaction);
    private bool IsCurrentSplitParent => HasSplitTransactions &&
                                         (ReferenceEquals(PendingTransaction, _currentRootTransaction) ||
                                          PendingTransaction.ChildTransactions.Count > 0);
    public bool ShowHistoryPanel => ShowSidePanelToggle && IsHistoryPanelSelected && IsHistoryOpen;
    public bool ShowPinnedPanel => ShowSidePanelToggle && IsPinnedPanelSelected && IsHistoryOpen;
    public bool ShowSplitPanel => IsSplitPanelSelected && (!IsViewOnly || HasSplitTransactions);
    public bool ShowSidePanel => !IsProcessingSession && (ShowHistoryPanel || ShowPinnedPanel || ShowSplitPanel);
    public bool CanUseSplit => IsExpense && !IsProcessingSession;
    public bool CanEditAccount => !IsEditingSplitNode;
    public bool CanEditDate => !IsEditingSplitNode;
    public bool CanEditTransactionName => !IsGoal && !IsRepayment;
    public bool CanEditCategory => IsExpense && !IsRepayment && !IsCurrentSplitParent;
    public bool CanEditTags => !IsGoal && !IsRepayment && !IsCurrentSplitParent;
    public bool CanChangeTransactionType => !_isTransactionTypeLocked && !IsProcessingSession;
    public bool CanEditViewedTransaction => IsViewOnly && ViewedTransaction?.Tag?.IsSystemTag != true;
    public bool CanCloneViewedTransaction => CanEditViewedTransaction;
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
    public bool CanToggleBulk => _popupPurpose == TransactionPopupPurpose.AddNewTransaction;
    public TransactionVM LoadedTransaction { get; private set; } = null!;
    public TransactionVM PendingTransaction { get; private set; } = null!;
    public TransactionVM? ViewedTransaction { get; private set; }

    public bool ShowNoteField => !IsGoal && !IsRepayment;
    public bool ShowGoalField => IsGoal;
    public bool ShowRepaymentAccountField => IsRepayment;
    public bool ShowCategoryField => IsExpense;
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

        var persistedTags = TransactionCatalogProjection.ProjectNonSystemTags(allTags).ToList();

        if (persistedTags.Count == 0)
            return;

        var selectedTagId = SelectedTag?.Id;

        _orderedTags.Clear();
        _orderedTags.AddRange(persistedTags);

        RefreshTagCollections();
        if (selectedTagId is not null)
            SelectedTag = _orderedTags.FirstOrDefault(tag => tag.Id == selectedTagId.Value) ?? _orderedTags.FirstOrDefault();

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
                var resetGoalOrRepayment = IsGoal || IsRepayment;
                IsGoal = false;
                IsExpense = false;
                IsRepayment = false;
                if (resetGoalOrRepayment)
                {
                    IsExcludedFromBudget = false;
                    AmountText = 0m;
                }
            }
            else if (IsIncome)
            {
                IsExpense = true;
            }
        }
    }

    public bool IsProcessingSession => ProcessingTargets.Any();
    public bool ShowRightFormDivider => IsBulkMode || ShowSidePanel;
    public PopupMode PopupMode => IsViewOnly ? PopupMode.Functional
        : _popupPurpose is TransactionPopupPurpose.AddNewTransaction or TransactionPopupPurpose.AddRecurringTransaction
            or TransactionPopupPurpose.Processing
            ? PopupMode.Persist
            : PopupMode.Modify;
    public int? CurrentProcessingRecurringTransactionId => _currentProcessingRecurringTransactionId;
    private IEnumerable<TransactionVM> TransactionsToPersist => IsBulkMode
        ? _queuedTransactions
        : _isTransactionStateInitialized ? [_currentRootTransaction] : [];

    private int GetQueuedTransactionIndex(TransactionVM transaction) =>
        _queuedTransactions.Select((candidate, index) => (candidate, index))
            .FirstOrDefault(item => ReferenceEquals(item.candidate, transaction)).index;

    partial void OnIsBulkModeChanged(bool value)
    {
        if (value)
        {
            EnsureTransactionState();
            SyncPendingTransactionFromForm();
            var transaction = TransactionMappingHelper.CreatePending(_currentRootTransaction);
            _transactionFormOptions.Clear();
            _transactionFormOptions[transaction] = CaptureOptions();
            _messenger.Send(new TransactionBulkQueueResetMessage(
                true, [transaction], Accounts.FirstOrDefault(account => account.IsDefault) ?? Accounts.FirstOrDefault()),
                _messageToken);
        }
        else if (!IsProcessingSession)
        {
            _transactionFormOptions.Clear();
            _messenger.Send(new TransactionBulkQueueResetMessage(false, [], null), _messageToken);
        }

        RevalidateTransactions();
        OnPropertyChanged(nameof(ShowRightFormDivider));
    }

    private void HandleQueueStateChanged(TransactionBulkQueueStateChangedMessage message)
    {
        var shouldResetForm = IsBulkMode && !IsProcessingSession &&
                              _bulkQueueHadItems && message.Transactions.Count == 0;
        _queuedTransactions = message.Transactions;
        _bulkQueueHadItems = message.Transactions.Count > 0;
        _selectedQueuedTransaction = message.SelectedTransaction;
        _bulkQueueHasChanges = message.HasChanges;
        _bulkQueueAllValid = message.AllValid;
        if (IsProcessingSession && message.SelectedTransaction is not null)
        {
            _currentProcessingIndex = GetQueuedTransactionIndex(message.SelectedTransaction);
            _currentProcessingRecurringTransactionId =
                (CurrentProcessingTarget as RecurringTransactionVM)?.Id;
        }

        OnPropertyChanged(nameof(CanPersist));
        OnPropertyChanged(nameof(HasChanges));
        NotifyProcessingChanged();
        RefreshDuplicateWarningState();

        if (shouldResetForm)
            ResetEmptyBulkForm();
    }

    private void HandleLoadRequested(TransactionVM transaction)
    {
        LoadTransaction(transaction);
        if (IsBulkMode && !IsProcessingSession)
            SetPopupPurpose(TransactionPopupPurpose.AddNewTransaction);
    }

    private void RevalidateTransactions()
    {
        if (!_isLoadingTransaction)
            SyncPendingTransactionFromForm();

        foreach (var transaction in TransactionsToPersist)
        {
            var options = GetOptions(transaction);
            var splitValidation = RequestSplitValidation(transaction);
            transaction.Validate(new TransactionValidationContext(
                options.IsGoal, options.IsRepayment, options.IsRecurring, options.IsInstallments,
                _isRepaymentAmountInvalid, options.SelectedRecurringPeriod, options.RecurringTimeText,
                options.StartDate, options.InstallmentEndDate, transaction.Account,
                GetCurrentTagSpending(transaction, options), LoadedTransaction?.Id > 0,
                splitValidation.IsSuccess));
            RefreshWarningStates(transaction, options.IsRecurring);
        }

        OnPropertyChanged(nameof(CanPersist));
    }

    private decimal GetCurrentTagSpending(TransactionVM transaction, TransactionFormOptions options)
    {
        if (transaction.Type != TransactionType.Expense || options.IsRecurring || options.IsInstallments ||
            transaction.IsExcludedFromBudget || transaction.Tag is not { SpendingLimit: > 0m } tag)
            return 0m;

        try
        {
            var allocation = _appData.GetBudgetAllocationAsync().GetAwaiter().GetResult();
            var period = BudgetAllocationCalculator.ResolveCurrentPeriod(
                allocation.AllocationPeriod, transaction.OccurredOn.Date, allocation.PeriodStart);
            return _appData.GetTransactionsAsync().GetAwaiter().GetResult()
                .Where(log => log.Type == TransactionType.Expense && !log.IsForDeletion && !log.IsExcludedFromBudget)
                .Where(log => log.OccurredOn.Date >= period.Start && log.OccurredOn.Date <= period.End)
                .Where(log => log.TagId == tag.Id || log.Tag?.Id == tag.Id)
                .Sum(log => log.Amount);
        }
        catch
        {
            return 0m;
        }
    }

    public void InitializeRepaymentProcessing(IReadOnlyList<AccountVM> accounts)
    {
        InitializeProcessing();
        _processingRepayments.AddRange(accounts);
        SetPopupPurpose(TransactionPopupPurpose.Processing);
        InitializeProcessingQueue();
        NotifyProcessingChanged();
    }

    public void InitializeGoalProcessing(IReadOnlyList<SavingGoalVM> goals)
    {
        InitializeProcessing();
        _processingGoals.AddRange(goals);
        SetPopupPurpose(TransactionPopupPurpose.Processing);
        InitializeProcessingQueue();
        NotifyProcessingChanged();
    }

    public void InitializeRecurringProcessing(IReadOnlyList<RecurringTransactionVM> recurringTransactions)
    {
        InitializeProcessing();
        _processingRecurringTransactions.AddRange(recurringTransactions);
        SetPopupPurpose(TransactionPopupPurpose.Processing);
        InitializeProcessingQueue();
        NotifyProcessingChanged();
    }

    public async Task<TransactionPopupSubmissionResult> PersistProcessedItemsAsync()
    {
        return await FinishQueuedTransactionsAsync();
    }

    public void RemoveQueuedTransaction(TransactionVM transaction)
    {
        _messenger.Send(new TransactionBulkQueueRemoveRequestedMessage(transaction), _messageToken);
    }

    public async Task<TransactionPopupSubmissionResult> FinishQueuedTransactionsAsync(
        bool allowMaximumSpendingOverflow = false)
    {
        foreach (var transaction in _queuedTransactions.ToList())
        {
            _messenger.Send(new TransactionBulkQueueSelectRequestedMessage(transaction), _messageToken);
            var result = await SaveAsync(false, allowMaximumSpendingOverflow);
            if (!result.IsSuccess)
                return result;

            _transactionFormOptions.Remove(transaction);
            _messenger.Send(new TransactionBulkQueueRemoveRequestedMessage(transaction), _messageToken);
        }

        if (_queuedTransactions.Count == 0)
        {
            if (IsProcessingSession)
                ClearProcessing();
            return TransactionPopupSubmissionResult.Success();
        }

        _messenger.Send(new TransactionBulkQueueSelectRequestedMessage(_queuedTransactions[0]), _messageToken);
        OnPropertyChanged(nameof(CanPersist));
        return TransactionPopupSubmissionResult.Failure("Queued transactions could not be saved.");
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
        TryAdoptCurrentBulkTransaction();
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

        NotifyTransactionModeChanged();
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
        if (_popupPurpose == TransactionPopupPurpose.AddNewTransaction)
        {
            var message = _messenger.Send(new DashboardDailyDateRequestedMessage());
            if (message.HasReceivedResponse && message.Response is { } date)
                SelectedDate = date.Date;
        }
        await LoadBalanceUpdateCurrentAmountsAsync(cancellationToken);
        await RefreshDuplicateCandidatesAsync(cancellationToken);
        _isInitialized = true;
        return true;
    }


    public void RequestAddTag() =>
        _messenger.Send(new TransactionPopupAddTagRequestedMessage(ViewedTransaction?.Id ?? 0, AddTagOwnerToken));

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
                await LoadSplitTreeAsync(LoadedTransaction.Id);
                break;
            case TransactionPopupRequestKind.EditTransaction when _request.Transaction is { } transaction:
                InitializeView(transaction);
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
        _messenger.UnregisterAll(this);
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
        BeginChangeTracking();
        return true;
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

        NotifyTransactionModeChanged();
        RefreshActiveValidation(nameof(AmountText));
        NotifyFormStateChanged();
    }

    private void NotifyTransactionModeChanged()
    {
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
    }

    partial void OnIsExcludedFromBudgetChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBudgetExcluded));
        OnPropertyChanged(nameof(IsNeedsCategory));
        OnPropertyChanged(nameof(IsWantsCategory));
        OnPropertyChanged(nameof(IsInvestCategory));
        OnPropertyChanged(nameof(IsExcludedCategory));
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

    partial void OnIsSavingChanged(bool value) => NotifyFormStateChanged();

    partial void OnNameTextChanged(string value)
    {
        RefreshActiveValidation(nameof(NameText));
        NotifyFormStateChanged();
        TryAdoptCurrentBulkTransaction();
        _ = RefreshTransactionNameSuggestionsAsync();
    }

    partial void OnNoteTextChanged(string value)
    {
        NotifyFormStateChanged();
        TryAdoptCurrentBulkTransaction();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        RefreshActiveValidation(nameof(AmountText));
        RefreshAmountWarning();
        _ = RefreshExpenseCategoryAvailabilityAsync();
        NotifyFormStateChanged();
    }

    partial void OnSelectedExpenseCategoryChanged(ExpenseCategory value) => NotifyFormStateChanged();

    partial void OnSelectedGoalChanged(SavingGoalVM? value)
    {
        if (IsGoal)
        {
            SyncGoalUpdateName();
            SyncPendingTransactionFromForm();
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
        SetSelectedOccurredOn(state.Input.Date);
        SelectedExpenseCategory = state.Input.Category ?? ExpenseCategory.Needs;
        SelectedAccount = state.SelectedAccount;
        SelectedTag = state.SelectedTag;
        SelectedGoal = state.SelectedGoal;
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
        _currentRootTransaction = state.PendingTransaction;
        PendingTransaction = _currentRootTransaction;
        _isTransactionStateInitialized = true;
        var loaded = state.LoadedTransaction;
        _isLoadingTransaction = true;
        try
        {
            SetPopupPurpose(TransactionPopupPurpose.ViewTransaction);
            IsExpense = state.IsExpense;
            IsGoal = state.IsGoal;
            IsRepayment = state.IsRepayment;
            NameText = loaded.Name;
            AmountText = loaded.Amount;
            NoteText = loaded.Notes;
            SetSelectedOccurredOn(loaded.OccurredOn);
            SelectedExpenseCategory = loaded.ExpenseCategory ?? ExpenseCategory.Needs;
            IsPinned = loaded.IsPinned;
            IsIoU = loaded.IsIoU;
            ShouldAffectBalance = loaded.ShouldAffectBalance;
            IsExcludedFromBudget = loaded.IsExcludedFromBudget;
            SelectedAccount = state.SelectedAccount;
            SelectedTag = state.SelectedTag;
            SelectedGoal = state.SelectedGoal;
            SelectedRepaymentAccount = state.SelectedRepaymentAccount;
        }
        finally
        {
            _isLoadingTransaction = false;
        }

        _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
        ViewedTransaction = loaded;
        _isTransactionTypeLocked = true;
        RefreshTagCollections();
        ClearViewModeFeedback();
        OnPropertyChanged(nameof(CanChangeTransactionType));
        OnPropertyChanged(nameof(IsViewOnly));
        OnPropertyChanged(nameof(CanToggleBulk));
        OnPropertyChanged(nameof(PopupMode));
        OnPropertyChanged(nameof(CanEditViewedTransaction));
        OnPropertyChanged(nameof(CanCloneViewedTransaction));
        PublishSplitContext();
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

    public async Task BeginEditingViewedTransactionAsync()
    {
        if (ViewedTransaction is null || !CanEditViewedTransaction)
            return;

        SetPopupPurpose(TransactionPopupPurpose.EditTransaction);
        await EnsureTagsLoadedAsync();
        RefreshTagCollections();
        await LoadSplitTreeAsync(LoadedTransaction.Id);
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
        if (ViewedTransaction is null || SelectedAccount is null)
            throw new InvalidOperationException("The transaction edit is incomplete.");
        SyncPendingTransactionFromForm();
        var input = EditTransactionHelper.CreateInput(_currentRootTransaction);
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
            {
                IsExcludedFromBudget = false;
                AmountText = 0m;
            }
        }
        else if (SelectedSidePanel == TransactionPopupSidePanel.Split && !HasSplitTransactions)
        {
            SelectedSidePanel = TransactionPopupSidePanel.History;
        }

        NotifyTransactionTypeChanged();

        if (!CanUseIoU)
            IsIoU = false;

        RefreshTransactionTypeState(seedGeneratedBaseline: false);
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

        NotifyTransactionTypeChanged();

        if (value)
        {
            IsInstallments = false;
            IsIoU = false;
            SyncGoalUpdateName();
        }

        RefreshTransactionTypeState(seedGeneratedBaseline: value);
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

    private void NotifyTransactionTypeChanged()
    {
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
        OnPropertyChanged(nameof(CanUseSplit));
        OnPropertyChanged(nameof(ShowSplitPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
        PublishSplitContext();
    }

    private void RefreshTransactionTypeState(bool seedGeneratedBaseline)
    {
        RefreshAccounts();
        if (seedGeneratedBaseline)
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

    partial void OnSelectedRepaymentAccountChanged(AccountVM? oldValue, AccountVM? newValue)
    {
        _isRepaymentAmountInvalid = false;
        if (IsRepayment)
        {
            LoadRepaymentAmount();
            SyncRepaymentName();
            SyncPendingTransactionFromForm();
        }
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
                ? _orderedTags.FirstOrDefault(tag => tag.Id == tagId)
                : null;
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
    }

    public async Task<TransactionPopupSubmissionResult> SaveAsync(
        bool resetAfterSave,
        bool allowMaximumSpendingOverflow = false)
    {
        if (IsSaving)
            return TransactionPopupSubmissionResult.Failure("A transaction is already being saved.");

        EnsureTransactionState();
        SyncPendingTransactionFromForm();
        var splitValidation = RequestSplitValidation(_currentRootTransaction);
        if (!splitValidation.IsSuccess)
            return splitValidation;
        if (IsEditingSplitNode)
            LoadTransaction(_currentRootTransaction);

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

                var recurringType = GetRecurringTransactionType(input);
                var recurringName = await BuildRecurringTransactionNameAsync(input);

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
                    ResetAfterSaveAndCreateNew();

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
                var splitResult = await PersistSplitTreeAsync(persistedTransactionId.Value);
                if (!splitResult.IsSuccess)
                    return splitResult;

            }
            else
            {
                if (!TryNormalizeRecurringTime(input.RecurringPeriod, input.RecurringTimeText, out var recurringTime))
                    return TransactionPopupSubmissionResult.Failure(GetRecurringTimeValidationMessage(input.RecurringPeriod));

                var recurringType = GetRecurringTransactionType(input);

                RecurringTransaction recurring;
                if (input.EditingRecurringTransactionId is > 0)
                {
                    recurring = await _appData.GetRecurringTransactionByIdAsync(input.EditingRecurringTransactionId.Value) ?? new RecurringTransaction();
                }
                else
                {
                    recurring = new RecurringTransaction();
                }

                recurring.Name = await BuildRecurringTransactionNameAsync(input);
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
                ResetAfterSaveAndCreateNew();

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
                .Where(transaction => !transaction.IsForDeletion && !IsLoadedTransaction(transaction))
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

    public async Task<bool> HasSimilarQueuedTransactionsAsync(CancellationToken cancellationToken = default)
    {
        await RefreshDuplicateCandidatesAsync(cancellationToken);
        return _queuedTransactions.Any(HasRealtimeDuplicate);
    }

    private async Task RefreshDuplicateCandidatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _duplicateCandidates = (await _appData.GetTransactionsAsync(cancellationToken))
                .Where(transaction => !transaction.IsForDeletion)
                .ToArray();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception,
                "load duplicate transaction candidates");
        }

        try
        {
            _duplicateGoalUpdateTagIds = (await _appData.GetTagsAsync(cancellationToken))
                .Where(tag => string.Equals(
                    tag.Name?.Trim(),
                    GoalUpdateTransactionSupport.GoalUpdateTagName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(tag => tag.Id)
                .ToHashSet();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception,
                "load duplicate transaction classifications");
        }

        RefreshDuplicateWarningState();
    }

    private bool HasRealtimeDuplicate(TransactionVM candidate)
    {
        var options = GetOptions(candidate);
        if (options.IsRecurring || string.IsNullOrWhiteSpace(GetDuplicateCandidateName(candidate, options)))
            return false;

        return _duplicateCandidates.Any(existing => IsSimilarPersistedTransaction(existing, candidate, options)) ||
               _queuedTransactions.Any(peer =>
                   !ReferenceEquals(peer, candidate) && IsSimilarQueuedTransaction(peer, candidate, options));
    }

    private bool IsSimilarPersistedTransaction(
        Transaction existing,
        TransactionVM candidate,
        TransactionFormOptions options)
    {
        if (existing.IsForDeletion || IsLoadedTransaction(existing) ||
            existing.OccurredOn.Date != candidate.OccurredOn.Date ||
            existing.Type != candidate.Type ||
            existing.SourceAccountId != candidate.SourceAccountId ||
            !IsSameTransactionName(existing.Name, GetDuplicateCandidateName(candidate, options)) ||
            !IsSimilarAmount(existing.Amount, candidate.Amount))
        {
            return false;
        }

        return candidate.Type != TransactionType.Expense ||
               IsGoalUpdateExpenseLog(existing, _duplicateGoalUpdateTagIds) == options.IsGoal;
    }

    private bool IsSimilarQueuedTransaction(
        TransactionVM existing,
        TransactionVM candidate,
        TransactionFormOptions candidateOptions)
    {
        var existingOptions = GetOptions(existing);
        return !existingOptions.IsRecurring &&
               existing.OccurredOn.Date == candidate.OccurredOn.Date &&
               existing.Type == candidate.Type &&
               existing.SourceAccountId == candidate.SourceAccountId &&
               IsSameTransactionName(
                   GetDuplicateCandidateName(existing, existingOptions),
                   GetDuplicateCandidateName(candidate, candidateOptions)) &&
               IsSimilarAmount(existing.Amount, candidate.Amount) &&
               (candidate.Type != TransactionType.Expense || existingOptions.IsGoal == candidateOptions.IsGoal);
    }

    private string GetDuplicateCandidateName(TransactionVM candidate, TransactionFormOptions options)
    {
        if (!options.IsGoal)
            return candidate.Name.Trim();

        var goalName = candidate.GoalId is int goalId
            ? Goals.FirstOrDefault(goal => goal.Id == goalId)?.Name
            : null;
        return string.IsNullOrWhiteSpace(goalName) ? string.Empty : BuildGoalUpdateName(goalName);
    }

    private void RefreshDuplicateWarningState()
    {
        if (_isRefreshingDuplicateWarnings)
            return;

        _isRefreshingDuplicateWarnings = true;
        try
        {
            foreach (var transaction in _queuedTransactions)
                RefreshWarningStates(transaction, GetOptions(transaction).IsRecurring);

            RefreshFieldFeedback();
        }
        finally
        {
            _isRefreshingDuplicateWarnings = false;
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
        SetSelectedOccurredOn(DateTime.Now);
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
        SelectedTag = null;
        SelectedGoal = Goals.FirstOrDefault();
        SelectedRepaymentAccount = RepaymentAccounts.FirstOrDefault();
        ClearTransactionNameSuggestions();
    }

    private void ResetEmptyBulkForm()
    {
        _isResettingBulkForm = true;
        try
        {
            _transactionFormOptions.Clear();
            SetTransactionState(new TransactionVM());
            ResetForm(false);
            SyncPendingTransactionFromForm();
            LoadedTransaction = TransactionMappingHelper.CreateLoaded(PendingTransaction);
            _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
        }
        finally
        {
            _isResettingBulkForm = false;
        }
    }

    private void TryAdoptCurrentBulkTransaction()
    {
        if (!IsBulkMode || IsProcessingSession || _isResettingBulkForm || _isLoadingTransaction ||
            !_isTransactionStateInitialized || _queuedTransactions.Count > 0 ||
            string.IsNullOrWhiteSpace(NameText) && AmountText == 0m && string.IsNullOrWhiteSpace(NoteText))
            return;

        _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
        _messenger.Send(new TransactionBulkQueueAdoptRequestedMessage(_currentRootTransaction), _messageToken);
    }

    private void SetSelectedOccurredOn(DateTime occurredOn)
    {
        SelectedDate = occurredOn.Date;
        SelectedTime = occurredOn.TimeOfDay;
    }

    private void ResetAfterSaveAndCreateNew()
    {
        AmountText = 0m;
        NameText = string.Empty;
        NoteText = string.Empty;
        SelectedTag = null;
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
        else if (!IsRepayment && !IsCurrentSplitParent)
        {
            category = IsExpense ? SelectedExpenseCategory : null;
            tagId = SelectedTag?.Id;
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
            SelectedDate.Date.Add(SelectedTime),
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
            .Select(TransactionCatalogProjection.ProjectAccount)
            .ToArray();
        var tags = TransactionCatalogProjection.ProjectNonSystemTags(await _appData.GetTagsAsync(cancellationToken)).ToArray();
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

            var snapshot = await BuildBudgetAllocationSnapshotAsync(allocation, SelectedDate);
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

    private void RefreshTagCollections()
    {
        var selectedTagId = SelectedTag?.Id;

        _isUpdatingTagCollections = true;

        try
        {
            if (IsViewOnly)
            {
                ReplaceCollection(Tags, SelectedTag is null ? [] : [SelectedTag]);
                return;
            }

            ReplaceCollection(Tags, _orderedTags);

            if (selectedTagId is null)
                return;

            SelectedTag = _orderedTags.FirstOrDefault(tag => tag.Id == selectedTagId.Value);
        }
        finally
        {
            _isUpdatingTagCollections = false;
        }
    }

    private static RecurringTransactionType GetRecurringTransactionType(QuickTransactionInput input) =>
        input.IsGoal
            ? RecurringTransactionType.GoalUpdate
            : input.IsExpense
                ? RecurringTransactionType.Expense
                : RecurringTransactionType.Income;

    private async Task<string> BuildRecurringTransactionNameAsync(QuickTransactionInput input)
    {
        if (input.IsGoal && input.GoalId is not null)
        {
            var goal = await _appData.GetSavingGoalByIdAsync(input.GoalId.Value);
            return BuildGoalUpdateName(goal?.Name ?? string.Empty);
        }

        return input.IsInstallments
            ? BuildInstallmentRecurringName(input.Name)
            : BuildExpenseName(
                input.Name,
                input.Note,
                input.IsExpense ? "Recurring Expense" : "Recurring Income");
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
        if (log.SourceAccountId != input.AccountId ||
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
        return log.SourceAccountId == input.AccountId &&
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
            .Where(log => !log.IsForDeletion)
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

    private TransactionFormOptions CaptureOptions() => new(
        IsGoal,
        IsRepayment,
        IsRecurring,
        IsInstallments,
        SelectedRecurringPeriod,
        RecurringTimeText ?? string.Empty,
        StartDate.Date,
        InstallmentEndDate.Date);

    private TransactionFormOptions GetOptions(TransactionVM root)
    {
        if (_transactionFormOptions.TryGetValue(root, out var options))
            return options;

        return new TransactionFormOptions(
            root.GoalId is > 0,
            root.RepaymentAccountId is > 0,
            false,
            false,
            RecurringPeriod.Monthly,
            string.Empty,
            root.OccurredOn == default ? DateTime.Today : root.OccurredOn.Date,
            root.OccurredOn == default ? DateTime.Today : root.OccurredOn.Date);
    }

    private void NotifyFormStateChanged()
    {
        if (_isLoadingTransaction)
            return;

        if (!_isTransactionStateInitialized)
        {
            NotifyFormDependenciesChanged();
            return;
        }

        SyncPendingTransactionFromForm();
        if (ReferenceEquals(PendingTransaction, _currentRootTransaction))
            _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
        RevalidateTransactions();
        NotifyFormDependenciesChanged();
        RefreshDuplicateWarningState();
    }

    private void NotifyFormDependenciesChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ShowCategoryImpact));
        OnPropertyChanged(nameof(ShowAccountImpact));
        OnPropertyChanged(nameof(CategoryCurrent));
        OnPropertyChanged(nameof(CategoryToBe));
        OnPropertyChanged(nameof(AccountCurrent));
        OnPropertyChanged(nameof(AccountToBe));
        RefreshBalanceUpdate();
        NotifyTransactionWarningsChanged();
        OnPropertyChanged(nameof(IsNeedsCategory));
        OnPropertyChanged(nameof(IsWantsCategory));
        OnPropertyChanged(nameof(IsInvestCategory));
        OnPropertyChanged(nameof(IsExcludedCategory));
        OnPropertyChanged(nameof(CanEditCategory));
        OnPropertyChanged(nameof(CanEditTags));
        OnPropertyChanged(nameof(ShowSplitPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
        OnPropertyChanged(nameof(ShowRightFormDivider));
        OnPropertyChanged(nameof(CanEditAccount));
        OnPropertyChanged(nameof(CanEditDate));
        PublishSplitContext();
    }

    private async Task<TransactionPopupSubmissionResult> PersistSplitTreeAsync(int rootTransactionId)
    {
        var account = await _appData.GetAccountByIdAsync(_currentRootTransaction.SourceAccountId);
        if (account is null)
            return TransactionPopupSubmissionResult.Failure("Please select a valid account.");

        var existing = (await _appData.GetTransactionsAsync(CancellationToken.None))
            .Where(transaction => !transaction.IsForDeletion)
            .ToDictionary(transaction => transaction.Id);
        var existingRootChildIds = existing.Values
            .Where(transaction => transaction.ParentTransactionId == rootTransactionId)
            .Select(transaction => transaction.Id)
            .ToHashSet();
        var retainedIds = new HashSet<int>();

        foreach (var child in _currentRootTransaction.ChildTransactions)
        {
            var childEntity = await PersistSplitNodeAsync(child, rootTransactionId, account, existing, retainedIds);
            if (childEntity is null)
                continue;

            foreach (var grandchild in child.ChildTransactions)
                await PersistSplitNodeAsync(grandchild, childEntity.Id, account, existing, retainedIds);
        }

        foreach (var transaction in existing.Values.Where(transaction =>
                     transaction.ParentTransactionId is not null &&
                     !retainedIds.Contains(transaction.Id) &&
                     (transaction.ParentTransactionId == rootTransactionId ||
                      existingRootChildIds.Contains(transaction.ParentTransactionId.Value))))
        {
            transaction.IsForDeletion = true;
            _appData.UpdateTransaction(transaction);
        }

        await _appData.SaveChangesAsync();
        return TransactionPopupSubmissionResult.Success();
    }

    private async Task<Transaction?> PersistSplitNodeAsync(
        TransactionVM node,
        int parentTransactionId,
        Account account,
        IReadOnlyDictionary<int, Transaction> existing,
        ISet<int> retainedIds)
    {
        if (node.Amount <= 0m)
            return null;

        var tag = node.IsLeaf && node.Tag is { Id: > 0 }
            ? await _appData.GetTagByIdAsync(node.Tag.Id)
            : null;
        var transaction = node.Id > 0 && existing.TryGetValue(node.Id, out var persisted)
            ? persisted
            : new Transaction();

        transaction.Type = _currentRootTransaction.Type;
        transaction.SourceAccountId = _currentRootTransaction.SourceAccountId;
        transaction.Account = account;
        transaction.Name = node.Name.Trim();
        transaction.Amount = node.Amount;
        transaction.OccurredOn = node.OccurredOn == default ? _currentRootTransaction.OccurredOn : node.OccurredOn;
        transaction.Notes = node.Notes;
        transaction.ExpenseCategory = node.IsLeaf ? node.ExpenseCategory : null;
        transaction.Tag = tag;
        transaction.TagId = tag?.Id;
        transaction.ParentTransactionId = parentTransactionId;
        transaction.IsIoU = node.IsIoU;
        transaction.ShouldAffectBalance = node.ShouldAffectBalance;
        transaction.IsExcludedFromBudget = node.IsExcludedFromBudget;
        transaction.IsForDeletion = false;

        if (transaction.Id > 0)
            _appData.UpdateTransaction(transaction);
        else
        {
            await _appData.AddTransactionAsync(transaction);
            await _appData.SaveChangesAsync();
        }

        if (transaction.Id > 0)
        {
            node.Id = transaction.Id;
            retainedIds.Add(transaction.Id);
        }

        return transaction;
    }

    private async Task LoadSplitTreeAsync(int rootTransactionId)
    {
        var byParent = (await _appData.GetTransactionsAsync(CancellationToken.None))
            .Where(transaction => !transaction.IsForDeletion && transaction.ParentTransactionId is not null)
            .GroupBy(transaction => transaction.ParentTransactionId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        LoadedTransaction.ChildTransactions.Clear();
        if (byParent.TryGetValue(rootTransactionId, out var children))
        {
            foreach (var child in children)
            {
                var childViewModel = CreateSplitTransactionViewModel(child);
                if (byParent.TryGetValue(child.Id, out var grandchildren))
                    foreach (var grandchild in grandchildren)
                        childViewModel.ChildTransactions.Add(CreateSplitTransactionViewModel(grandchild));
                LoadedTransaction.ChildTransactions.Add(childViewModel);
            }
        }

        _currentRootTransaction = TransactionMappingHelper.CreatePending(LoadedTransaction);
        PendingTransaction = _currentRootTransaction;
        _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
        OnPropertyChanged(nameof(PendingTransaction));
        PublishSplitContext();
        NotifyFormDependenciesChanged();
    }

    private static TransactionVM CreateSplitTransactionViewModel(Transaction transaction) => new()
    {
        Id = transaction.Id,
        Type = transaction.Type,
        SourceAccountId = transaction.SourceAccountId,
        Account = transaction.Account is null ? new AccountVM() : new AccountVM { Id = transaction.Account.Id, Name = transaction.Account.Name },
        Name = transaction.Name,
        Amount = transaction.Amount,
        OccurredOn = transaction.OccurredOn,
        Notes = transaction.Notes,
        ExpenseCategory = transaction.ExpenseCategory,
        Tag = transaction.Tag is null ? null : new TagVM { Id = transaction.Tag.Id, Name = transaction.Tag.Name, HexCode = transaction.Tag.HexCode },
        ParentTransactionId = transaction.ParentTransactionId,
        IsIoU = transaction.IsIoU,
        ShouldAffectBalance = transaction.ShouldAffectBalance,
        IsExcludedFromBudget = transaction.IsExcludedFromBudget
    };

    partial void OnSelectedSidePanelChanged(TransactionPopupSidePanel value)
    {
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(ShowPinnedPanel));
        OnPropertyChanged(nameof(ShowSplitPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
        OnPropertyChanged(nameof(ShowRightFormDivider));
        PublishSplitContext();
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

    private void RefreshBalanceUpdate()
    {
        OnPropertyChanged(nameof(ShowBalanceUpdate));
        OnPropertyChanged(nameof(ShowBalanceUpdateCategories));
        OnPropertyChanged(nameof(ShowBalanceUpdateTags));
        OnPropertyChanged(nameof(BalanceUpdateAccountName));
        OnPropertyChanged(nameof(BalanceUpdateAccountCurrent));
        OnPropertyChanged(nameof(BalanceUpdateAccountToBe));

        if (!ShowBalanceUpdate)
        {
            ReplaceCollection(CategoryBalanceUpdates, []);
            ReplaceCollection(TagBalanceUpdates, []);
            return;
        }

        var leaves = GetBalanceUpdateLeaves(_currentRootTransaction).ToArray();
        ReplaceCollection(
            CategoryBalanceUpdates,
            leaves
                .Where(leaf => leaf is { Type: TransactionType.Expense, IsExcludedFromBudget: false, ExpenseCategory: not null })
                .GroupBy(leaf => leaf.ExpenseCategory!.Value)
                .OrderBy(group => group.Key)
                .Select(group => new BalanceUpdateItem(
                    TransactionCalculationHelper.GetExpenseCategoryLabel(group.Key),
                    _balanceUpdateCategoryCurrentAmounts.GetValueOrDefault(group.Key),
                    _balanceUpdateCategoryCurrentAmounts.GetValueOrDefault(group.Key) + group.Sum(leaf => leaf.Amount))));

        ReplaceCollection(
            TagBalanceUpdates,
            leaves
                .Where(leaf => leaf.Tag is not null)
                .GroupBy(leaf => leaf.Tag!.Id)
                .OrderBy(group => group.First().Tag!.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var current = _balanceUpdateTagCurrentAmounts.GetValueOrDefault(group.Key);
                    return new BalanceUpdateItem(group.First().Tag!.Name, current, current + group.Sum(leaf => leaf.Amount));
                }));
    }

    private decimal CalculateBalanceUpdateAccountToBe()
    {
        if (!ShowBalanceUpdate)
            return 0m;

        var account = _currentRootTransaction.Account;
        return GetBalanceUpdateLeaves(_currentRootTransaction).Aggregate(
            TransactionCalculationHelper.GetAccountCurrent(account),
            (current, leaf) => current + (account.IsCredit
                ? leaf.Type == TransactionType.Income ? -leaf.Amount : leaf.Amount
                : leaf.Type == TransactionType.Income ? leaf.Amount : -leaf.Amount));
    }

    private static IEnumerable<TransactionVM> GetBalanceUpdateLeaves(TransactionVM transaction)
    {
        if (transaction.ChildTransactions.Count == 0)
        {
            yield return transaction;
            yield break;
        }

        foreach (var child in transaction.ChildTransactions)
            foreach (var leaf in GetBalanceUpdateLeaves(child))
                yield return leaf;
    }

    private async Task LoadBalanceUpdateCurrentAmountsAsync(CancellationToken cancellationToken)
    {
        if (!_isTransactionStateInitialized)
            return;

        var leaves = GetBalanceUpdateLeaves(_currentRootTransaction).ToArray();
        try
        {
            var allocation = await _appData.GetBudgetAllocationAsync(cancellationToken);
            var snapshot = await BuildBudgetAllocationSnapshotAsync(allocation, SelectedDate);
            _balanceUpdateCategoryCurrentAmounts = leaves
                .Where(leaf => leaf is { Type: TransactionType.Expense, IsExcludedFromBudget: false, ExpenseCategory: not null })
                .Select(leaf => leaf.ExpenseCategory!.Value)
                .Distinct()
                .ToDictionary(category => category, category => TransactionCalculationHelper.GetCategoryState(snapshot, category).Spent);
        }
        catch
        {
            _balanceUpdateCategoryCurrentAmounts = new Dictionary<ExpenseCategory, decimal>();
        }

        try
        {
            _balanceUpdateTagCurrentAmounts = (await _appData.GetTransactionsAsync(cancellationToken))
                .Where(transaction => !transaction.IsForDeletion && transaction.TagId.HasValue)
                .GroupBy(transaction => transaction.TagId!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(transaction => transaction.Amount));
        }
        catch
        {
            _balanceUpdateTagCurrentAmounts = new Dictionary<int, decimal>();
        }

        RefreshBalanceUpdate();
    }

    private ValidationResult? ToInstallmentValidationResult()
    {
        var result = RecurringTransactionValidationHelper.ValidateInstallments(
            SelectedRecurringPeriod, RecurringTimeText, InstallmentEndDate, StartDate);
        return result.IsValid ? ValidationResult.Success : new ValidationResult(result.ErrorMessage);
    }

    private void NotifyTransactionWarningsChanged()
    {
        RefreshFieldFeedback();
    }

    private void RefreshFieldFeedback()
    {
        var context = CreateValidationContext();
        var duplicateWarning = _isTransactionStateInitialized && HasRealtimeDuplicate(PendingTransaction)
            ? new TransactionWarning(DuplicateWarningMessage, true)
            : null;
        NameFeedback.Update(ToFeedback(ValidateNameText(NameText, context))
            .Append(duplicateWarning)
            .OfType<TransactionWarning>());
        TimeFeedback.Update(new[] { duplicateWarning }.OfType<TransactionWarning>());
        AmountFeedback.Update(ToFeedback(ValidateAmountText(AmountText, context))
            .Append(string.IsNullOrWhiteSpace(AmountWarningHint)
                ? null
                : new TransactionWarning(AmountWarningHint, true))
            .OfType<TransactionWarning>());
        AccountFeedback.Update(ToFeedback(ValidateSelectedAccount(SelectedAccount, context)));
        GoalFeedback.Update(ToFeedback(ValidateSelectedGoal(SelectedGoal, context)));
        RecurrenceFeedback.Update(ToFeedback(ValidateRecurringTimeText(RecurringTimeText, context))
            .Concat(ToFeedback(IsInstallments ? ToInstallmentValidationResult() : ValidationResult.Success)));
        CreditAccountFeedback.Update(IsRepayment && SelectedRepaymentAccount is null
            ? [new TransactionWarning("Please choose a credit account.", false)]
            : []);
    }

    private static IEnumerable<TransactionWarning> ToFeedback(ValidationResult? result)
    {
        if (!string.IsNullOrWhiteSpace(result?.ErrorMessage))
            yield return new TransactionWarning(result.ErrorMessage, false);
    }

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

    private string GetDailyAllowanceWarning() => GetDailyAllowanceWarning(
        IsExpense,
        IsRecurringTransactionMode,
        IsExcludedFromBudget,
        AmountText,
        SelectedDate);

    private string GetDailyAllowanceWarning(
        bool isExpense,
        bool isRecurring,
        bool isExcludedFromBudget,
        decimal amount,
        DateTime occurredOn)
    {
        if (!isExpense || isRecurring || isExcludedFromBudget || amount <= 0m)
            return string.Empty;

        try
        {
            var allocation = _appData.GetBudgetAllocationAsync().GetAwaiter().GetResult();
            var spent = BudgetEffectiveTransactionFilter
                .Select(_appData.GetTransactionsAsync().GetAwaiter().GetResult())
                .Where(transaction => !IsLoadedTransaction(transaction))
                .Where(transaction => transaction.Type == TransactionType.Expense &&
                                      transaction.OccurredOn.Date == occurredOn.Date)
                .Sum(transaction => transaction.Amount);
            var allowance = BudgetAllocationCalculator.CalculateDailyAllowance(
                allocation,
                occurredOn.Date,
                CalculateBudgetAvailableBaseAsync(allocation).GetAwaiter().GetResult());

            return spent + amount > allowance ? "Over Daily Allowance" : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void RefreshWarningStates(TransactionVM transaction, bool isRecurring)
    {
        var hasDailyAllowanceWarning = !string.IsNullOrWhiteSpace(GetDailyAllowanceWarning(
            transaction.Type == TransactionType.Expense,
            isRecurring,
            transaction.IsExcludedFromBudget,
            transaction.Amount,
            transaction.OccurredOn));
        transaction.HasWarnings = hasDailyAllowanceWarning || HasRealtimeDuplicate(transaction);

        foreach (var child in transaction.ChildTransactions)
            RefreshWarningStates(child, isRecurring);
    }

    private bool IsLoadedTransaction(Transaction transaction) =>
        _isTransactionStateInitialized &&
        LoadedTransaction.Id > 0 &&
        transaction.Id == LoadedTransaction.Id;

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
        _currentRootTransaction = TransactionMappingHelper.CreatePending(LoadedTransaction);
        PendingTransaction = _currentRootTransaction;
        _isTransactionStateInitialized = true;
        _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
        OnPropertyChanged(nameof(PendingTransaction));
        PublishSplitContext();
    }

    public void LoadTransaction(TransactionVM transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (_isTransactionStateInitialized)
        {
            SyncPendingTransactionFromForm();
            if (ReferenceEquals(PendingTransaction, _currentRootTransaction))
                _transactionFormOptions[_currentRootTransaction] = CaptureOptions();
            if (!ContainsTransaction(_currentRootTransaction, transaction))
                _currentRootTransaction = transaction;
        }
        else
        {
            _currentRootTransaction = transaction;
            _isTransactionStateInitialized = true;
        }

        var transactionName = transaction.Name;
        var transactionAmount = transaction.Amount;
        PendingTransaction = transaction;
        var options = GetOptions(_currentRootTransaction);
        var isRoot = ReferenceEquals(transaction, _currentRootTransaction);
        _isLoadingTransaction = true;
        try
        {
            IsExpense = transaction.Type != TransactionType.Income;
            IsGoal = isRoot && options.IsGoal;
            IsRepayment = isRoot && options.IsRepayment;
            IsRecurring = isRoot && options.IsRecurring;
            IsInstallments = isRoot && options.IsInstallments;
            SelectedRecurringPeriod = options.SelectedRecurringPeriod;
            RecurringTimeText = options.RecurringTimeText;
            StartDate = options.StartDate;
            InstallmentEndDate = options.InstallmentEndDate;
            NameText = transactionName;
            AmountText = transactionAmount;
            NoteText = transaction.Notes;
            SetSelectedOccurredOn(transaction.OccurredOn == default ? DateTime.Now : transaction.OccurredOn);
            SelectedExpenseCategory = transaction.ExpenseCategory ?? ExpenseCategory.Needs;
            SelectedAccount = Accounts.FirstOrDefault(account => account.Id == transaction.SourceAccountId) ??
                              transaction.Account;
            SelectedTag = transaction.Tag is null
                ? null
                : _orderedTags.FirstOrDefault(tag => tag.Id == transaction.Tag.Id) ?? transaction.Tag;
            SelectedGoal = isRoot && transaction.GoalId is int goalId
                ? Goals.FirstOrDefault(goal => goal.Id == goalId)
                : null;
            SelectedRepaymentAccount = isRoot && transaction.RepaymentAccountId is int repaymentId
                ? RepaymentAccounts.FirstOrDefault(account => account.Id == repaymentId)
                : null;
            IsPinned = transaction.IsPinned;
            IsIoU = transaction.IsIoU;
            ShouldAffectBalance = transaction.ShouldAffectBalance;
            IsExcludedFromBudget = transaction.IsExcludedFromBudget;
        }
        finally
        {
            _isLoadingTransaction = false;
        }

        OnPropertyChanged(nameof(PendingTransaction));
        RevalidateTransactions();
        NotifyFormDependenciesChanged();
    }

    private static bool ContainsTransaction(TransactionVM root, TransactionVM transaction) =>
        ReferenceEquals(root, transaction) || root.ChildTransactions.Any(child =>
            ContainsTransaction(child, transaction));

    private void HandleSplitChanged(TransactionVM root)
    {
        if (!ReferenceEquals(root, _currentRootTransaction) ||
            _isLoadingTransaction || _isSyncingTransaction || IsSaving)
            return;

        _isSyncingTransaction = true;
        try
        {
            LoadTransaction(PendingTransaction);
        }
        finally
        {
            _isSyncingTransaction = false;
        }
    }

    private TransactionPopupSubmissionResult RequestSplitValidation(TransactionVM root)
    {
        var message = _messenger.Send(new TransactionSplitValidationRequestedMessage(root), _messageToken);
        return message.HasReceivedResponse
            ? message.Response
            : TransactionPopupSubmissionResult.Success();
    }

    private void PublishSplitContext()
    {
        if (!_isTransactionStateInitialized || _isLoadingTransaction)
            return;

        _messenger.Send(new TransactionSplitContextChangedMessage(
            _currentRootTransaction,
            IsSplitPanelSelected,
            !IsViewOnly && CanUseSplit && IsSplitPanelSelected,
            ReferenceEquals(PendingTransaction, _currentRootTransaction)), _messageToken);
    }

    private void SeedGeneratedAddBaseline()
    {
        if (!IsGeneratedAddMode)
            return;

        SetTransactionState(CreateGeneratedAddTransaction());
    }

    private void SyncPendingTransactionFromForm()
    {
        if (!_isTransactionStateInitialized || _isLoadingTransaction || _isSyncingTransaction)
            return;

        _isSyncingTransaction = true;
        try
        {
            SyncTransactionFromForm(PendingTransaction);
        }
        finally
        {
            _isSyncingTransaction = false;
        }
    }

    private void SyncTransactionFromForm(TransactionVM transaction)
    {
        transaction.Type = IsExpense || IsGoal || IsRepayment ? TransactionType.Expense : TransactionType.Income;
        transaction.SourceAccountId = SelectedAccount?.Id ?? 0;
        transaction.GoalId = IsGoal ? SelectedGoal?.Id : null;
        transaction.RepaymentAccountId = IsRepayment ? SelectedRepaymentAccount?.Id : null;
        transaction.Account = SelectedAccount ?? transaction.Account;
        transaction.Name = NameText;
        transaction.Amount = AmountText;
        transaction.OccurredOn = SelectedDate.Date.Add(SelectedTime);
        transaction.Notes = NoteText;
        var isSplitParent = _isTransactionStateInitialized &&
                            _currentRootTransaction.ChildTransactions.Count > 0 &&
                            (ReferenceEquals(transaction, _currentRootTransaction) ||
                             transaction.ChildTransactions.Count > 0);
        transaction.ExpenseCategory = isSplitParent
            ? null
            : IsGoal ? ExpenseCategory.Savings : IsExpense ? SelectedExpenseCategory : null;
        transaction.Tag = isSplitParent || IsGoal || IsRepayment ? null : SelectedTag;
        transaction.IsPinned = IsPinned;
        transaction.IsIoU = IsIoU;
        transaction.ShouldAffectBalance = ShouldAffectBalance;
        transaction.IsExcludedFromBudget = IsBudgetExcluded;
        if (ReferenceEquals(transaction, _currentRootTransaction))
            SyncSplitDescendantContext();
    }

    private void SyncSplitDescendantContext()
    {
        foreach (var child in _currentRootTransaction.ChildTransactions)
        {
            child.SourceAccountId = _currentRootTransaction.SourceAccountId;
            child.Account = _currentRootTransaction.Account;
            child.OccurredOn = _currentRootTransaction.OccurredOn;
            foreach (var grandchild in child.ChildTransactions)
            {
                grandchild.SourceAccountId = _currentRootTransaction.SourceAccountId;
                grandchild.Account = _currentRootTransaction.Account;
                grandchild.OccurredOn = _currentRootTransaction.OccurredOn;
            }
        }
    }

    private bool IsGeneratedAddMode => !_isLoadingTransaction &&
                                       _popupPurpose == TransactionPopupPurpose.AddNewTransaction &&
                                       !IsBulkMode &&
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
        OccurredOn = SelectedDate.Date.Add(SelectedTime),
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
        SetSelectedOccurredOn(item.Date);
        IsPinned = item.IsPinned;
        SelectedAccount = Accounts.FirstOrDefault(source => source.Id == item.AccountId) ??
                                 SelectedAccount;

        if (item.IsExpense)
        {
            SelectedExpenseCategory = item.Category ?? SelectedExpenseCategory;
            SelectedTag = item.TagId is int tagId
                ? _orderedTags.FirstOrDefault(tag => tag.Id == tagId)
                : null;
        }

        ClearTransactionNameSuggestions();
        NotifyFormStateChanged();
    }

    private bool IsCurrentInputValid()
    {
        if (!_isTransactionStateInitialized)
        {
            var transaction = new TransactionVM();
            SyncTransactionFromForm(transaction);
            var validationOptions = CaptureOptions();
            transaction.Validate(new TransactionValidationContext(
                validationOptions.IsGoal, validationOptions.IsRepayment, validationOptions.IsRecurring,
                validationOptions.IsInstallments, _isRepaymentAmountInvalid,
                validationOptions.SelectedRecurringPeriod, validationOptions.RecurringTimeText,
                validationOptions.StartDate, validationOptions.InstallmentEndDate, transaction.Account,
                GetCurrentTagSpending(transaction, validationOptions), false, true));
            return transaction.IsValid;
        }

        SyncPendingTransactionFromForm();
        var options = GetOptions(_currentRootTransaction);
        var splitValidation = RequestSplitValidation(_currentRootTransaction);
        _currentRootTransaction.Validate(new TransactionValidationContext(
            options.IsGoal, options.IsRepayment, options.IsRecurring, options.IsInstallments,
            _isRepaymentAmountInvalid, options.SelectedRecurringPeriod, options.RecurringTimeText,
            options.StartDate, options.InstallmentEndDate, _currentRootTransaction.Account,
            GetCurrentTagSpending(_currentRootTransaction, options), LoadedTransaction?.Id > 0,
            splitValidation.IsSuccess));
        return _currentRootTransaction.IsValid;
    }

    private ValidationContext CreateValidationContext()
    {
        return new ValidationContext(this);
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
        OnPropertyChanged(nameof(CanPersist));
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
        OnPropertyChanged(nameof(CanPersist));
        NotifyTransactionWarningsChanged();
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
            viewModel.GetAmountValidationAccount(),
            viewModel.LoadedTransaction?.Id > 0);
        if (!amountValidation.IsValid)
            return ToValidationResult(amountValidation);

        if (!viewModel.TryValidateSpendingAmountAgainstTagLimit(amountToValidate, out var tagLimitValidationMessage))
            return new ValidationResult(tagLimitValidationMessage);

        return ValidationResult.Success;
    }

    private AccountVM? GetAmountValidationAccount()
    {
        if (SelectedAccount is not { } account)
            return null;

        if (LoadedTransaction?.Id is not > 0)
            return account;

        if (account.AccountType is not (AccountType.Cash or AccountType.Checking))
            return null;

        var balance = account.Balance;
        if (LoadedTransaction.SourceAccountId == account.Id && LoadedTransaction.Type == TransactionType.Expense)
            balance += LoadedTransaction.Amount;

        return new AccountVM { AccountType = account.AccountType, Balance = balance };
    }

    private bool TryValidateSpendingAmountAgainstTagLimit(decimal amount, out string validationMessage)
    {
        validationMessage = string.Empty;

        if (!IsExpense || IsRecurringTransactionMode || IsExcludedFromBudget ||
            SelectedTag is not { SpendingLimit: > 0m } tag)
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
                .Where(log => !IsLoadedTransaction(log))
                .Where(log => log.OccurredOn.Date >= currentPeriod.Start && log.OccurredOn.Date <= currentPeriod.End)
                .Where(log => log.TagId == tag.Id || log.Tag?.Id == tag.Id)
                .Sum(log => log.Amount);

            var result = TransactionValidationHelper.ValidateTagSpending(
                IsExpense, IsRecurringTransactionMode, IsExcludedFromBudget, tag, currentTagSpending, amount);
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

    internal static SavingGoalVM ProjectSavingGoal(SavingGoal goal) => new()
    {
        Id = goal.Id,
        Name = goal.Name,
        TargetAmount = goal.TargetAmount,
        CurrentAmount = goal.CurrentAmount,
        SavingEndDate = goal.SavingEndDate,
        CreatedOn = goal.CreatedOn
    };





    private IEnumerable<object> ProcessingTargets => _processingRepayments.Cast<object>()
        .Concat(_processingGoals).Concat(_processingRecurringTransactions);

    private object? CurrentProcessingTarget => ProcessingTargets.ElementAtOrDefault(_currentProcessingIndex);

    private void InitializeProcessing()
    {
        IsBulkMode = true;
        _processingRepayments.Clear();
        _processingGoals.Clear();
        _processingRecurringTransactions.Clear();
        _currentProcessingIndex = 0;
    }

    private void InitializeProcessingQueue()
    {
        var transactions = new List<TransactionVM>();
        _transactionFormOptions.Clear();
        var targets = ProcessingTargets.ToList();
        for (var index = 0; index < targets.Count; index++)
        {
            _currentProcessingIndex = index;
            LoadProcessingCurrent();
            var transaction = TransactionMappingHelper.CreatePending(_currentRootTransaction);
            transactions.Add(transaction);
            _transactionFormOptions[transaction] = CaptureOptions();
        }

        _currentProcessingIndex = 0;
        _messenger.Send(new TransactionBulkQueueResetMessage(
            true, transactions, Accounts.FirstOrDefault(account => account.IsDefault) ?? Accounts.FirstOrDefault()),
            _messageToken);
        RevalidateTransactions();
    }

    private void LoadProcessingCurrent()
    {
        if (CurrentProcessingTarget is not { } target)
            return;

        _currentProcessingRecurringTransactionId = null;
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
            ResetForm(false);
            IsRecurring = false;
            IsExpense = recurring.Type != RecurringTransactionType.Income;
            NameText = recurring.Name;
            AmountText = recurring.Amount;
            SelectedExpenseCategory = recurring.Category ?? ExpenseCategory.Needs;
            SelectedAccount = Accounts.FirstOrDefault(account => account.Id == recurring.Source.Id);
            SelectedTag = recurring.Tag;
            SetSelectedOccurredOn(DateTime.Now);
            IsExcludedFromBudget = recurring.IsExcludedFromBudget;
        }

        SetPopupPurpose(TransactionPopupPurpose.Processing);
        RestoreProcessingTransactionState();
    }

    private void RestoreProcessingTransactionState()
    {
        EnsureTransactionState();
        SyncPendingTransactionFromForm();
        var loaded = TransactionMappingHelper.CreateLoaded(PendingTransaction);
        LoadedTransaction = loaded;
        PendingTransaction.Id = 0;
        PendingTransaction.LoggedOn = default;
        PendingTransaction.ParentTransactionId = null;
        PendingTransaction.IsForDeletion = false;
    }

    private void ClearProcessing()
    {
        _messenger.Send(new TransactionBulkQueueResetMessage(false, [], null), _messageToken);
        _transactionFormOptions.Clear();
        _processingRepayments.Clear();
        _processingGoals.Clear();
        _processingRecurringTransactions.Clear();
        IsBulkMode = false;
        _currentProcessingIndex = 0;
        _currentProcessingRecurringTransactionId = null;
        SetPopupPurpose(TransactionPopupPurpose.AddNewTransaction);
        NotifyProcessingChanged();
    }

    private void NotifyProcessingChanged()
    {
        OnPropertyChanged(nameof(IsProcessingSession));
        OnPropertyChanged(nameof(CurrentProcessingRecurringTransactionId));
        OnPropertyChanged(nameof(PopupMode));
        OnPropertyChanged(nameof(CanPersist));
        OnPropertyChanged(nameof(PopupTitle));
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(ShowPinnedPanel));
        OnPropertyChanged(nameof(ShowSplitPanel));
        OnPropertyChanged(nameof(ShowSidePanel));
        OnPropertyChanged(nameof(ShowRightFormDivider));
        OnPropertyChanged(nameof(CanChangeTransactionType));
        OnPropertyChanged(nameof(CanUseSplit));
        PublishSplitContext();
    }


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
        SetPopupPurpose(TransactionPopupPurpose.EditRecurringTransaction);
        ApplyRecurringState(state);
        IsExcludedFromBudget = state.IsExcludedFromBudget;
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
        SetPopupPurpose(draft.EditingRecurringTransactionId is > 0
            ? TransactionPopupPurpose.EditRecurringTransaction
            : TransactionPopupPurpose.AddRecurringTransaction);
        ApplyRecurringState(state);
    }

    private void ApplyRecurringState(EditRecurringTransactionHelper.State state)
    {
        _editingRecurringTransactionId = state.EditingRecurringTransactionId;
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
        OnPropertyChanged(nameof(CanToggleBulk));
        OnPropertyChanged(nameof(PopupMode));
        OnPropertyChanged(nameof(ShowSidePanelToggle));
        OnPropertyChanged(nameof(CanChangeTransactionType));
        OnPropertyChanged(nameof(CanUseSplit));
        PublishSplitContext();
        OnPropertyChanged(nameof(ShowHistoryPanel));
        OnPropertyChanged(nameof(ShowPinnedPanel));
        OnPropertyChanged(nameof(ShowSplitPanel));
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
