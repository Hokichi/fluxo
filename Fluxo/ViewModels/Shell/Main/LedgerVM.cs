using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.History;
using Fluxo.Services.Ui;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Shell.Main;

public partial class LedgerVM : ObservableRecipient,
    IRecipient<LedgerDateRangeRequestedMessage>,
    IRecipient<LedgerAllTimeRequestedMessage>,
    IRecipient<LedgerSearchTextChangedMessage>
{
    private readonly IDataOperationRunner _dataOperationRunner;
    private readonly ITransactionService _transactionService;
    private readonly IMapper _mapper;
    private readonly IAccountService _accountService;
    private readonly ITagService _tagService;
    private readonly IDialogService? _dialogService;
    private readonly IUiSettleAwaiter? _uiSettleAwaiter;
    private readonly ObservableCollection<LedgerTransactionItemVM> _transactions = [];
    private readonly Dictionary<(LedgerTransactionKind Kind, int Id), BatchPreviewSnapshot> _batchPreviewSnapshots = [];
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private LedgerFilterSelectionSnapshot _appliedFilterSelection = LedgerFilterSelectionSnapshot.Empty;
    private bool _loadAllTransactionsOnNextLoad;
    private bool _isApplyingExternalRange;
    private bool _isSynchronizingFilters;
    private (DateTime From, DateTime To)? _selectedRange = (DateTime.Today, DateTime.Today);

    [ObservableProperty] private LedgerGroupingMode _selectedGroupingMode = LedgerGroupingMode.Date;
    [ObservableProperty] private LedgerAmountSortDirection _amountSortDirection = LedgerAmountSortDirection.Descending;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private DateTime _maxSelectableDate = DateTime.Today;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private decimal _spentAmount;
    [ObservableProperty] private decimal _earnedAmount;
    [ObservableProperty] private decimal _goalAmount;
    [ObservableProperty] private decimal _netAmount;
    [ObservableProperty] private bool _hasTransactions;
    [ObservableProperty] private bool _hasVisibleTransactions;
    [ObservableProperty] private bool _isSelectionModeEnabled;
    [ObservableProperty] private bool _hasSelectedVisibleTransactions;
    [ObservableProperty] private bool _areAllVisibleTransactionsSelected;
    [ObservableProperty] private bool _isBulkEditEnabled;
    [ObservableProperty] private int? _selectedBatchAccountId;
    [ObservableProperty] private int? _selectedBatchTagId;

    public LedgerVM(
        ITransactionService transactionService,
        IAccountService accountService,
        ITagService tagService,
        IDataOperationRunner dataOperationRunner,
        IMapper mapper,
        IMessenger? messenger = null,
        IDialogService? dialogService = null,
        IUiSettleAwaiter? uiSettleAwaiter = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _transactionService = transactionService;
        _accountService = accountService;
        _tagService = tagService;
        _dataOperationRunner = dataOperationRunner;
        _mapper = mapper;
        _dialogService = dialogService;
        _uiSettleAwaiter = uiSettleAwaiter;
        TypeFilterPresentation = new LedgerFilterSelectionPresentation(
            "Type",
            () => TypeFilterSelectionCount,
            () => TypeFilterSelectionToolTip);
        AccountFilterPresentation = new LedgerFilterSelectionPresentation(
            "Account",
            () => AccountFilterSelectionCount,
            () => AccountFilterSelectionToolTip);
        CategoryFilterPresentation = new LedgerFilterSelectionPresentation(
            "Category",
            () => CategoryFilterSelectionCount,
            () => CategoryFilterSelectionToolTip);
        TagFilterPresentation = new LedgerFilterSelectionPresentation(
            "Tag",
            () => TagFilterSelectionCount,
            () => TagFilterSelectionToolTip);

        TransactionsView = CollectionViewSource.GetDefaultView(_transactions);
        TransactionsView.Filter = FilterTransaction;
        UpdateSortAndGroups();

        IsActive = true;
    }

    public ICollectionView TransactionsView { get; }
    public string EmptyStatePeriodText => BuildSelectedPeriodText();
    public ObservableCollection<LedgerFilterOption<LedgerTransactionKind>> TypeFilters { get; } = [];
    public ObservableCollection<LedgerFilterOption<int>> AccountFilters { get; } = [];
    public ObservableCollection<LedgerFilterOption<LedgerCategoryFilter>> CategoryFilters { get; } = [];
    public ObservableCollection<LedgerFilterOption<int>> TagFilters { get; } = [];
    public ObservableCollection<TagVM> EditableTags { get; } = [];
    public IReadOnlyList<LedgerFilterOption<int>> BatchAccountOptions =>
        AccountFilters.Where(option => !option.IsAll).ToList();
    public IReadOnlyList<LedgerFilterOption<int>> BatchTagOptions =>
        TagFilters.Where(option => !option.IsAll).ToList();
    public bool HasPendingFilterChanges => CaptureFilterSelectionSnapshot() != _appliedFilterSelection;
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        TypeFilterSelectionCount > 0 ||
        AccountFilterSelectionCount > 0 ||
        CategoryFilterSelectionCount > 0 ||
        TagFilterSelectionCount > 0;
    public LedgerFilterSelectionPresentation TypeFilterPresentation { get; }
    public LedgerFilterSelectionPresentation AccountFilterPresentation { get; }
    public LedgerFilterSelectionPresentation CategoryFilterPresentation { get; }
    public LedgerFilterSelectionPresentation TagFilterPresentation { get; }
    public int TypeFilterSelectionCount => CountSpecificSelections(TypeFilters);
    public int AccountFilterSelectionCount => CountSpecificSelections(AccountFilters);
    public int CategoryFilterSelectionCount => CountSpecificSelections(CategoryFilters);
    public int TagFilterSelectionCount => CountSpecificSelections(TagFilters);
    public string? TypeFilterSelectionToolTip => BuildSpecificSelectionToolTip(TypeFilters);
    public string? AccountFilterSelectionToolTip => BuildSpecificSelectionToolTip(AccountFilters);
    public string? CategoryFilterSelectionToolTip => BuildSpecificSelectionToolTip(CategoryFilters);
    public string? TagFilterSelectionToolTip => BuildSpecificSelectionToolTip(TagFilters);
    public string SelectionModeButtonText => IsSelectionModeEnabled ? "Disable Selection" : "Enable Selection";
    public string CheckAllButtonText => AreAllVisibleTransactionsSelected ? "Uncheck All" : "Check All";
    public string DeleteSelectedButtonText => AreAllVisibleTransactionsSelected ? "Delete All" : "Delete Selected";
    public decimal SelectedVisibleTotal => GetVisibleTransactions()
        .SelectMany(transaction => transaction.HasChildTransactions
            ? transaction.VisibleChildTransactions
            : [transaction])
        .Where(transaction => transaction.IsSelectedForBatch)
        .Sum(transaction => transaction.SignedAmount);
    public string BatchAccountSelectionText => SelectedBatchAccountId is { } sourceId
        ? BatchAccountOptions.FirstOrDefault(option => option.Value == sourceId)?.Label ?? string.Empty
        : string.Empty;
    public string BatchTagSelectionText => SelectedBatchTagId is { } tagId
        ? BatchTagOptions.FirstOrDefault(option => option.Value == tagId)?.Label ?? string.Empty
        : string.Empty;
    public IReadOnlyList<LedgerGroupingMode> GroupingModes { get; } =
    [
        LedgerGroupingMode.None,
        LedgerGroupingMode.Date,
        LedgerGroupingMode.Tags,
        LedgerGroupingMode.Accounts,
        LedgerGroupingMode.Types,
        LedgerGroupingMode.Category
    ];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _reloadGate.WaitAsync(cancellationToken);
        try
        {
            if (_loadAllTransactionsOnNextLoad)
            {
                _loadAllTransactionsOnNextLoad = false;
                await ReloadAllTransactionsAsync(cancellationToken);
            }
            else
            {
                await ReloadPeriodAsync(cancellationToken);
            }
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Receive(LedgerDateRangeRequestedMessage message)
    {
        _loadAllTransactionsOnNextLoad = false;
        ApplyExternalDateRange(message.Value.From, message.Value.To, refresh: false);
    }

    public void ApplyExternalDateRange(DateTime from, DateTime to, bool refresh)
    {
        SetSelectedRange(from, to, updateSelectors: true);

        if (refresh)
            _ = LoadAsync();
    }

    public void Receive(LedgerAllTimeRequestedMessage message)
    {
        _loadAllTransactionsOnNextLoad = true;
    }

    [RelayCommand]
    public async Task LoadAllTransactionsAsync(CancellationToken cancellationToken = default)
    {
        _loadAllTransactionsOnNextLoad = true;
        await LoadWithFeedbackAsync(cancellationToken);
    }

    public void Receive(LedgerSearchTextChangedMessage message)
    {
        SearchText = message.Value;
    }

    [RelayCommand]
    private void ToggleAmountSortDirection()
    {
        AmountSortDirection = AmountSortDirection == LedgerAmountSortDirection.Descending
            ? LedgerAmountSortDirection.Ascending
            : LedgerAmountSortDirection.Descending;
    }

    [RelayCommand]
    private void ToggleChildTransactions(LedgerTransactionItemVM? transaction)
    {
        if (transaction is null || !transaction.HasChildTransactions)
            return;

        transaction.IsChildrenExpanded = !transaction.IsChildrenExpanded;
    }

    [RelayCommand]
    private async Task RemoveTransactionAsync(LedgerTransactionItemVM? transaction)
    {
        if (transaction is null || transaction.IsGoal)
            return;

        switch (transaction.Kind)
        {
            case LedgerTransactionKind.Expense:
                await RemoveExpenseTransactionAsync(transaction);
                break;
            case LedgerTransactionKind.Income:
                await RemoveIncomeTransactionAsync(transaction);
                break;
        }
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        var shouldEnable = !IsSelectionModeEnabled;
        if (!shouldEnable)
        {
            RestoreAllBatchPreviewSnapshots();
            IsBulkEditEnabled = false;
        }

        IsSelectionModeEnabled = shouldEnable;
        SelectedBatchAccountId = null;
        SelectedBatchTagId = null;
        if (IsSelectionModeEnabled)
            _batchPreviewSnapshots.Clear();

        foreach (var transaction in _transactions)
            transaction.IsSelectedForBatch = IsSelectionModeEnabled && TransactionsView.Contains(transaction);

        RefreshBatchSelectionState();
    }

    [RelayCommand]
    private void ToggleVisibleBatchSelection()
    {
        var visibleTransactions = GetVisibleTransactions();
        var shouldCheck = visibleTransactions.Count > 0 &&
                          !visibleTransactions.All(transaction => transaction.IsSelectedForBatch);

        foreach (var transaction in visibleTransactions)
            transaction.IsSelectedForBatch = shouldCheck;

        RefreshBatchSelectionState();
    }

    partial void OnSearchTextChanged(string value)
    {
        TransactionsView.Refresh();
        RefreshVisibleChildTransactions();
        RefreshVisibleTransactionState();
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (_isApplyingExternalRange)
            return;

        SetSelectedRange(value, EndDate, updateSelectors: true);
        _ = LoadWithFeedbackAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (_isApplyingExternalRange)
            return;

        SetSelectedRange(StartDate, value, updateSelectors: true);
        _ = LoadWithFeedbackAsync();
    }

    partial void OnSelectedGroupingModeChanged(LedgerGroupingMode value)
    {
        UpdateSortAndGroups();
        RefreshVisibleTransactionState();
    }

    partial void OnAmountSortDirectionChanged(LedgerAmountSortDirection value)
    {
        UpdateSortAndGroups();
        RefreshVisibleTransactionState();
    }

    partial void OnIsSelectionModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectionModeButtonText));
    }

    partial void OnSelectedBatchAccountIdChanged(int? value)
    {
        OnPropertyChanged(nameof(BatchAccountSelectionText));
        ApplyBatchPreview();
    }

    partial void OnSelectedBatchTagIdChanged(int? value)
    {
        OnPropertyChanged(nameof(BatchTagSelectionText));
        ApplyBatchPreview();
    }

    private async Task ReloadAllTransactionsAsync(CancellationToken cancellationToken)
    {
        var source = await _transactionService.GetAllAsync(cancellationToken);
        var range = DateRangeResolver.ResolveAllTransactions(
            source.Where(transaction => !transaction.IsForDeletion).Select(transaction => transaction.OccurredOn),
            DateTime.Today);
        SetSelectedRange(range.From, range.To, updateSelectors: true);
        await ReloadPeriodAsync(cancellationToken, source);
    }

    private async Task LoadWithFeedbackAsync(CancellationToken cancellationToken = default)
    {
        if (_dialogService is null || _uiSettleAwaiter is null)
        {
            await LoadAsync(cancellationToken);
            return;
        }

        await _dialogService.ShowToastWhileAsync(
            "Loading data",
            async () =>
            {
                await LoadAsync(cancellationToken);
                await _uiSettleAwaiter.WaitForUiReadyAsync(cancellationToken: cancellationToken);
            });
    }

    private async Task ReloadPeriodAsync(
        CancellationToken cancellationToken,
        IReadOnlyList<TransactionDto>? source = null)
    {
        source ??= await _transactionService.GetAllAsync(cancellationToken);
        var transactions = _mapper.Map<IReadOnlyList<TransactionVM>>(source);
        var expenseLogs = transactions.Where(transaction => transaction.Type == TransactionType.Expense)
            .Select(ToExpenseLogVm).ToList();
        var incomeLogs = transactions.Where(transaction => transaction.Type == TransactionType.Income)
            .Select(ToIncomeLogVm).ToList();
        var accounts = _mapper.Map<IReadOnlyList<AccountVM>>(
            await _accountService.GetAllAsync(cancellationToken));
        var tags = _mapper.Map<IReadOnlyList<TagVM>>(
            await _tagService.GetAllAsync(cancellationToken));

        RebuildFilters(accounts, tags);

        var activeExpenseLogs = expenseLogs
            .Where(log => !log.IsForDeletion)
            .ToList();
        var childExpenseRowsByParentId = activeExpenseLogs
            .Where(log => log.ParentTransactionId is not null)
            .GroupBy(log => log.ParentTransactionId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(log => log.OccurredOn)
                    .ThenByDescending(log => log.LoggedOn)
                    .ThenBy(log => log.Name ?? "Expense", StringComparer.OrdinalIgnoreCase)
                    .Select(log => ProjectExpense(log, isChildTransaction: true))
                    .ToList());

        var projectedExpenses = activeExpenseLogs
            .Where(log => log.ParentTransactionId is null)
            .Where(IsInSelectedRange)
            .Select(log => ProjectExpense(log))
            .ToList();
        foreach (var expense in projectedExpenses)
        {
            if (!childExpenseRowsByParentId.TryGetValue(expense.Id, out var childExpenses))
                continue;

            foreach (var childExpense in childExpenses)
            {
                expense.ChildTransactions.Add(childExpense);
            }

            expense.RefreshChildTransactionState();
        }

        var projected = projectedExpenses
            .Concat(incomeLogs.Where(IsInSelectedRange).Select(ProjectIncome))
            .OrderByDescending(item => item.OccurredOn)
            .ThenByDescending(item => item.LoggedOn)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _transactions.Clear();
        foreach (var transaction in projected)
            _transactions.Add(transaction);

        HasTransactions = _transactions.Count > 0;
        RefreshSummaries();
        TransactionsView.Refresh();
        RefreshVisibleChildTransactions();
        RefreshVisibleTransactionState();
    }

    private void SetSelectedRange(DateTime from, DateTime to, bool updateSelectors)
    {
        var start = from.Date;
        var end = to.Date;
        var max = MaxSelectableDate.Date;

        if (start > max)
            start = max;
        if (end > max)
            end = max;
        if (start > end)
            (start, end) = (end, start);

        _selectedRange = (start, end);
        OnPropertyChanged(nameof(EmptyStatePeriodText));

        if (!updateSelectors)
            return;

        _isApplyingExternalRange = true;
        try
        {
            if (StartDate.Date != start)
                StartDate = start;
            if (EndDate.Date != end)
                EndDate = end;
        }
        finally
        {
            _isApplyingExternalRange = false;
        }
    }

    private static TransactionVM ToExpenseLogVm(TransactionVM transaction)
        => transaction;

    private static TransactionVM ToIncomeLogVm(TransactionVM transaction) => transaction;

    private void RebuildFilters(IReadOnlyList<AccountVM> accounts, IReadOnlyList<TagVM> tags)
    {
        var preservedSelection = CaptureFilterSelectionSnapshot();
        ReplaceEditableTags(tags);

        RebuildFilter(TypeFilters,
        [
            new LedgerFilterOption<LedgerTransactionKind>("All", default, isAll: true, isChecked: true),
            new LedgerFilterOption<LedgerTransactionKind>("Expenses", LedgerTransactionKind.Expense),
            new LedgerFilterOption<LedgerTransactionKind>("Incomes", LedgerTransactionKind.Income)
        ], preservedSelection.Type);

        RebuildFilter(AccountFilters,
            new[] { new LedgerFilterOption<int>("All", default, isAll: true, isChecked: true) }
                .Concat(accounts
                    .OrderBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(source => new LedgerFilterOption<int>(source.Name, source.Id)))
                .ToList(), preservedSelection.Account);

        RebuildFilter(CategoryFilters,
        [
            new LedgerFilterOption<LedgerCategoryFilter>("All", default, isAll: true, isChecked: true),
            new LedgerFilterOption<LedgerCategoryFilter>("Needs", LedgerCategoryFilter.Needs),
            new LedgerFilterOption<LedgerCategoryFilter>("Wants", LedgerCategoryFilter.Wants),
            new LedgerFilterOption<LedgerCategoryFilter>("Invest", LedgerCategoryFilter.Invest),
            new LedgerFilterOption<LedgerCategoryFilter>("Excluded", LedgerCategoryFilter.Excluded)
        ], preservedSelection.Category);

        RebuildFilter(TagFilters,
            new[] { new LedgerFilterOption<int>("All", default, isAll: true, isChecked: true) }
                .Concat(tags
                    .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(tag => new LedgerFilterOption<int>(tag.Name, tag.Id)))
                .ToList(), preservedSelection.Tag);

        _appliedFilterSelection = CaptureFilterSelectionSnapshot();
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(BatchAccountOptions));
        OnPropertyChanged(nameof(BatchTagOptions));
        OnPropertyChanged(nameof(BatchAccountSelectionText));
        OnPropertyChanged(nameof(BatchTagSelectionText));
        RefreshAllFilterSelectionPresentations();
    }

    private void ReplaceEditableTags(IReadOnlyList<TagVM> tags)
    {
        EditableTags.Clear();
        foreach (var tag in tags
                     .Where(tag => !tag.IsSystemTag)
                     .OrderBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase))
        {
            EditableTags.Add(tag);
        }
    }

    private void RebuildFilter<T>(
        ObservableCollection<LedgerFilterOption<T>> target,
        IReadOnlyList<LedgerFilterOption<T>> options,
        string preservedSelection)
    {
        var selectedValues = string.IsNullOrWhiteSpace(preservedSelection)
            ? new HashSet<string>(["all"])
            : preservedSelection.Split('|').ToHashSet(StringComparer.Ordinal);

        foreach (var option in target)
            option.PropertyChanged -= OnFilterOptionPropertyChanged;

        target.Clear();
        foreach (var option in options)
        {
            option.IsChecked = selectedValues.Contains(option.IsAll ? "all" : option.Value?.ToString() ?? string.Empty);
            option.PropertyChanged += OnFilterOptionPropertyChanged;
            target.Add(option);
        }
    }

    private void OnFilterOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LedgerFilterOption<int>.IsChecked) || _isSynchronizingFilters)
            return;

        switch (sender)
        {
            case LedgerFilterOption<LedgerTransactionKind> type:
                NormalizeFilterSelection(TypeFilters, type);
                break;
            case LedgerFilterOption<int> source when AccountFilters.Contains(source):
                NormalizeFilterSelection(AccountFilters, source);
                break;
            case LedgerFilterOption<int> tag when TagFilters.Contains(tag):
                NormalizeFilterSelection(TagFilters, tag);
                break;
            case LedgerFilterOption<LedgerCategoryFilter> category:
                NormalizeFilterSelection(CategoryFilters, category);
                break;
        }

        OnPropertyChanged(nameof(HasPendingFilterChanges));
        OnPropertyChanged(nameof(HasActiveFilters));
        RefreshFilterSelectionPresentation(sender);
    }

    public void ApplyFilters()
    {
        _appliedFilterSelection = CaptureFilterSelectionSnapshot();
        TransactionsView.Refresh();
        RefreshVisibleChildTransactions();
        RefreshVisibleTransactionState();
        OnPropertyChanged(nameof(HasPendingFilterChanges));
        RefreshAllFilterSelectionPresentations();
    }

    public bool ApplyFiltersIfChanged()
    {
        if (!HasPendingFilterChanges)
            return false;

        ApplyFilters();
        return true;
    }

    public IReadOnlyList<LedgerTransactionItemVM> GetVisibleTransactionsForExport()
    {
        var visibleTransactions = GetVisibleTransactions();
        return IsSelectionModeEnabled
            ? visibleTransactions.Where(transaction => transaction.IsSelectedForBatch).ToList()
            : visibleTransactions;
    }

    public void RefreshBatchSelectionState()
    {
        var visibleTransactions = GetVisibleTransactions();
        HasSelectedVisibleTransactions = visibleTransactions.Any(transaction => transaction.IsSelectedForBatch);
        AreAllVisibleTransactionsSelected = visibleTransactions.Count > 0 &&
                                            visibleTransactions.All(transaction => transaction.IsSelectedForBatch);
        OnPropertyChanged(nameof(CheckAllButtonText));
        OnPropertyChanged(nameof(DeleteSelectedButtonText));
        OnPropertyChanged(nameof(SelectedVisibleTotal));
        ApplyBatchPreview();
    }

    [RelayCommand]
    private async Task ApplyBatchTransactionUpdatesAsync()
    {
        var selectedTransactions = GetSelectedVisibleBatchTransactions();
        if (selectedTransactions.Count == 0 ||
            (SelectedBatchAccountId is null && SelectedBatchTagId is null))
        {
            return;
        }

        await _dataOperationRunner.RunAsync(async (scope, ct) =>
        {
            Account? targetSource = null;
            if (SelectedBatchAccountId is { } sourceId)
                targetSource = await scope.UnitOfWork.Accounts.GetByIdAsync(sourceId, ct);

            Tag? targetTag = null;
            if (SelectedBatchTagId is { } tagId)
                targetTag = await scope.UnitOfWork.Tags.GetByIdAsync(tagId, ct);

            foreach (var transaction in selectedTransactions)
            {
                var persisted = await scope.UnitOfWork.Transactions.GetByIdAsync(transaction.Id, ct);
                if (persisted is null)
                    continue;

                if (targetSource is not null)
                {
                    persisted.Account = targetSource;
                    persisted.SourceAccountId = targetSource.Id;
                }

                if (persisted.Type == TransactionType.Expense && targetTag is not null && !transaction.IsGoal)
                {
                    persisted.Tag = targetTag;
                    persisted.TagId = targetTag.Id;
                }

                scope.UnitOfWork.Transactions.Update(persisted);
            }

            await scope.UnitOfWork.SaveChangesAsync(ct);
        });

        _batchPreviewSnapshots.Clear();
        SelectedBatchAccountId = null;
        SelectedBatchTagId = null;
        await LoadAsync();
        if (IsSelectionModeEnabled)
        {
            foreach (var transaction in GetVisibleTransactions())
                transaction.IsSelectedForBatch = true;
            RefreshBatchSelectionState();
        }
    }

    [RelayCommand]
    private async Task RemoveSelectedTransactionsAsync()
    {
        var selectedTransactions = GetSelectedVisibleBatchTransactions();
        if (selectedTransactions.Count == 0)
            return;

        await _dataOperationRunner.RunAsync(async (scope, ct) =>
        {
            foreach (var transaction in selectedTransactions)
            {
                var persisted = await scope.UnitOfWork.Transactions.GetByIdAsync(transaction.Id, ct);
                if (persisted is null)
                    continue;

                persisted.IsForDeletion = true;
                scope.UnitOfWork.Transactions.Update(persisted);
            }

            await scope.UnitOfWork.SaveChangesAsync(ct);
        });

        foreach (var transaction in selectedTransactions)
            _transactions.Remove(transaction);

        _batchPreviewSnapshots.Clear();
        HasTransactions = _transactions.Count > 0;
        RefreshSummaries();
        TransactionsView.Refresh();
        RefreshVisibleTransactionState();
    }

    public void ClearFilters()
    {
        ResetFilter(TypeFilters);
        ResetFilter(AccountFilters);
        ResetFilter(CategoryFilters);
        ResetFilter(TagFilters);
        SearchText = string.Empty;
        ApplyFilters();
    }

    public void ApplyTagFilter(int tagId)
    {
        ApplySpecificFilter(TagFilters, tagId);
        ApplyFilters();
    }

    public void ApplyAccountFilter(int accountId)
    {
        ApplySpecificFilter(AccountFilters, accountId);
        ApplyFilters();
    }

    private void NormalizeFilterSelection<T>(
        ObservableCollection<LedgerFilterOption<T>> options,
        LedgerFilterOption<T> changedOption)
    {
        var allOption = options.First(option => option.IsAll);
        var specificOptions = options.Where(option => !option.IsAll).ToList();

        _isSynchronizingFilters = true;
        try
        {
            if (changedOption.IsAll && changedOption.IsChecked)
            {
                foreach (var option in specificOptions)
                    option.IsChecked = false;
                return;
            }

            if (!changedOption.IsAll && changedOption.IsChecked)
                allOption.IsChecked = false;

            if (specificOptions.Count > 0 && specificOptions.All(option => option.IsChecked))
            {
                foreach (var option in specificOptions)
                    option.IsChecked = false;
                allOption.IsChecked = true;
                return;
            }

            if (!options.Any(option => option.IsChecked))
                allOption.IsChecked = true;
        }
        finally
        {
            _isSynchronizingFilters = false;
        }
    }

    private void ResetFilter<T>(ObservableCollection<LedgerFilterOption<T>> options)
    {
        _isSynchronizingFilters = true;
        try
        {
            foreach (var option in options)
                option.IsChecked = option.IsAll;
        }
        finally
        {
            _isSynchronizingFilters = false;
        }

        OnPropertyChanged(nameof(HasPendingFilterChanges));
        OnPropertyChanged(nameof(HasActiveFilters));
        RefreshFilterSelectionPresentation(options);
    }

    private void ApplySpecificFilter<T>(ObservableCollection<LedgerFilterOption<T>> options, T value)
    {
        var selectedOption = options.FirstOrDefault(option =>
            !option.IsAll && EqualityComparer<T>.Default.Equals(option.Value, value));
        if (selectedOption is null)
            return;

        var allOption = options.First(option => option.IsAll);
        _isSynchronizingFilters = true;
        try
        {
            allOption.IsChecked = false;
            selectedOption.IsChecked = true;
        }
        finally
        {
            _isSynchronizingFilters = false;
        }

        NormalizeFilterSelection(options, selectedOption);
        OnPropertyChanged(nameof(HasPendingFilterChanges));
        RefreshFilterSelectionPresentation(options);
    }

    private static int CountSpecificSelections<T>(IEnumerable<LedgerFilterOption<T>> options)
    {
        return options.Count(option => !option.IsAll && option.IsChecked);
    }

    private static string? BuildSpecificSelectionToolTip<T>(IEnumerable<LedgerFilterOption<T>> options)
    {
        var labels = options
            .Where(option => !option.IsAll && option.IsChecked)
            .Select(option => option.Label)
            .ToList();

        return labels.Count == 0
            ? null
            : string.Join(Environment.NewLine, labels);
    }

    private void RefreshFilterSelectionPresentation(object? filterSource)
    {
        switch (filterSource)
        {
            case LedgerFilterOption<LedgerTransactionKind>:
            case ObservableCollection<LedgerFilterOption<LedgerTransactionKind>>:
                RefreshTypeFilterSelectionPresentation();
                break;
            case LedgerFilterOption<int> source when AccountFilters.Contains(source):
            case ObservableCollection<LedgerFilterOption<int>> sourceCollection when ReferenceEquals(sourceCollection, AccountFilters):
                RefreshAccountFilterSelectionPresentation();
                break;
            case LedgerFilterOption<int> tag when TagFilters.Contains(tag):
            case ObservableCollection<LedgerFilterOption<int>> tagCollection when ReferenceEquals(tagCollection, TagFilters):
                RefreshTagFilterSelectionPresentation();
                break;
            case LedgerFilterOption<LedgerCategoryFilter>:
            case ObservableCollection<LedgerFilterOption<LedgerCategoryFilter>>:
                RefreshCategoryFilterSelectionPresentation();
                break;
        }
    }

    private void RefreshAllFilterSelectionPresentations()
    {
        RefreshTypeFilterSelectionPresentation();
        RefreshAccountFilterSelectionPresentation();
        RefreshCategoryFilterSelectionPresentation();
        RefreshTagFilterSelectionPresentation();
    }

    private void RefreshTypeFilterSelectionPresentation()
    {
        OnPropertyChanged(nameof(TypeFilterSelectionCount));
        OnPropertyChanged(nameof(TypeFilterSelectionToolTip));
        TypeFilterPresentation.Refresh();
    }

    private void RefreshAccountFilterSelectionPresentation()
    {
        OnPropertyChanged(nameof(AccountFilterSelectionCount));
        OnPropertyChanged(nameof(AccountFilterSelectionToolTip));
        AccountFilterPresentation.Refresh();
    }

    private void RefreshCategoryFilterSelectionPresentation()
    {
        OnPropertyChanged(nameof(CategoryFilterSelectionCount));
        OnPropertyChanged(nameof(CategoryFilterSelectionToolTip));
        CategoryFilterPresentation.Refresh();
    }

    private void RefreshTagFilterSelectionPresentation()
    {
        OnPropertyChanged(nameof(TagFilterSelectionCount));
        OnPropertyChanged(nameof(TagFilterSelectionToolTip));
        TagFilterPresentation.Refresh();
    }

    private LedgerFilterSelectionSnapshot CaptureFilterSelectionSnapshot()
    {
        return new LedgerFilterSelectionSnapshot(
            CaptureFilterSelection(TypeFilters),
            CaptureFilterSelection(AccountFilters),
            CaptureFilterSelection(CategoryFilters),
            CaptureFilterSelection(TagFilters));
    }

    private static string CaptureFilterSelection<T>(IEnumerable<LedgerFilterOption<T>> options)
    {
        return string.Join(
            "|",
            options
                .Where(option => option.IsChecked)
                .Select(option => option.IsAll ? "all" : option.Value?.ToString() ?? string.Empty));
    }

    private bool FilterTransaction(object item)
    {
        if (item is not LedgerTransactionItemVM transaction)
            return false;

        return !transaction.HasChildTransactions
            ? MatchesTransactionFilters(transaction)
            : !HasActiveFilters || transaction.ChildTransactions.Any(MatchesTransactionFilters);
    }

    private bool MatchesTransactionFilters(LedgerTransactionItemVM transaction)
    {
        if (!string.IsNullOrWhiteSpace(SearchText) &&
            transaction.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) is false)
            return false;

        if (!MatchesFilter(TypeFilters, transaction.Kind))
            return false;

        if (!MatchesFilter(AccountFilters, transaction.AccountId))
            return false;

        if (!MatchesCategoryFilter(transaction))
            return false;

        if (transaction.Kind == LedgerTransactionKind.Expense &&
            !MatchesFilter(TagFilters, transaction.TagId))
            return false;

        return true;
    }

    private void RefreshVisibleChildTransactions()
    {
        foreach (var transaction in _transactions.Where(transaction => transaction.HasChildTransactions))
            transaction.SetVisibleChildTransactions(transaction.ChildTransactions.Where(MatchesTransactionFilters));
    }

    private static bool MatchesFilter<T>(IEnumerable<LedgerFilterOption<T>> options, T value)
    {
        var selectedOptions = options.Where(option => option.IsChecked).ToList();
        return selectedOptions.Any(option => option.IsAll) ||
               selectedOptions.Any(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private bool MatchesCategoryFilter(LedgerTransactionItemVM transaction)
    {
        var selected = CategoryFilters.Where(option => option.IsChecked).ToList();
        if (selected.Any(option => option.IsAll))
            return true;

        if (transaction.IsExcludedFromBudget)
            return selected.Any(option => option.Value == LedgerCategoryFilter.Excluded);

        if (transaction.Kind != LedgerTransactionKind.Expense)
            return false;

        var category = transaction.Category switch
        {
            ExpenseCategory.Wants => LedgerCategoryFilter.Wants,
            ExpenseCategory.Savings => LedgerCategoryFilter.Invest,
            _ => LedgerCategoryFilter.Needs
        };
        return selected.Any(option => option.Value == category);
    }

    private bool IsInSelectedRange(TransactionVM log)
    {
        return _selectedRange is not { } range ||
               log.OccurredOn.Date >= range.From.Date && log.OccurredOn.Date <= range.To.Date;
    }

    private static LedgerTransactionItemVM ProjectExpense(TransactionVM log, bool isChildTransaction = false)
    {
        var tagName = log.Tag?.Name ?? string.Empty;
        return new LedgerTransactionItemVM
        {
            Id = log.Id,
            Kind = LedgerTransactionKind.Expense,
            Name = log.Name,
            Amount = log.Amount,
            OccurredOn = log.OccurredOn,
            LoggedOn = log.LoggedOn,
            Category = log.ExpenseCategory ?? ExpenseCategory.Needs,
            IsExcludedFromBudget = log.IsExcludedFromBudget,
            ParentTransactionId = log.ParentTransactionId,
            IsChildTransaction = isChildTransaction,
            AccountId = log.Account?.Id ?? 0,
            AccountName = log.Account?.Name ?? string.Empty,
            TagId = log.Tag?.Id ?? 0,
            TagName = tagName,
            TagHexCode = log.Tag?.HexCode ?? string.Empty,
            IsGoal = string.Equals(tagName, "Goal Update", StringComparison.OrdinalIgnoreCase),
            IsRecurring = log.Notes.Contains("recurring", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static LedgerTransactionItemVM ProjectIncome(TransactionVM log)
    {
        return new LedgerTransactionItemVM
        {
            Id = log.Id,
            Kind = LedgerTransactionKind.Income,
            Name = log.Name,
            Amount = log.Amount,
            OccurredOn = log.OccurredOn,
            LoggedOn = log.LoggedOn,
            IsExcludedFromBudget = log.IsExcludedFromBudget,
            AccountId = log.Account?.Id ?? 0,
            AccountName = log.Account?.Name ?? string.Empty
        };
    }

    private void UpdateSortAndGroups()
    {
        using (TransactionsView.DeferRefresh())
        {
            TransactionsView.GroupDescriptions.Clear();
            if (GetGroupPropertyName(SelectedGroupingMode) is { } groupPropertyName)
                TransactionsView.GroupDescriptions.Add(new PropertyGroupDescription(groupPropertyName));

            if (TransactionsView is ListCollectionView listCollectionView)
            {
                listCollectionView.CustomSort = new LedgerTransactionComparer(
                    SelectedGroupingMode,
                    AmountSortDirection);
                return;
            }

            TransactionsView.SortDescriptions.Clear();
            TransactionsView.SortDescriptions.Add(new SortDescription(
                nameof(LedgerTransactionItemVM.LoggedOn),
                AmountSortDirection == LedgerAmountSortDirection.Ascending
                    ? ListSortDirection.Ascending
                    : ListSortDirection.Descending));
        }
    }

    private static string? GetGroupPropertyName(LedgerGroupingMode groupingMode)
    {
        return groupingMode switch
        {
            LedgerGroupingMode.None => null,
            LedgerGroupingMode.Date => nameof(LedgerTransactionItemVM.DateGroupKey),
            LedgerGroupingMode.Tags => nameof(LedgerTransactionItemVM.TagGroupKey),
            LedgerGroupingMode.Accounts => nameof(LedgerTransactionItemVM.AccountGroupKey),
            LedgerGroupingMode.Types => nameof(LedgerTransactionItemVM.TypeGroupKey),
            LedgerGroupingMode.Category => nameof(LedgerTransactionItemVM.CategoryGroupKey),
            _ => null
        };
    }

    private void RefreshSummaries()
    {
        SpentAmount = _transactions
            .Where(transaction => transaction.Kind == LedgerTransactionKind.Expense && !transaction.IsGoal)
            .Sum(transaction => transaction.Amount);
        EarnedAmount = _transactions
            .Where(transaction => transaction.Kind == LedgerTransactionKind.Income)
            .Sum(transaction => transaction.Amount);
        GoalAmount = _transactions
            .Where(transaction => transaction.IsGoal)
            .Sum(transaction => transaction.Amount);
        NetAmount = EarnedAmount - SpentAmount - GoalAmount;
    }

    private async Task RemoveExpenseTransactionAsync(LedgerTransactionItemVM transaction)
    {
        var snapshot = await _dataOperationRunner.RunAsync(async (scope, ct) =>
        {
            var expenseLog = await scope.UnitOfWork.Transactions.GetByIdAsync(transaction.Id, ct);
            if (expenseLog is null)
                return null;

            var result = TransactionMemorySnapshot.Create(expenseLog);
            expenseLog.IsForDeletion = true;
            scope.UnitOfWork.Transactions.Update(expenseLog);
            await scope.UnitOfWork.SaveChangesAsync(ct);
            return result;
        });

        if (snapshot is null)
            return;

        _transactions.Remove(transaction);
        HasTransactions = _transactions.Count > 0;
        Messenger.Send(new RecordLogMemoryMessage(new DeleteTransactionMemoryAction(snapshot)));
        RefreshSummaries();
        TransactionsView.Refresh();
        RefreshVisibleTransactionState();
    }

    private async Task RemoveIncomeTransactionAsync(LedgerTransactionItemVM transaction)
    {
        var snapshot = await _dataOperationRunner.RunAsync(async (scope, ct) =>
        {
            var incomeLog = await scope.UnitOfWork.Transactions.GetByIdAsync(transaction.Id, ct);
            if (incomeLog is null)
                return null;

            var result = TransactionMemorySnapshot.Create(incomeLog);
            incomeLog.IsForDeletion = true;
            scope.UnitOfWork.Transactions.Update(incomeLog);
            await scope.UnitOfWork.SaveChangesAsync(ct);
            return result;
        });

        if (snapshot is null)
            return;

        _transactions.Remove(transaction);
        HasTransactions = _transactions.Count > 0;
        Messenger.Send(new RecordLogMemoryMessage(new DeleteTransactionMemoryAction(snapshot)));
        RefreshSummaries();
        TransactionsView.Refresh();
        RefreshVisibleTransactionState();
    }

    private void RefreshVisibleTransactionState()
    {
        foreach (var transaction in _transactions)
            transaction.IsLastVisibleInGroup = false;

        var visibleTransactions = GetVisibleTransactions();
        HasVisibleTransactions = visibleTransactions.Count > 0;

        if (SelectedGroupingMode == LedgerGroupingMode.None)
        {
            if (visibleTransactions.LastOrDefault() is { } lastTransaction)
                lastTransaction.IsLastVisibleInGroup = true;
            RefreshBatchSelectionState();
            return;
        }

        foreach (var group in visibleTransactions.GroupBy(GetVisibleGroupKey))
        {
            if (group.LastOrDefault() is { } lastTransaction)
                lastTransaction.IsLastVisibleInGroup = true;
        }

        RefreshBatchSelectionState();
    }

    private IReadOnlyList<LedgerTransactionItemVM> GetVisibleTransactions()
    {
        return TransactionsView.Cast<LedgerTransactionItemVM>().ToList();
    }

    private IReadOnlyList<LedgerTransactionItemVM> GetSelectedVisibleBatchTransactions()
    {
        return GetVisibleTransactions()
            .Where(transaction => transaction.IsSelectedForBatch)
            .ToList();
    }

    private void ApplyBatchPreview()
    {
        if (!IsSelectionModeEnabled)
            return;

        if (SelectedBatchAccountId is null && SelectedBatchTagId is null)
        {
            RestoreAllBatchPreviewSnapshots();
            return;
        }

        var visibleTransactions = GetVisibleTransactions();
        foreach (var transaction in visibleTransactions.Where(transaction => !transaction.IsSelectedForBatch))
            RestoreBatchPreviewSnapshot(transaction);

        foreach (var transaction in visibleTransactions.Where(transaction => transaction.IsSelectedForBatch))
        {
            CaptureBatchPreviewSnapshot(transaction);

            if (SelectedBatchAccountId is { } sourceId)
            {
                transaction.AccountId = sourceId;
                transaction.AccountName = AccountFilters
                    .FirstOrDefault(option => !option.IsAll && option.Value == sourceId)
                    ?.Label ?? transaction.AccountName;
            }

            if (SelectedBatchTagId is { } tagId &&
                transaction.Kind == LedgerTransactionKind.Expense &&
                !transaction.IsGoal)
            {
                transaction.TagId = tagId;
                transaction.TagName = TagFilters
                    .FirstOrDefault(option => !option.IsAll && option.Value == tagId)
                    ?.Label ?? transaction.TagName;
                transaction.TagHexCode = EditableTags
                    .FirstOrDefault(tag => tag.Id == tagId)
                    ?.HexCode ?? transaction.TagHexCode;
            }
        }
    }

    private void CaptureBatchPreviewSnapshot(LedgerTransactionItemVM transaction)
    {
        var key = (transaction.Kind, transaction.Id);
        if (_batchPreviewSnapshots.ContainsKey(key))
            return;

        _batchPreviewSnapshots[key] = new BatchPreviewSnapshot(
            transaction.AccountId,
            transaction.AccountName,
            transaction.TagId,
            transaction.TagName,
            transaction.TagHexCode);
    }

    private void RestoreAllBatchPreviewSnapshots()
    {
        foreach (var transaction in _transactions)
            RestoreBatchPreviewSnapshot(transaction);

        _batchPreviewSnapshots.Clear();
    }

    private void RestoreBatchPreviewSnapshot(LedgerTransactionItemVM transaction)
    {
        var key = (transaction.Kind, transaction.Id);
        if (!_batchPreviewSnapshots.Remove(key, out var snapshot))
            return;

        transaction.AccountId = snapshot.AccountId;
        transaction.AccountName = snapshot.AccountName;
        transaction.TagId = snapshot.TagId;
        transaction.TagName = snapshot.TagName;
        transaction.TagHexCode = snapshot.TagHexCode;
    }

    private string GetVisibleGroupKey(LedgerTransactionItemVM transaction)
    {
        return SelectedGroupingMode switch
        {
            LedgerGroupingMode.Date => transaction.DateGroupKey,
            LedgerGroupingMode.Tags => transaction.TagGroupKey,
            LedgerGroupingMode.Accounts => transaction.AccountGroupKey,
            LedgerGroupingMode.Types => transaction.TypeGroupKey,
            LedgerGroupingMode.Category => transaction.CategoryGroupKey,
            _ => string.Empty
        };
    }

    private string BuildSelectedPeriodText()
    {
        if (_selectedRange is not { } range)
            return "all time";

        var start = range.From.Date;
        var end = range.To.Date;
        var dayRange = start == end
            ? DateRangeResolver.Resolve(start, MainContentViewMode.Daily)
            : null;
        var from = dayRange?.From ?? start;
        var to = dayRange?.To ?? end;

        return from.Date == to.Date
            ? FormatPeriodDate(from)
            : $"{FormatPeriodDate(from)} to {FormatPeriodDate(to)}";
    }

    private static string FormatPeriodDate(DateTime date)
    {
        return date.ToString("MMMM d", CultureInfo.InvariantCulture);
    }

    private sealed class LedgerTransactionComparer(
        LedgerGroupingMode groupingMode,
        LedgerAmountSortDirection sortDirection)
        : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is not LedgerTransactionItemVM left)
                return -1;
            if (y is not LedgerTransactionItemVM right)
                return 1;

            var groupComparison = CompareGroup(left, right);
            if (groupComparison != 0)
                return groupComparison;

            var loggedOnComparison = left.LoggedOn.CompareTo(right.LoggedOn);
            if (sortDirection == LedgerAmountSortDirection.Descending)
                loggedOnComparison *= -1;
            if (loggedOnComparison != 0)
                return loggedOnComparison;

            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        private int CompareGroup(LedgerTransactionItemVM left, LedgerTransactionItemVM right)
        {
            return groupingMode switch
            {
                LedgerGroupingMode.Date => right.OccurredOn.Date.CompareTo(left.OccurredOn.Date),
                LedgerGroupingMode.Tags => string.Compare(left.TagGroupKey, right.TagGroupKey, StringComparison.OrdinalIgnoreCase),
                LedgerGroupingMode.Accounts => string.Compare(left.AccountGroupKey, right.AccountGroupKey, StringComparison.OrdinalIgnoreCase),
                LedgerGroupingMode.Types => 0,
                LedgerGroupingMode.Category => 0,
                _ => 0
            };
        }
    }

    private readonly record struct LedgerFilterSelectionSnapshot(
        string Type,
        string Account,
        string Category,
        string Tag)
    {
        public static LedgerFilterSelectionSnapshot Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private readonly record struct BatchPreviewSnapshot(
        int AccountId,
        string AccountName,
        int TagId,
        string TagName,
        string TagHexCode);
}

public sealed class LedgerGroupingModeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            LedgerGroupingMode.Accounts => "Accounts",
            LedgerGroupingMode groupingMode => groupingMode.ToString(),
            _ => string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class LedgerFilterSelectionPresentation(
    string label,
    Func<int> selectionCountProvider,
    Func<string?> selectionToolTipProvider)
    : ObservableObject
{
    public string Label { get; } = label;
    public int SelectionCount => selectionCountProvider();
    public string? SelectionToolTip => selectionToolTipProvider();

    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionToolTip));
    }
}
