using System.Collections.ObjectModel;
using Fluxo.Helpers.MainWindow;
using Fluxo.Helpers.Transaction;
using System.ComponentModel;
using System.Windows.Data;
using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.History;
using Fluxo.Services.Ui;
using Fluxo.ViewModels.Entities;
using CoreILogMemoryAction = Fluxo.Core.Interfaces.History.ILogMemoryAction;

namespace Fluxo.ViewModels.Shell.Main;

public partial class BudgetAllocationPanelVM : ObservableRecipient,
    IRecipient<DateRangeSelectionChangedMessage>,
    IRecipient<AllTimeViewModeMessage>,
    IRecipient<ViewModeChangeMessage>,
    IRecipient<DashboardDataInvalidatedMessage>,
    IRecipient<RecordLogMemoryMessage>,
    IRecipient<LogMemoryActionAppliedMessage>
{
    private const int BucketPageSize = 25;
    private const int VisibleTagSlots = 5;

    private readonly IAppDataService _appData;
    private readonly ITransactionService _transactionService;
    private readonly HashSet<TransactionVM> _investVisibleWindow = [];
    private readonly Dictionary<int, TagVM> _knownTagsById = [];
    private readonly IMapper _mapper;
    private readonly ObservableCollection<TransactionVM> _investSource = [];
    private readonly HashSet<TransactionVM> _needsVisibleWindow = [];
    private readonly ObservableCollection<TransactionVM> _needsSource = [];
    private readonly HashSet<BudgetTransactionLogVM> _transactionsVisibleWindow = [];
    private readonly ObservableCollection<BudgetTransactionLogVM> _transactionsSource = [];
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly IAccountService _accountService;
    private readonly ITagService _tagService;
    private readonly HashSet<TransactionVM> _wantsVisibleWindow = [];
    private readonly ObservableCollection<TransactionVM> _wantsSource = [];
    private readonly ObservableCollection<AccountVM> _accounts = [];
    private readonly IDialogService? _dialogService;
    private readonly IUiSettleAwaiter? _uiSettleAwaiter;
    private readonly SemaphoreSlim _filterFeedbackGate = new(1, 1);
    private readonly List<TagVM> _orderedTags = [];
    private readonly AllocationDataVM? _allocationData;

    private List<TransactionVM> _allExpenseLogs = [];
    private List<TransactionVM> _allIncomeLogs = [];
    private BudgetAllocation _budgetAllocation = new();
    private int _investVisibleCount = BucketPageSize;
    private bool _isSynchronizingTagSelections;
    private bool _suppressFilterFeedback;
    private int _needsVisibleCount = BucketPageSize;
    private (DateTime From, DateTime To)? _selectedRange = (DateTime.Today, DateTime.Today);
    private int _transactionsVisibleCount = BucketPageSize;
    private int _wantsVisibleCount = BucketPageSize;

    public BudgetAllocationPanelVM(
        ITransactionService transactionService,
        IAccountService accountService,
        ITagService tagService,
        IAppDataService appData,
        IMapper mapper,
        IMessenger? messenger = null,
        IDialogService? dialogService = null,
        IUiSettleAwaiter? uiSettleAwaiter = null,
        AllocationDataVM? allocationData = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _transactionService = transactionService;
        _accountService = accountService;
        _tagService = tagService;
        _appData = appData;
        _mapper = mapper;
        _dialogService = dialogService;
        _uiSettleAwaiter = uiSettleAwaiter;
        _allocationData = allocationData;

        if (_allocationData is not null)
            _allocationData.PropertyChanged += OnAllocationDataPropertyChanged;

        Initialize();
    }

    public decimal TotalSpent => _allocationData?.TotalSpent ?? 0m;

    public int DailyAllowance => _allocationData?.DailyAllowance ?? 0;

    public decimal NeedsAvailable => _allocationData?.NeedsAvailable ?? 0m;

    public decimal WantsAvailable => _allocationData?.WantsAvailable ?? 0m;

    public decimal InvestAvailable => _allocationData?.InvestAvailable ?? 0m;

    public decimal NeedsSpent => _allocationData?.NeedsSpent ?? 0m;

    public decimal WantsSpent => _allocationData?.WantsSpent ?? 0m;

    public decimal InvestSpent => _allocationData?.InvestSpent ?? 0m;

    public decimal NeedsRemaining => _allocationData?.NeedsRemaining ?? 0m;

    public decimal WantsRemaining => _allocationData?.WantsRemaining ?? 0m;

    public decimal InvestRemaining => _allocationData?.InvestRemaining ?? 0m;

    public int NeedsPercentage => _allocationData?.NeedsPercentage ?? 0;

    public int WantsPercentage => _allocationData?.WantsPercentage ?? 0;

    public int InvestPercentage => _allocationData?.InvestPercentage ?? 0;

    [ObservableProperty]
    private ICollectionView _needs = CollectionViewSource.GetDefaultView(Array.Empty<TransactionVM>());

    [ObservableProperty]
    private ICollectionView _wants = CollectionViewSource.GetDefaultView(Array.Empty<TransactionVM>());

    [ObservableProperty]
    private ICollectionView _invest = CollectionViewSource.GetDefaultView(Array.Empty<TransactionVM>());

    [ObservableProperty]
    private ICollectionView _transactions = CollectionViewSource.GetDefaultView(Array.Empty<BudgetTransactionLogVM>());

    [ObservableProperty]
    private bool _isNeedsEmpty;

    [ObservableProperty]
    private bool _isWantsEmpty;

    [ObservableProperty]
    private bool _isInvestEmpty;

    [ObservableProperty]
    private bool _isTransactionsEmpty;

    [ObservableProperty]
    private bool _needsHasMoreItems;

    [ObservableProperty]
    private bool _wantsHasMoreItems;

    [ObservableProperty]
    private bool _investHasMoreItems;

    [ObservableProperty]
    private bool _transactionsHasMoreItems;

    [ObservableProperty]
    private bool _isNeedsLoading;

    [ObservableProperty]
    private bool _isWantsLoading;

    [ObservableProperty]
    private bool _isInvestLoading;

    [ObservableProperty]
    private bool _isTransactionsLoading;

    [ObservableProperty]
    private ObservableCollection<TagVM> _tags = [];

    [ObservableProperty]
    private ObservableCollection<TagVM> _otherTags = [];

    [ObservableProperty]
    private TagVM? _selectedTag;

    [ObservableProperty]
    private TagVM? _selectedVisibleTag;

    [ObservableProperty]
    private TagVM? _selectedOtherTag;

    [ObservableProperty]
    private int? _selectedAccountId;

    [ObservableProperty]
    private decimal _needsThreshold = 0.5m;

    [ObservableProperty]
    private decimal _wantsThreshold = 0.3m;

    [ObservableProperty]
    private decimal _investThreshold = 0.2m;

    public int NeedsAllocationPercentage => ConvertThresholdToPercentage(NeedsThreshold);

    public int WantsAllocationPercentage => ConvertThresholdToPercentage(WantsThreshold);

    public int InvestAllocationPercentage => ConvertThresholdToPercentage(InvestThreshold);

    public bool HasOtherTags => OtherTags.Count > 0;

    public bool IsSelectedTagInOtherTags => SelectedOtherTag is not null;

    public ObservableCollection<AccountVM> Accounts => _accounts;

    public decimal TotalIncomeAmount => _allocationData?.TotalIncomeAmount ?? 0m;

    public IReadOnlyList<TransactionVM> GetAllExpenseLogs() => _allExpenseLogs.ToList();

    public IReadOnlyList<TransactionVM> GetAllIncomeLogs() => _allIncomeLogs.ToList();

    public IReadOnlyList<TransactionVM> GetAllTransactions() => _allExpenseLogs.Concat(_allIncomeLogs).ToList();

    public DateRange GetCurrentAllocationPeriodRange(DateTime today)
    {
        return DateRangeResolver.ResolveAllocationPeriod(today, _budgetAllocation);
    }

    public void Receive(DateRangeSelectionChangedMessage message)
    {
        _selectedRange = message.Value;
        RefreshRangeScopedData();
    }

    public void Receive(AllTimeViewModeMessage message)
    {
        _selectedRange = null;
        RefreshRangeScopedData();
    }

    public void Receive(ViewModeChangeMessage message)
    {
        if (message.Value != MainContentViewMode.AllocationPeriod)
            return;

        var range = GetCurrentAllocationPeriodRange(DateTime.Today);
        Messenger.Send(new DateRangeSelectionChangedMessage(range.From, range.To));
    }

    public void Receive(DashboardDataInvalidatedMessage message)
    {
        if (!message.Value.HasFlag(DashboardDataInvalidationScope.Budget))
            return;

        _ = ReloadFromServicesAsync();
    }

    public void Receive(RecordLogMemoryMessage message)
    {
        ApplyBalanceImpactsFromAction(message.Value, LogMemoryApplyDirection.Reapply);
    }

    public void Receive(LogMemoryActionAppliedMessage message)
    {
        var (action, direction) = message.Value;
        ApplyBalanceImpactsFromAction(action, direction);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _budgetAllocation = await LoadBudgetAllocationAsync(cancellationToken);
        NeedsThreshold = _budgetAllocation.NeedsThreshold / 100m;
        WantsThreshold = _budgetAllocation.WantsThreshold / 100m;
        InvestThreshold = _budgetAllocation.InvestThreshold / 100m;

        var transactions = _mapper.Map<IReadOnlyList<TransactionVM>>(
            await _transactionService.GetAllAsync(cancellationToken));
        var expenseLogs = transactions
            .Where(transaction => transaction.Type == TransactionType.Expense)
            .ToList();
        var incomeLogs = transactions
            .Where(transaction => transaction.Type == TransactionType.Income)
            .ToList();
        var accounts = _mapper.Map<IReadOnlyList<AccountVM>>(
            await _accountService.GetAllAsync(cancellationToken));
        var tags = _mapper.Map<IReadOnlyList<TagVM>>(
            await _tagService.GetAllAsync(cancellationToken));

        _allExpenseLogs = expenseLogs
            .Where(log => !log.IsForDeletion)
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ToList();
        _allIncomeLogs = incomeLogs
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ToList();

        if (_allocationData is not null)
            await _allocationData.LoadAsync(cancellationToken);

        _accounts.Clear();
        foreach (var source in accounts)
            _accounts.Add(source);

        if (SelectedAccountId is int selectedId &&
            _accounts.All(source => source.Id != selectedId))
            SetSelectedAccountInternal(null);
        else
            SynchronizeAccountSelections(SelectedAccountId);

        CacheKnownTags(tags);
        ApplyVisibleExpenseLogs();
        RefreshSourceDifferences();
    }

    partial void OnSelectedTagChanged(TagVM? value)
    {
        if (!_isSynchronizingTagSelections &&
            value is not null &&
            OtherTags.Any(tag => tag.Id == value.Id))
            PromoteTagToVisibleStart(value);
        else
            SynchronizeTagSelections(value);

        OnPropertyChanged(nameof(IsSelectedTagInOtherTags));
        ResetPaginationWindows();

        if (_suppressFilterFeedback)
        {
            RefreshExpenseViews();
            return;
        }

        QueueFilterRefreshWithFeedback($"Filtering {value?.Name ?? "All"}");
    }

    partial void OnSelectedAccountIdChanged(int? value)
    {
        SynchronizeAccountSelections(value);
        ResetPaginationWindows();

        if (_suppressFilterFeedback)
        {
            RefreshExpenseViews();
            return;
        }

        var sourceName = value is int sourceId
            ? Enumerable.FirstOrDefault<AccountVM>(_accounts, source => source.Id == sourceId)?.Name
            : null;
        QueueFilterRefreshWithFeedback($"Filtering for {sourceName ?? "All sources"}");
    }

    partial void OnNeedsThresholdChanged(decimal value)
    {
        OnPropertyChanged(nameof(NeedsAllocationPercentage));
    }

    partial void OnWantsThresholdChanged(decimal value)
    {
        OnPropertyChanged(nameof(WantsAllocationPercentage));
    }

    partial void OnInvestThresholdChanged(decimal value)
    {
        OnPropertyChanged(nameof(InvestAllocationPercentage));
    }

    partial void OnSelectedVisibleTagChanged(TagVM? value)
    {
        if (_isSynchronizingTagSelections)
            return;

        SelectedTag = value;
    }

    partial void OnSelectedOtherTagChanged(TagVM? value)
    {
        if (_isSynchronizingTagSelections)
            return;

        SelectedTag = value;
    }

    partial void OnNeedsHasMoreItemsChanged(bool value)
    {
        LoadMoreNeedsCommand.NotifyCanExecuteChanged();
    }

    partial void OnWantsHasMoreItemsChanged(bool value)
    {
        LoadMoreWantsCommand.NotifyCanExecuteChanged();
    }

    partial void OnInvestHasMoreItemsChanged(bool value)
    {
        LoadMoreInvestCommand.NotifyCanExecuteChanged();
    }

    partial void OnTransactionsHasMoreItemsChanged(bool value)
    {
        LoadMoreTransactionsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsNeedsLoadingChanged(bool value)
    {
        LoadMoreNeedsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsWantsLoadingChanged(bool value)
    {
        LoadMoreWantsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsInvestLoadingChanged(bool value)
    {
        LoadMoreInvestCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsTransactionsLoadingChanged(bool value)
    {
        LoadMoreTransactionsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearSelectedTag()
    {
        SelectedTag = null;
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreNeeds))]
    private void LoadMoreNeeds()
    {
        if (!CanLoadMoreNeeds())
            return;

        IsNeedsLoading = true;
        try
        {
            _needsVisibleCount += BucketPageSize;
            RefreshExpenseViews();
        }
        finally
        {
            IsNeedsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreWants))]
    private void LoadMoreWants()
    {
        if (!CanLoadMoreWants())
            return;

        IsWantsLoading = true;
        try
        {
            _wantsVisibleCount += BucketPageSize;
            RefreshExpenseViews();
        }
        finally
        {
            IsWantsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreInvest))]
    private void LoadMoreInvest()
    {
        if (!CanLoadMoreInvest())
            return;

        IsInvestLoading = true;
        try
        {
            _investVisibleCount += BucketPageSize;
            RefreshExpenseViews();
        }
        finally
        {
            IsInvestLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadMoreTransactions))]
    private void LoadMoreTransactions()
    {
        if (!CanLoadMoreTransactions())
            return;

        IsTransactionsLoading = true;
        try
        {
            _transactionsVisibleCount += BucketPageSize;
            RefreshExpenseViews();
        }
        finally
        {
            IsTransactionsLoading = false;
        }
    }

    public void ToggleSelectedAccount(AccountVM? source)
    {
        var selectedId = source?.Id;
        if (selectedId is null)
            return;

        SelectedAccountId = SelectedAccountId == selectedId
            ? null
            : selectedId;
    }

    private void SetSelectedAccountInternal(int? sourceId)
    {
        _suppressFilterFeedback = true;
        try
        {
            SelectedAccountId = sourceId;
        }
        finally
        {
            _suppressFilterFeedback = false;
        }
    }

    [RelayCommand]
    private async Task DeleteExpenseLog(object? item)
    {
        var expenseLog = item switch
        {
            TransactionVM log => log,
            BudgetTransactionLogVM row => row.Transaction,
            _ => null
        };

        if (expenseLog is null || expenseLog.IsForDeletion)
            return;

        await _transactionService.DeleteAsync(expenseLog.Id);

        ApplyDeletedExpenseLogToUi(expenseLog);
    }

    private void Initialize()
    {
        ConfigureExpenseViews();
        ResetPaginationWindows();
        IsActive = true;
    }

    private async Task ReloadFromServicesAsync()
    {
        await _reloadGate.WaitAsync();

        try
        {
            await LoadAsync();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private void CacheKnownTags(IEnumerable<TagVM> allTags)
    {
        _knownTagsById.Clear();
        foreach (var tag in allTags.Where(tag => tag.Id > 0))
            _knownTagsById[tag.Id] = CloneTag(tag);
    }

    private void ConfigureExpenseViews()
    {
        Needs = CollectionViewSource.GetDefaultView(_needsSource);
        Wants = CollectionViewSource.GetDefaultView(_wantsSource);
        Invest = CollectionViewSource.GetDefaultView(_investSource);
        Transactions = CollectionViewSource.GetDefaultView(_transactionsSource);

        Needs.Filter = FilterNeedsExpenseLog;
        Wants.Filter = FilterWantsExpenseLog;
        Invest.Filter = FilterInvestExpenseLog;
        Transactions.Filter = FilterTransactionLog;
    }

    private async Task<BudgetAllocation> LoadBudgetAllocationAsync(CancellationToken cancellationToken)
    {
        return await _appData.GetBudgetAllocationAsync(cancellationToken);
    }

    private static int ConvertThresholdToPercentage(decimal threshold)
    {
        return (int)Math.Round(threshold * 100m, MidpointRounding.AwayFromZero);
    }

    private void ApplyVisibleExpenseLogs(bool resetPaginationWindows = true)
    {
        var budgetEffectiveLogs = BudgetEffectiveTransactionFilter.Select(_allExpenseLogs);
        var visibleExpenseLogs = (_selectedRange is { } range
            ? budgetEffectiveLogs.Where(log => log.OccurredOn.Date >= range.From.Date && log.OccurredOn.Date <= range.To.Date)
            : budgetEffectiveLogs)
            .ToList();
        var visibleTransactionExpenseLogs = (_selectedRange is { } transactionRange
            ? _allExpenseLogs.Where(log =>
                !log.IsForDeletion &&
                log.ParentTransactionId is null &&
                log.OccurredOn.Date >= transactionRange.From.Date &&
                log.OccurredOn.Date <= transactionRange.To.Date)
            : _allExpenseLogs.Where(log => !log.IsForDeletion && log.ParentTransactionId is null))
            .ToList();
        var visibleIncomeLogs = EnumerateVisibleIncomeLogs().ToList();

        ReplaceExpenseLogs(
            _needsSource,
            visibleExpenseLogs.Where(log => log.ExpenseCategory == ExpenseCategory.Needs));
        ReplaceExpenseLogs(
            _wantsSource,
            visibleExpenseLogs.Where(log => log.ExpenseCategory == ExpenseCategory.Wants));
        ReplaceExpenseLogs(
            _investSource,
            visibleExpenseLogs.Where(log => log.ExpenseCategory == ExpenseCategory.Savings));
        ReplaceTransactionLogs(_transactionsSource, visibleTransactionExpenseLogs, visibleIncomeLogs);
        LoadRangeTags(visibleExpenseLogs);

        if (resetPaginationWindows)
            ResetPaginationWindows();

        RefreshExpenseViews();
    }

    private bool FilterNeedsExpenseLog(object item)
    {
        return FilterPagedExpenseLog(item, _needsVisibleWindow);
    }

    private bool FilterWantsExpenseLog(object item)
    {
        return FilterPagedExpenseLog(item, _wantsVisibleWindow);
    }

    private bool FilterInvestExpenseLog(object item)
    {
        return FilterPagedExpenseLog(item, _investVisibleWindow);
    }

    private bool FilterTransactionLog(object item)
    {
        if (item is not BudgetTransactionLogVM transactionLog)
            return false;

        if (!MatchesSelectedFilters(transactionLog))
            return false;

        return _transactionsVisibleWindow.Contains(transactionLog);
    }

    private bool FilterPagedExpenseLog(object item, HashSet<TransactionVM> visibleWindow)
    {
        if (item is not TransactionVM expenseLog)
            return false;

        if (!MatchesSelectedFilters(expenseLog))
            return false;

        return visibleWindow.Contains(expenseLog);
    }

    private bool MatchesSelectedFilters(TransactionVM expenseLog)
    {
        if (SelectedTag is not null &&
            expenseLog.Tag?.Id != SelectedTag.Id)
            return false;

        return SelectedAccountId is null ||
               expenseLog.Account?.Id == SelectedAccountId;
    }

    private bool MatchesSelectedFilters(BudgetTransactionLogVM transactionLog)
    {
        if (SelectedTag is not null)
            return transactionLog.Transaction.Tag?.Id == SelectedTag.Id;

        return SelectedAccountId is null ||
               transactionLog.Account.Id == SelectedAccountId;
    }

    private void SynchronizeTagSelections(TagVM? selectedTag)
    {
        _isSynchronizingTagSelections = true;

        try
        {
            SelectedVisibleTag = selectedTag is null
                ? null
                : Enumerable.FirstOrDefault<TagVM>(Tags, tag => tag.Id == selectedTag.Id);
            SelectedOtherTag = selectedTag is null
                ? null
                : Enumerable.FirstOrDefault<TagVM>(OtherTags, tag => tag.Id == selectedTag.Id);
        }
        finally
        {
            _isSynchronizingTagSelections = false;
        }
    }

    private void PromoteTagToVisibleStart(TagVM selectedTag)
    {
        var promotedTag = _orderedTags.FirstOrDefault(tag => tag.Id == selectedTag.Id) ?? selectedTag;
        var reorderedTags = _orderedTags
            .Where(tag => tag.Id != selectedTag.Id)
            .Prepend(promotedTag)
            .ToList();

        _orderedTags.Clear();
        _orderedTags.AddRange(reorderedTags);

        RefreshTagCollections();
        SynchronizeTagSelections(promotedTag);
    }

    private void QueueFilterRefreshWithFeedback(string message)
    {
        QueueFilterRefreshWithFeedback(message, RefreshExpenseViews);
    }

    private void QueueFilterRefreshWithFeedback(string message, Action refreshOperation)
    {
        _ = ApplyFilterFeedbackAsync(message, refreshOperation);
    }

    private async Task ApplyFilterFeedbackAsync(string message, Action refreshOperation)
    {
        if (_dialogService is null || _uiSettleAwaiter is null)
        {
            refreshOperation();
            return;
        }

        await _filterFeedbackGate.WaitAsync();
        try
        {
            await _dialogService.ShowToastWhileAsync(
                message,
                async () =>
                {
                    refreshOperation();
                    await _uiSettleAwaiter.WaitForUiReadyAsync();
                });
        }
        catch
        {
            refreshOperation();
        }
        finally
        {
            _filterFeedbackGate.Release();
        }
    }

    private void RefreshRangeScopedData()
    {
        ApplyVisibleExpenseLogs();
        RefreshSourceDifferences();
    }

    private void RefreshExpenseViews()
    {
        RecomputePagedVisibleWindows();

        Needs.Refresh();
        Wants.Refresh();
        Invest.Refresh();
        Transactions.Refresh();

        IsNeedsEmpty = Needs.IsEmpty;
        IsWantsEmpty = Wants.IsEmpty;
        IsInvestEmpty = Invest.IsEmpty;
        IsTransactionsEmpty = Transactions.IsEmpty;
    }

    private void SynchronizeAccountSelections(int? selectedAccountId)
    {
        foreach (var source in _accounts)
            source.IsSelected = selectedAccountId is int id && source.Id == id;
    }

    private void ApplyDeletedExpenseLogToUi(TransactionVM expenseLog)
    {
        var trackedExpenseLog = _allExpenseLogs.FirstOrDefault(log => log.Id == expenseLog.Id) ?? expenseLog;
        Messenger.Send(new RecordLogMemoryMessage(new DeleteTransactionMemoryAction(
            CreateTransactionSnapshot(trackedExpenseLog))));
    }

    private void RefreshSourceDifferences()
    {
        var visibleExpenses = EnumerateVisibleExpenseLogs();
        var visibleIncomes = EnumerateVisibleIncomeLogs();

        var expenseBySource = visibleExpenses
            .GroupBy(log => log.Account?.Id ?? 0)
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));
        var incomeBySource = visibleIncomes
            .GroupBy(log => log.Account?.Id ?? 0)
            .ToDictionary(group => group.Key, group => group.Sum(log => log.Amount));

        foreach (var source in _accounts)
        {
            var expense = expenseBySource.GetValueOrDefault(source.Id);
            var income = incomeBySource.GetValueOrDefault(source.Id);
            source.Difference = income - expense;
        }
    }

    private IEnumerable<TransactionVM> EnumerateVisibleExpenseLogs()
    {
        var source = BudgetEffectiveTransactionFilter.Select(_allExpenseLogs);
        if (_selectedRange is not { } range)
            return source;

        return source.Where(log => log.OccurredOn.Date >= range.From.Date && log.OccurredOn.Date <= range.To.Date);
    }

    private IEnumerable<TransactionVM> EnumerateVisibleIncomeLogs()
    {
        var included = _allIncomeLogs.Where(log => log.ExpenseCategory != ExpenseCategory.Excluded);
        if (_selectedRange is not { } range)
            return included;

        return included.Where(log => log.OccurredOn.Date >= range.From.Date && log.OccurredOn.Date <= range.To.Date);
    }

    private void ApplyBalanceImpactsFromAction(CoreILogMemoryAction action, LogMemoryApplyDirection direction)
    {
        if (action is CompositeLogMemoryAction composite)
        {
            foreach (var child in composite.Actions)
                ApplyBalanceImpactsFromAction(child, direction);
            return;
        }

        if (action is AddTransactionMemoryAction or EditTransactionMemoryAction or DeleteTransactionMemoryAction)
            _ = ReloadFromServicesAsync();
    }
    private void RefreshExpenseBucketsFromTrackedLogs()
    {
        _allExpenseLogs = _allExpenseLogs
            .Where(log => !log.IsForDeletion)
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ToList();

        ApplyVisibleExpenseLogs(resetPaginationWindows: false);
    }

    private void OnAllocationDataPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
            OnPropertyChanged(e.PropertyName);
    }

    private bool CanLoadMoreNeeds()
    {
        return NeedsHasMoreItems && !IsNeedsLoading;
    }

    private bool CanLoadMoreWants()
    {
        return WantsHasMoreItems && !IsWantsLoading;
    }

    private bool CanLoadMoreInvest()
    {
        return InvestHasMoreItems && !IsInvestLoading;
    }

    private bool CanLoadMoreTransactions()
    {
        return TransactionsHasMoreItems && !IsTransactionsLoading;
    }

    private void ResetPaginationWindows()
    {
        _needsVisibleCount = BucketPageSize;
        _wantsVisibleCount = BucketPageSize;
        _investVisibleCount = BucketPageSize;
        _transactionsVisibleCount = BucketPageSize;

        IsNeedsLoading = false;
        IsWantsLoading = false;
        IsInvestLoading = false;
        IsTransactionsLoading = false;
    }

    private void RecomputePagedVisibleWindows()
    {
        NeedsHasMoreItems = UpdatePagedVisibleWindow(_needsSource, _needsVisibleWindow, _needsVisibleCount);
        WantsHasMoreItems = UpdatePagedVisibleWindow(_wantsSource, _wantsVisibleWindow, _wantsVisibleCount);
        InvestHasMoreItems = UpdatePagedVisibleWindow(_investSource, _investVisibleWindow, _investVisibleCount);
        TransactionsHasMoreItems = UpdatePagedVisibleWindow(
            _transactionsSource,
            _transactionsVisibleWindow,
            _transactionsVisibleCount);
    }

    private bool UpdatePagedVisibleWindow(
        IEnumerable<TransactionVM> source,
        HashSet<TransactionVM> visibleWindow,
        int visibleCount)
    {
        var filteredLogs = source
            .Where(MatchesSelectedFilters)
            .ToList();

        visibleWindow.Clear();
        foreach (var log in filteredLogs.Take(visibleCount))
            visibleWindow.Add(log);

        return filteredLogs.Count > visibleCount;
    }

    private bool UpdatePagedVisibleWindow(
        IEnumerable<BudgetTransactionLogVM> source,
        HashSet<BudgetTransactionLogVM> visibleWindow,
        int visibleCount)
    {
        var filteredLogs = source
            .Where(MatchesSelectedFilters)
            .ToList();

        visibleWindow.Clear();
        foreach (var log in filteredLogs.Take(visibleCount))
            visibleWindow.Add(log);

        return filteredLogs.Count > visibleCount;
    }

    private void LoadRangeTags(IEnumerable<TransactionVM> visibleExpenseLogs)
    {
        var orderedTags = BuildOrderedRangeTags(visibleExpenseLogs).ToList();

        _orderedTags.Clear();
        _orderedTags.AddRange(orderedTags);

        RefreshTagCollections();

        if (SelectedTag is not null &&
            orderedTags.All(tag => tag.Id != SelectedTag.Id))
            SetSelectedTagInternal(null);
        else
            SynchronizeTagSelections(SelectedTag);
    }

    private void RefreshTagCollections()
    {
        Tags = new ObservableCollection<TagVM>(_orderedTags.Take(VisibleTagSlots));
        OtherTags = new ObservableCollection<TagVM>(_orderedTags.Skip(VisibleTagSlots));

        OnPropertyChanged(nameof(HasOtherTags));
        OnPropertyChanged(nameof(IsSelectedTagInOtherTags));
    }

    private IEnumerable<TagVM> BuildOrderedRangeTags(IEnumerable<TransactionVM> visibleExpenseLogs)
    {
        return visibleExpenseLogs
            .Where(log => !log.IsForDeletion)
            .Select(log => log.Tag)
            .Where(tag => tag is { Id: > 0 })
            .GroupBy(tag => tag!.Id)
            .Select(group =>
            {
                var sourceTag = _knownTagsById.GetValueOrDefault(group.Key) ?? group.First()!;
                return new
                {
                    Tag = CloneTag(sourceTag),
                    UsageCount = group.Count()
                };
            })
            .OrderBy(item => item.Tag.IsSystemTag)
            .ThenByDescending(item => item.UsageCount)
            .ThenBy(item => item.Tag.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Tag);
    }

    private void SetSelectedTagInternal(TagVM? selectedTag)
    {
        _suppressFilterFeedback = true;
        try
        {
            SelectedTag = selectedTag;
        }
        finally
        {
            _suppressFilterFeedback = false;
        }
    }

    private static TagVM CloneTag(TagVM source)
    {
        return new TagVM
        {
            Id = source.Id,
            Name = source.Name,
            HexCode = source.HexCode,
            IsSystemTag = source.IsSystemTag,
            SpendingLimit = source.SpendingLimit
        };
    }

    private static TransactionMemorySnapshot CreateTransactionSnapshot(TransactionVM transaction) => new(
        transaction.Id,
        transaction.Type,
        transaction.Account.Id,
        transaction.Name,
        transaction.Amount,
        transaction.OccurredOn,
        transaction.Notes,
        transaction.ExpenseCategory,
        transaction.Tag?.Id,
        transaction.GoalId,
        transaction.RepaymentAccountId,
        transaction.ParentTransactionId,
        transaction.IsPinned,
        transaction.IsForDeletion,
        transaction.IsIoU,
        transaction.LoggedOn,
        transaction.ShouldAffectBalance);

    private static void ReplaceExpenseLogs(ObservableCollection<TransactionVM> target, IEnumerable<TransactionVM> items)
    {
        target.Clear();

        foreach (var item in items.OrderByDescending(log => log.OccurredOn).ThenByDescending(log => log.LoggedOn))
            target.Add(item);
    }

    private static void ReplaceTransactionLogs(
        ObservableCollection<BudgetTransactionLogVM> target,
        IEnumerable<TransactionVM> expenseLogs,
        IEnumerable<TransactionVM> incomeLogs)
    {
        target.Clear();

        var rows = expenseLogs
            .Select(log => new BudgetTransactionLogVM
            {
                Id = log.Id,
                Name = log.Name ?? "Expense",
                Amount = log.Amount,
                AmountText = $"-{log.Amount:N0}",
                OccurredOn = log.OccurredOn,
                LoggedOn = log.LoggedOn,
                Account = log.Account,
                TagHexCode = log.Tag?.HexCode,
                Transaction = log
            })
            .Concat(incomeLogs.Select(log => new BudgetTransactionLogVM
            {
                Id = log.Id,
                Name = log.Name,
                Amount = log.Amount,
                AmountText = $"+{log.Amount:N0}",
                OccurredOn = log.OccurredOn,
                LoggedOn = log.LoggedOn,
                Account = log.Account,
                TagHexCode = log.Tag?.HexCode,
                Transaction = log
            }))
            .OrderByDescending(log => log.OccurredOn)
            .ThenByDescending(log => log.LoggedOn)
            .ThenBy(log => log.Id)
            .ThenBy(log => log.IsExpense ? 0 : 1);

        foreach (var row in rows)
            target.Add(row);
    }
}
