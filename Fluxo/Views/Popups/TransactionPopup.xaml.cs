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
    private readonly TransactionPopupVM _viewModel;
    private readonly TransactionBulkQueueVM _bulkQueueViewModel;
    private readonly TransactionSplitsVM _splitsViewModel;
    private bool _isInitialized;
    private bool _isHandlingAddTagSelection;
    private bool _isSyncingNoteDocument;
    private Point _tagScrollStartPoint;
    private double _tagScrollStartOffset;
    private TagVM? _tagPointerDownTag;
    private bool _wasSelectedTagPointerDown;
    private bool _isDraggingTags;

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
    }

    internal void Configure(TransactionPopupRequest request) => _viewModel.Configure(request);

    internal bool IsViewingTransaction(int transactionId) => _viewModel.ViewedTransaction?.Id == transactionId;

    public bool IsOwnedBy(Guid ownerToken) => IsActive && _viewModel.AddTagOwnerToken == ownerToken;

    public TransactionSplitsVM SplitsViewModel => _splitsViewModel;

    protected override async void OnSaveButtonClick()
    {
        if ((_viewModel.IsBulkMode || _viewModel.IsProcessingSession) &&
            !await ShouldSaveQueuedTransactionsAsync())
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
        if (FluxoMessageBox.Show(
                this,
                "Stop Bulk Insert and discard all queued transactions?",
                "Bulk Insert",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

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

        if (CanDeleteActiveQueuedTransaction(
                _viewModel.IsBulkMode,
                _bulkQueueViewModel.SelectedQueuedTransaction,
                e.Key,
                Keyboard.Modifiers,
                Keyboard.FocusedElement))
        {
            ApplicationCommands.Delete.Execute(_bulkQueueViewModel.SelectedQueuedTransaction, this);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    internal static bool CanDeleteActiveQueuedTransaction(
        bool isBulkMode,
        TransactionVM? activeTransaction,
        Key key,
        ModifierKeys modifiers,
        IInputElement? focusedElement) =>
        isBulkMode && activeTransaction is not null && key == Key.Delete && modifiers == ModifierKeys.None &&
        focusedElement is not TextBoxBase and not PasswordBox and not ComboBox { IsEditable: true };

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
            if (!_viewModel.TryGetRepaymentCorrection(out var correctedAmount))
                continue;

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

        if (!await _viewModel.HasSimilarQueuedTransactionsAsync())
            return true;

        return FluxoMessageBox.Show(
            this,
            "Potentially duplicated transaction found. Would you like to save the current one?",
            "Add New Transaction",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
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
            var previousTagNames = _viewModel.Tags
                .Select(tag => tag.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _viewModel.RequestAddTag();
            await _viewModel.EnsureTagsLoadedAsync();

            var newTag = _viewModel.Tags
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

    private void OnBulkQueuePreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = source as ListBoxItem ?? DependencyObjectTree.FindAncestor<ListBoxItem>(source);
        if (item?.DataContext is not TransactionVM transaction)
            return;

        ApplicationCommands.Delete.Execute(transaction, this);
        e.Handled = true;
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

    internal static double CalculateTagScrollOffset(
        double startingOffset,
        double horizontalDelta,
        double scrollableWidth) =>
        Math.Clamp(startingOffset - horizontalDelta, 0d, scrollableWidth);

    internal void OnTagsScrollViewerPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FadingScrollViewer scrollViewer)
            return;

        _tagScrollStartPoint = e.GetPosition(scrollViewer);
        _tagScrollStartOffset = scrollViewer.HorizontalOffset;
        _tagPointerDownTag = GetTagFromSource((e.OriginalSource ?? e.Source) as DependencyObject);
        _wasSelectedTagPointerDown = ReferenceEquals(_tagPointerDownTag, _viewModel.SelectedTag);
        _isDraggingTags = false;
        scrollViewer.CaptureMouse();
    }

    private void OnTagsScrollViewerPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FadingScrollViewer { IsMouseCaptured: true } scrollViewer)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetTagPointerState(scrollViewer);
            return;
        }

        var horizontalDelta = e.GetPosition(scrollViewer).X - _tagScrollStartPoint.X;
        if (!_isDraggingTags && Math.Abs(horizontalDelta) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        _isDraggingTags = true;
        scrollViewer.ScrollToHorizontalOffset(CalculateTagScrollOffset(
            _tagScrollStartOffset,
            horizontalDelta,
            scrollViewer.ScrollableWidth));
        e.Handled = true;
    }

    internal void OnTagsScrollViewerPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FadingScrollViewer scrollViewer)
            return;

        var selectedTag = !_isDraggingTags ? _tagPointerDownTag : null;
        var shouldDeselect = selectedTag is not null && _wasSelectedTagPointerDown;
        ResetTagPointerState(scrollViewer);

        if (selectedTag is null)
            return;

        if (shouldDeselect)
        {
            TagsListBox.SelectedItem = null;
            _viewModel.SelectedTag = null;
            e.Handled = true;
            return;
        }

        TagsListBox.SelectedItem = selectedTag;
        _viewModel.SelectedTag = selectedTag;
        e.Handled = true;
    }

    internal static bool IsHorizontalTagScroll(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Shift) != 0;

    private void OnTagsScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not FadingScrollViewer scrollViewer || !IsHorizontalTagScroll(Keyboard.Modifiers))
            return;

        var wheelSteps = e.Delta / (double)Mouse.MouseWheelDeltaForOneLine;
        scrollViewer.ScrollToHorizontalOffset(Math.Clamp(
            scrollViewer.HorizontalOffset - wheelSteps * 48d,
            0d,
            scrollViewer.ScrollableWidth));
        e.Handled = true;
    }

    private void OnTagsScrollViewerLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is FadingScrollViewer scrollViewer)
            ResetTagPointerState(scrollViewer);
    }

    private static TagVM? GetTagFromSource(DependencyObject? source)
    {
        var item = source as ListBoxItem ?? DependencyObjectTree.FindAncestor<ListBoxItem>(source);
        return item?.DataContext as TagVM;
    }

    private void ResetTagPointerState(FadingScrollViewer scrollViewer)
    {
        if (scrollViewer.IsMouseCaptured)
            scrollViewer.ReleaseMouseCapture();

        _tagPointerDownTag = null;
        _wasSelectedTagPointerDown = false;
        _isDraggingTags = false;
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

}
