using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Fluxo.Resources.CustomControls;
using Fluxo.Resources.Infrastructure;
using Fluxo.Resources.Styles;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;

namespace Fluxo.Views.Popups;

public partial class TransactionPopup : BasePopup
{
    private enum MoreTagsPopupLifecycleState
    {
        Closed,
        Opening,
        Open,
        Closing
    }

    private readonly TransactionPopupVM _viewModel;
    private readonly TransactionBulkQueueVM _bulkQueueViewModel;
    private readonly TransactionSplitsVM _splitsViewModel;
    private bool _isInitialized;
    private bool _isHandlingAddTagSelection;
    private readonly DispatcherTimer _moreTagsHoverCloseTimer;
    private MoreTagsPopupLifecycleState _moreTagsPopupState = MoreTagsPopupLifecycleState.Closed;
    private bool _isSyncingNoteDocument;

    public TransactionPopup(
        TransactionPopupVM viewModel,
        TransactionBulkQueueVM bulkQueueViewModel,
        TransactionSplitsVM splitsViewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _bulkQueueViewModel = bulkQueueViewModel;
        _splitsViewModel = splitsViewModel;
        DataContext = viewModel;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, OnDeleteQueuedTransactionExecuted));
        BulkQueuePanel.DataContext = bulkQueueViewModel;
        SplitPanel.DataContext = splitsViewModel;
        BulkInsertChecked += (_, _) => _viewModel.IsBulkMode = true;
        BulkInsertUnchecked += OnBulkInsertUnchecked;
        _moreTagsHoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _moreTagsHoverCloseTimer.Tick += (_, _) =>
        {
            _moreTagsHoverCloseTimer.Stop();
            TryCloseMoreTagsPopupIfNotPinned();
        };

        Loaded += async (_, _) =>
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            if (!await _viewModel.InitializeAsync())
            {
                Close();
                return;
            }
            await _viewModel.EnsureTagsLoadedAsync();
            if (_viewModel.IsHistoryOpen)
                await _viewModel.LoadHistoryAsync();
            RecalculateTagLayout();
            SyncMoreTagsPopupState();
            SyncNameSuggestionsPopupState();
            SyncNoteDocumentFromViewModel();
            _viewModel.BeginChangeTracking();
            FocusPrimaryInput();
        };

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _splitsViewModel.PropertyChanged += OnSplitsViewModelPropertyChanged;
        Unloaded += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _splitsViewModel.PropertyChanged -= OnSplitsViewModelPropertyChanged;
        };
        Closed += (_, _) =>
        {
            _viewModel.Dispose();
            _bulkQueueViewModel.Dispose();
            _splitsViewModel.Dispose();
        };
        TagsDockPanel.SizeChanged += (_, _) => RecalculateTagLayout();
        PreviewMouseDown += OnPopupPreviewMouseDown;
    }

    internal void Configure(TransactionPopupRequest request) => _viewModel.Configure(request);

    internal bool IsViewingTransaction(int transactionId) => _viewModel.ViewedTransaction?.Id == transactionId;

    public bool IsOwnedBy(Guid ownerToken) => IsActive && _viewModel.AddTagOwnerToken == ownerToken;

    public TransactionSplitsVM SplitsViewModel => _splitsViewModel;

    protected override async void OnSaveButtonClick()
    {
        if (_viewModel.IsProcessingSession && !await ShouldSaveQueuedTransactionsAsync())
            return;

        if (_viewModel.IsBulkMode || _viewModel.IsProcessingSession)
        {
            await FinishQueuedTransactionsAsync();
            return;
        }

        if (_viewModel.IsEditingViewedTransaction)
        {
            if (_viewModel.ViewedTransaction is null)
                return;

            var editResult = await TrySaveWithMaximumSpendingConfirmationAsync(false);
            if (editResult is not { } editResultValue)
                return;
            if (!editResultValue.IsSuccess)
            {
                ShowValidationMessage(editResultValue.ErrorMessage);
                return;
            }

            Close();
            return;
        }

        if (!await ShouldSaveCurrentTransactionAsync())
            return;

        var result = await TrySaveWithMaximumSpendingConfirmationAsync(false);
        if (result is not { } resultValue)
            return;
        if (!resultValue.IsSuccess)
        {
            ShowValidationMessage(resultValue.ErrorMessage);
            return;
        }

        Close();
    }

    protected override async void OnEditButtonClick()
    {
        await _viewModel.BeginEditingViewedTransactionAsync();
        FocusPrimaryInput();
    }

    protected override void OnDiscardButtonClick()
    {
        if (!_viewModel.IsEditingViewedTransaction)
        {
            base.OnDiscardButtonClick();
            return;
        }

        if (_viewModel.HasChanges && FluxoMessageBox.Show(this, "Discard your changes?", "Transaction Detail",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _viewModel.DiscardEditingViewedTransaction();
        ConfigureViewModeFocus();
    }

    protected override void OnCloneButtonClick()
    {
        if (_viewModel.ViewedTransaction is null)
            return;

        _viewModel.SwitchToCloneAddMode();
        RecalculateTagLayout();
        SyncMoreTagsPopupState();
        SyncNameSuggestionsPopupState();
        SyncNoteDocumentFromViewModel();
        FocusPrimaryInput();
    }

    protected override async void OnDeleteButtonClick()
    {
        if (_viewModel.ViewedTransaction is null ||
            FluxoMessageBox.Show(this, "Delete this transaction?", "Transaction Detail", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var result = await _viewModel.DeleteAsync();
        if (!result.IsSuccess)
        {
            ShowValidationMessage(result.ErrorMessage);
            return;
        }

        Close();
    }

    private void OnBulkInsertUnchecked(object? sender, BulkInsertUncheckedEventArgs e)
    {
        _viewModel.IsBulkMode = false;
        e.ShouldSwitchToSaveOnly = true;
    }

    private async Task<TransactionPopupSubmissionResult?>
        TrySaveWithMaximumSpendingConfirmationAsync(bool resetAfterSave)
    {
        var result = await _viewModel.SaveAsync(resetAfterSave);
        if (!result.RequiresConfirmation)
            return result;

        var saveAnyway = FluxoMessageBox.Show(
            this,
            result.ErrorMessage ?? "This expense exceeds the account's maximum spending limit. Save anyway?",
            "Transaction",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
        if (!saveAnyway)
            return null;

        return await _viewModel.SaveAsync(resetAfterSave, allowMaximumSpendingOverflow: true);
    }

    private async Task<bool> ShouldSaveCurrentTransactionAsync()
    {
        if (_viewModel.TryGetRepaymentCorrection(out var correctedAmount))
        {
            var useCorrectAmount = FluxoMessageBox.Show(
                this,
                $"Repayment exceeds the credit account's spent amount. Use {correctedAmount:N2} instead?",
                "Invalid Repayment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!useCorrectAmount)
            {
                _viewModel.RejectRepaymentCorrection();
                return false;
            }

            _viewModel.AcceptRepaymentCorrection();
        }

        if (!await _viewModel.HasSimilarTransactionAsync())
            return true;

        return FluxoMessageBox.Show(
            this,
            "Potentially duplicated transaction found. Would you like to save the current one?",
            "Add New Transaction",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && NoteRichTextBox.IsKeyboardFocusWithin && Keyboard.Modifiers != ModifierKeys.Shift)
            return;

        base.OnPreviewKeyDown(e);
    }

    protected override void OnCloseButtonClick()
    {
        if ((_viewModel.IsEditingViewedTransaction && _viewModel.HasPendingTransactionChanges) ||
            (!_viewModel.IsEditingViewedTransaction && _viewModel.HasChanges))
        {
            var confirmation = FluxoMessageBox.Show(
                this,
                "Close without saving your changes?",
                _viewModel.PopupTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;
        }

        base.OnCloseButtonClick();
    }

    private void OnNoteTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingNoteDocument)
            return;

        _viewModel.NoteText = NoteRichTextBox.Text;
    }

    private void ShowValidationMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        FloatingNotificationPublisher.SaveFailed("Transaction not saved", [message]);
    }

    private void FocusPrimaryInput()
    {
        if (_viewModel.IsViewOnly)
        {
            ConfigureViewModeFocus();
            return;
        }

        if (_viewModel.IsGoal)
        {
            ExpenseAmountTextBox.Focus();
            return;
        }

        if (_viewModel.IsRepayment)
        {
            RepaymentAmountTextBox.Focus();
            return;
        }

        ExpenseNameTextBox.Focus();
    }

    private void ConfigureViewModeFocus()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            foreach (var control in GetDescendantControls(this))
            {
                control.Focusable = false;
                KeyboardNavigation.SetIsTabStop(control, false);
            }

            Focus();
        }));
    }

    private static IEnumerable<Control> GetDescendantControls(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Control control)
                yield return control;

            foreach (var descendant in GetDescendantControls(child))
                yield return descendant;
        }
    }

    private async Task FinishQueuedTransactionsAsync()
    {
        var result = await _viewModel.FinishQueuedTransactionsAsync();
        if (result.RequiresConfirmation)
        {
            var saveAnyway = FluxoMessageBox.Show(
                this,
                result.ErrorMessage ?? "This expense exceeds the account's maximum spending limit. Save anyway?",
                "Transaction",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
            if (!saveAnyway)
                return;

            result = await _viewModel.FinishQueuedTransactionsAsync(allowMaximumSpendingOverflow: true);
        }

        if (result.IsSuccess)
        {
            Close();
            return;
        }

        var count = _bulkQueueViewModel.QueuedTransactions.Count;
        FluxoMessageBox.Show(this,
            count == 1 ? "1 transaction has not been saved." : $"{count} transactions have not been saved.",
            "Transaction", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task<bool> ShouldSaveQueuedTransactionsAsync()
    {
        foreach (var transaction in _bulkQueueViewModel.QueuedTransactions.ToList())
        {
            _bulkQueueViewModel.SelectedQueuedTransaction = transaction;
            if (!await ShouldSaveCurrentTransactionAsync())
                return false;
        }

        return true;
    }

    private void SyncNoteDocumentFromViewModel()
    {
        _isSyncingNoteDocument = true;

        try
        {
            var noteText = _viewModel.NoteText ?? string.Empty;
            NoteRichTextBox.Text = noteText;
        }
        finally
        {
            _isSyncingNoteDocument = false;
        }
    }

    private async void OnAddTagClick(object sender, RoutedEventArgs e)
    {
        if (_isHandlingAddTagSelection)
            return;

        _isHandlingAddTagSelection = true;
        try
        {
            var previousTagNames = _viewModel.VisibleTags
                .Concat(_viewModel.OverflowTags)
                .Select(tag => tag.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _viewModel.RequestAddTag();
            await _viewModel.EnsureTagsLoadedAsync();
            RecalculateTagLayout();

            var newTag = _viewModel.VisibleTags
                .Concat(_viewModel.OverflowTags)
                .FirstOrDefault(tag =>
                    !string.IsNullOrWhiteSpace(tag.Name) &&
                    !previousTagNames.Contains(tag.Name));

            if (newTag is not null)
                _viewModel.SelectedTag = newTag;
        }
        finally
        {
            _isHandlingAddTagSelection = false;
        }
    }

    private void OnTagSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            RecalculateTagLayout();
            SyncMoreTagsPopupState();
        }));
    }

    private void OnTransactionNameSuggestionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
            return;

        if (listBox.SelectedItem is not AddNewTransactionSuggestion suggestion)
            return;

        _viewModel.ApplyTransactionNameSuggestion(suggestion);
        SyncNoteDocumentFromViewModel();
        listBox.SelectedItem = null;
        SyncNameSuggestionsPopupState();
    }

    private void OnHistoryListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
            return;

        var scrollViewer = DependencyObjectTree.FindAncestor<ScrollViewer>(DependencyObjectTree.GetParent(source));
        if (scrollViewer is null)
            return;

        var wheelSteps = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - wheelSteps * 48d);
        e.Handled = true;
    }

    private void OnBulkQueuePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = source as ListBoxItem ?? DependencyObjectTree.FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is TransactionVM transaction)
            _bulkQueueViewModel.ActivateQueuedTransactionCommand.Execute(transaction);
    }

    private void OnDeleteQueuedTransactionExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not TransactionVM transaction ||
            FluxoMessageBox.Show(this, "Delete this transaction?", "Transaction", MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _viewModel.RemoveQueuedTransaction(transaction);
    }

    private void OnSplitPanelPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        var wheelSteps = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - wheelSteps * 48d);
        e.Handled = true;
    }

    private void OnRootSplitCardClick(object sender, RoutedEventArgs e)
    {
        _splitsViewModel.SelectSplitCommand.Execute(null);
        TransactionSplitTreeStyles.SyncSelection(SplitTransactionTree, null);
    }

    private void OnTransactionTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not SegmentedToggleOption option || option.IsSelected)
            return;

        if (_viewModel.IsExpense && _splitsViewModel.HasSplitTransactions &&
            FluxoMessageBox.Show(
                this,
                "Switching transaction type will remove all sub-transactions. Continue?",
                "Change Transaction Type",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (_viewModel.IsExpense && _splitsViewModel.HasSplitTransactions)
            _splitsViewModel.ClearSplitTransactions();

        if (ReferenceEquals(option, ExpenseRadioButton))
            _viewModel.IsExpense = true;
        else if (ReferenceEquals(option, IncomeRadioButton))
            _viewModel.IsIncome = true;
        else if (ReferenceEquals(option, GoalRadioButton))
            _viewModel.IsGoal = true;
        else if (ReferenceEquals(option, RepaymentRadioButton))
            _viewModel.IsRepayment = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TransactionPopupVM.HasTransactionNameSuggestions) or
            nameof(TransactionPopupVM.IsExpense) or
            nameof(TransactionPopupVM.IsGoal) or
            nameof(TransactionPopupVM.IsIncome))
            SyncNameSuggestionsPopupState();

        if (e.PropertyName is nameof(TransactionPopupVM.SelectedPinnedHistoryItem) or
            nameof(TransactionPopupVM.SelectedHistoryItem) or
            nameof(TransactionPopupVM.PendingTransaction))
        {
            SyncNoteDocumentFromViewModel();
            FocusPrimaryInput();
        }

        if (e.PropertyName == nameof(TransactionPopupVM.SelectedSidePanel))
            Dispatcher.BeginInvoke(() => TransactionSplitTreeStyles.SyncSelection(
                SplitTransactionTree, _splitsViewModel.SelectedSplitTransaction), DispatcherPriority.Loaded);
    }

    private void OnSplitsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TransactionSplitsVM.SelectedSplitTransaction))
        {
            Dispatcher.BeginInvoke(() => TransactionSplitTreeStyles.SyncSelection(
                SplitTransactionTree, _splitsViewModel.SelectedSplitTransaction), DispatcherPriority.Loaded);
        }
    }

    private void OnMoreTagsButtonChecked(object sender, RoutedEventArgs e) => TryOpenMoreTagsPopup();

    private void OnMoreTagsButtonUnchecked(object sender, RoutedEventArgs e) => TryCloseMoreTagsPopup();

    private void OnMoreTagsHoverChanged(object sender, RoutedEventArgs e)
    {
        if (!CanShowMoreTagsPopup())
        {
            _moreTagsHoverCloseTimer.Stop();
            TryCloseMoreTagsPopup();
            return;
        }

        var isPointerOverMoreRegion = IsPointerOverMoreRegion();
        if (isPointerOverMoreRegion)
        {
            _moreTagsHoverCloseTimer.Stop();

            if (!_viewModel.IsMoreTagsOpen)
                TryOpenMoreTagsPopup();

            return;
        }

        if (_viewModel.IsMoreTagsOpen)
            return;

        _moreTagsHoverCloseTimer.Stop();
        _moreTagsHoverCloseTimer.Start();
    }

    private void OnMoreTagsPopupClosed(object? sender, EventArgs e)
    {
        _moreTagsHoverCloseTimer.Stop();
        _moreTagsPopupState = MoreTagsPopupLifecycleState.Closed;

        if (_viewModel.IsMoreTagsOpen)
            _viewModel.IsMoreTagsOpen = false;
    }

    private void OnPopupPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_viewModel.IsMoreTagsOpen || _moreTagsPopupState is not MoreTagsPopupLifecycleState.Open)
            return;

        if (e.OriginalSource is not DependencyObject source)
            return;

        if (DependencyObjectTree.IsDescendantOf(source, MoreTagsButton))
            return;

        _viewModel.IsMoreTagsOpen = false;
        TryCloseMoreTagsPopup();
    }

    private void SyncNameSuggestionsPopupState()
    {
        var shouldShow = IsLoaded && _viewModel.HasTransactionNameSuggestions && !_viewModel.IsGoal;
        var isNameSuggestionFocused =
            ExpenseNameTextBox.IsKeyboardFocusWithin || ExpenseNameSuggestionsListBox.IsKeyboardFocusWithin;

        ExpenseNameSuggestionsPopup.IsOpen = shouldShow && isNameSuggestionFocused;
    }

    private void OnTransactionNameTextBoxFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(SyncNameSuggestionsPopupState));
    }

    private void OnTransactionNameTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateNameField();
        OnTransactionNameTextBoxFocusChanged(sender, e);
    }

    private void OnTransactionAmountTextBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _viewModel.ValidateAmountField();
    }

    private void OnTransactionAmountTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox { IsKeyboardFocusWithin: true } textBox)
            return;

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        _viewModel.ActivateAmountValidation();
    }

    private void RecalculateTagLayout()
    {
        if (!IsLoaded || !_viewModel.IsExpense)
            return;

        var containerWidth = TagsDockPanel.ActualWidth;
        if (containerWidth <= 0)
            return;

        var orderedTags = _viewModel.VisibleTags.Concat(_viewModel.OverflowTags).ToList();
        if (orderedTags.Count == 0)
        {
            _viewModel.SetVisibleTagSlots(0);
            SyncMoreTagsPopupState();
            return;
        }

        var addTagWidth = AddTagButton.ActualWidth + AddTagButton.Margin.Left + AddTagButton.Margin.Right;
        var moreButtonWidth = MeasureMoreButtonWidth();
        var tagWidths = orderedTags.Select(MeasureTagWidth).ToList();
        var visibleSlots = CalculateVisibleTagSlots(containerWidth, addTagWidth, moreButtonWidth, tagWidths);
        _viewModel.SetVisibleTagSlots(visibleSlots);
        SyncMoreTagsPopupState();
    }

    private bool IsPointerOverMoreRegion()
    {
        return MoreTagsButton.IsMouseOver || MoreTagsPopupContent.IsMouseOver;
    }

    private bool CanShowMoreTagsPopup()
    {
        return IsLoaded && _viewModel.HasMoreTags;
    }

    private void SyncMoreTagsPopupState()
    {
        if (!CanShowMoreTagsPopup())
        {
            _viewModel.IsMoreTagsOpen = false;
            TryCloseMoreTagsPopup();
            return;
        }

        if (_viewModel.IsMoreTagsOpen || IsPointerOverMoreRegion())
            TryOpenMoreTagsPopup();
        else
            TryCloseMoreTagsPopup();
    }

    private void TryOpenMoreTagsPopup()
    {
        if (!CanShowMoreTagsPopup())
            return;

        if (_moreTagsPopupState is MoreTagsPopupLifecycleState.Open or MoreTagsPopupLifecycleState.Opening)
            return;

        _moreTagsPopupState = MoreTagsPopupLifecycleState.Opening;
        MoreTagsPopup.IsOpen = true;
        if (MoreTagsPopup.IsOpen)
            _moreTagsPopupState = MoreTagsPopupLifecycleState.Open;
        else
            _moreTagsPopupState = MoreTagsPopupLifecycleState.Closed;
    }

    private void TryCloseMoreTagsPopup()
    {
        if (_moreTagsPopupState is MoreTagsPopupLifecycleState.Closed or MoreTagsPopupLifecycleState.Closing)
            return;

        _moreTagsPopupState = MoreTagsPopupLifecycleState.Closing;
        MoreTagsPopup.IsOpen = false;
        if (!MoreTagsPopup.IsOpen)
            _moreTagsPopupState = MoreTagsPopupLifecycleState.Closed;
    }

    private void TryCloseMoreTagsPopupIfNotPinned()
    {
        if (_viewModel.IsMoreTagsOpen || IsPointerOverMoreRegion())
            return;

        TryCloseMoreTagsPopup();
    }

    private double MeasureTagWidth(TagVM tag)
    {
        var tagChip = new RadioButton
        {
            Content = tag.Name,
            DataContext = tag,
            Style = (Style)FindResource("PopupTagItemRadioStyle")
        };

        tagChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return tagChip.DesiredSize.Width + 8d;
    }

    private double MeasureMoreButtonWidth()
    {
        var moreButton = new ToggleButton
        {
            Content = "More",
            Style = (Style)FindResource("PopupTagToggleStyle")
        };

        moreButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return moreButton.DesiredSize.Width + MoreTagsButton.Margin.Left + MoreTagsButton.Margin.Right;
    }

    private static int CalculateVisibleTagSlots(
        double containerWidth,
        double addTagWidth,
        double moreButtonWidth,
        IReadOnlyList<double> tagWidths)
    {
        var remainingWidth = Math.Max(0d, containerWidth - addTagWidth);
        var totalTagsWidth = tagWidths.Sum();

        if (totalTagsWidth <= remainingWidth)
            return tagWidths.Count;

        var remainingWidthWithMore = Math.Max(0d, remainingWidth - moreButtonWidth);
        if (remainingWidthWithMore <= 0d)
            return 0;

        var consumedWidth = 0d;
        var visibleCount = 0;
        foreach (var width in tagWidths)
        {
            if (consumedWidth + width > remainingWidthWithMore)
                break;

            consumedWidth += width;
            visibleCount++;
        }

        return visibleCount;
    }
}
