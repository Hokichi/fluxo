using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Fluxo.Resources.Infrastructure;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Views.Shell;

namespace Fluxo.Views.Popups;

public partial class TransactionSplitPopup : BasePopup
{
    private enum MoreTagsPopupLifecycleState
    {
        Closed,
        Opening,
        Open,
        Closing
    }

    private readonly IDialogService _dialogService;
    private readonly SettingsTagsTabVM _settingsTagsTabViewModel;
    private readonly TransactionSplitVM _viewModel;
    private bool _allowClose;
    private bool _isHandlingAddTagSelection;
    private bool _isHandlingCloseRequest;
    private readonly DispatcherTimer _moreTagsHoverCloseTimer;
    private MoreTagsPopupLifecycleState _moreTagsPopupState = MoreTagsPopupLifecycleState.Closed;
    private bool _isSyncingNoteDocument;
    private readonly PropertyChangedEventHandler _viewModelPropertyChangedHandler;

    public TransactionSplitPopup(
        TransactionSplitVM viewModel,
        IDialogService dialogService,
        SettingsTagsTabVM settingsTagsTabViewModel)
    {
        InitializeComponent();

        _dialogService = dialogService;
        _settingsTagsTabViewModel = settingsTagsTabViewModel;
        _viewModel = viewModel;
        DataContext = viewModel;
        _moreTagsHoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _moreTagsHoverCloseTimer.Tick += (_, _) =>
        {
            _moreTagsHoverCloseTimer.Stop();
            TryCloseMoreTagsPopupIfNotPinned();
        };

        _viewModelPropertyChangedHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(TransactionSplitVM.IsEditing) or nameof(TransactionSplitVM.IsSplitMode))
            {
                UpdateButtonStates();
                RecalculateTagLayout();
                SyncMoreTagsPopupState();
            }
        };
        _viewModel.PropertyChanged += _viewModelPropertyChangedHandler;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadChildTransactionsAsync();
            SyncNoteDocumentFromViewModel();
            UpdateButtonStates();
            RecalculateTagLayout();
            SyncMoreTagsPopupState();
        };
        Closing += OnPopupClosing;
        Closed += OnPopupClosed;

        TagsDockPanel.SizeChanged += (_, _) => RecalculateTagLayout();
        PreviewMouseDown += OnPopupPreviewMouseDown;
    }

    protected override async void OnEditButtonClick()
    {
        await _viewModel.BeginEditingAsync();
        RecalculateTagLayout();
        SyncMoreTagsPopupState();
        TransactionNameTextBox.Focus();
    }

    protected override async void OnSaveButtonClick()
    {
        var result = await TrySaveWithSplitRemainderConfirmationAsync();
        if (result is null)
            return;

        if (!result.Value.IsSuccess)
        {
            ShowValidationMessage(result.Value.ErrorMessage);
            return;
        }

        _allowClose = true;
        Close();
    }

    protected override void OnCloneButtonClick()
    {
        CloseForPopupHandoff();
        _viewModel.RequestClone();
    }

    protected override async void OnDeleteButtonClick()
    {
        if (_viewModel.IsEditing || _viewModel.IsSplitMode)
            return;

        var confirmation = FluxoMessageBox.Show(
            this,
            _viewModel.DeleteConfirmationMessage,
            "Transaction Split",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        var result = await _viewModel.DeleteAsync();
        if (!result.IsSuccess)
        {
            ShowValidationMessage(result.ErrorMessage);
            return;
        }

        _allowClose = true;
        Close();
    }

    protected override async void OnSplitButtonClick()
    {
        await _viewModel.BeginSplitModeAsync();
        SplitTransactionAmountTextBox.Focus();

        UpdateButtonStates();
        SyncMoreTagsPopupState();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (CanClone)
            {
                OnCloneButtonClick();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Enter && NoteRichTextBox.IsKeyboardFocusWithin)
            return;

        base.OnPreviewKeyDown(e);
    }

    private async void OnPopupClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _isHandlingCloseRequest || !_viewModel.HasPendingSplitChanges)
            return;

        e.Cancel = true;
        _isHandlingCloseRequest = true;

        try
        {
            var confirmation = FluxoMessageBox.Show(
                this,
                "Discard all changes and return?",
                "Transaction Split",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation == MessageBoxResult.Yes)
            {
                _allowClose = true;
                _ = Dispatcher.BeginInvoke(new Action(Close));
                return;
            }

            var result = await TrySaveWithSplitRemainderConfirmationAsync();
            if (result is null)
                return;

            if (!result.Value.IsSuccess)
            {
                ShowValidationMessage(result.Value.ErrorMessage);
                return;
            }

            _allowClose = true;
            _ = Dispatcher.BeginInvoke(new System.Action(Close));
        }
        finally
        {
            _isHandlingCloseRequest = false;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= _viewModelPropertyChangedHandler;
        _viewModel.RequestParentRefresh();
    }

    private void OnNoteTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingNoteDocument)
            return;

        _viewModel.NoteText = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd)
            .Text
            .Trim();
    }

    private void UpdateButtonStates()
    {
        var isEditing = _viewModel.IsEditing || _viewModel.IsSplitMode;
        Mode = isEditing ? PopupMode.SaveDiscard : PopupMode.Functional;
        CanContinue = false;
        CanDiscard = false;
        CanEdit = !isEditing;
        CanDelete = !isEditing;
        CanClone = !isEditing;
        CanSplit = _viewModel.ShowSplitButton && !isEditing;
    }

    private void ShowValidationMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        FloatingNotificationPublisher.SaveFailed("Transaction changes not saved", [message]);
    }

    private void SyncNoteDocumentFromViewModel()
    {
        _isSyncingNoteDocument = true;

        try
        {
            var noteText = _viewModel.NoteText ?? string.Empty;
            new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd).Text = noteText;
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

            _dialogService.ShowAddTag(_settingsTagsTabViewModel, this);
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

    private void RecalculateTagLayout()
    {
        if (!IsLoaded || !_viewModel.IsEditing)
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
        return IsLoaded && _viewModel.IsEditing && _viewModel.HasMoreTags;
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
        var tagChip = new ToggleButton
        {
            Content = tag.Name,
            DataContext = tag,
            Style = (Style)FindResource("PopupTagItemToggleStyle")
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

    private void OnAddSplitRowClick(object sender, RoutedEventArgs e)
    {
        _viewModel.AddSplitRow();
    }

    private void OnRemoveSplitRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionSplitRowVM row)
            _viewModel.RemoveSplitRow(row);
    }

    private void OnAddNestedSplitRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionSplitRowVM row)
            _viewModel.AddNestedSplitRow(row);
    }

    private void OnRemoveNestedSplitRowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransactionSplitRowVM row)
            _viewModel.RemoveNestedSplitRow(row);
    }

    private async Task<TransactionSplitVM.TransactionSplitSaveResult?> TrySaveWithSplitRemainderConfirmationAsync()
    {
        var result = await _viewModel.SaveAsync();
        if (!result.RequiresConfirmation)
            return result;

        var saveAnyway = _dialogService.ShowWarning(
            result.ErrorMessage ?? "This expense exceeds the destination account's maximum spending limit. Save anyway?",
            "Transaction Split",
            this,
            MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        if (!saveAnyway)
            return null;

        return await _viewModel.SaveAsync(allowMaximumSpendingOverflow: true);
    }

}
