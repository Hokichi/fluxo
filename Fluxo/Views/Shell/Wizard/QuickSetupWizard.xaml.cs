using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Fluxo.Core.Enums;
using Fluxo.Resources.Infrastructure;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Logging;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Views.Popups;
using Microsoft.Extensions.DependencyInjection;
using QuickSetupWizardLoadingOutcome = Fluxo.Data.Enums.QuickSetupWizardLoadingOutcome;
using QuickSetupWizardVM = Fluxo.ViewModels.Shell.QuickSetupWizard.QuickSetupWizardVM;

namespace Fluxo.Views.Shell.Wizard;

public partial class QuickSetupWizard : BasePopup
{
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150));

    private readonly IDialogService _dialogService;
    private readonly QuickSetupWizardVM _viewModel;
    private readonly TransactionPopupAddTagHost _addTagHost;
    private bool _allowClose;
    private bool _isAnimating;
    private bool _isHandlingClose;
    private readonly DispatcherTimer _allocationHoldDelayTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _allocationRepeatTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private int _heldAllocationDelta;
    private BudgetAllocationSegment _heldAllocationSegment;

    public QuickSetupWizard(QuickSetupWizardVM viewModel, IDialogService dialogService, IServiceProvider serviceProvider, IMessenger messenger)
    {
        InitializeComponent();
        _dialogService = dialogService;
        _viewModel = viewModel;
        _addTagHost = new TransactionPopupAddTagHost(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(), dialogService, messenger,
            ownerToken => Application.Current?.Windows.OfType<TransactionPopup>()
                .FirstOrDefault(popup => popup.IsOwnedBy(ownerToken)));
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
        Closing += OnClosingAsync;
        Closed += (_, _) =>
        {
            _addTagHost.Dispose();
            _viewModel.MiddlePage.RecurringTransactions.Dispose();
        };
        PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        _allocationHoldDelayTimer.Tick += OnAllocationHoldDelayTick;
        _allocationRepeatTimer.Tick += OnAllocationRepeatTick;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to load startup wizard.");
            FluxoMessageBox.Show(this, FluxoLogManager.CreateFailureMessage("load startup wizard"),
                "Startup Wizard", MessageBoxButton.OK, MessageBoxImage.Information);
            _allowClose = true;
            Close();
        }
    }

    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        if (_isHandlingClose)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _isHandlingClose = true;

        try
        {
            var confirmation = FluxoMessageBox.Show(this,
                "Setup isn't finished yet. fluxo will continue to the app after this dialog.",
                "Startup Wizard",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;

            var result = await _viewModel.DismissAsync();
            if (!result.IsSuccess)
            {
                FluxoMessageBox.Show(this, result.ErrorMessage ?? "Unable to close the startup wizard.",
                    "Startup Wizard", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _allowClose = true;
            _ = Dispatcher.BeginInvoke(new System.Action(Close));
        }
        finally
        {
            _isHandlingClose = false;
        }
    }

    public async void OnBackClick(object sender, RoutedEventArgs e)
    {
        await AnimateStepTransitionAsync(_viewModel.GoBack);
    }

    public async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsStep2Active && !_viewModel.HasAccounts)
        {
            var dialogResult = FluxoMessageBox.Show(this,
                "A account is required to calculate budgets and linked transactions. If there are no available sources, Recurring transactions, Saving goals, and Budget allocation setup will be skipped. Do you want to continue without adding a source?",
                "Startup Wizard",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (dialogResult == MessageBoxResult.Yes)
            {
                await AnimateStepTransitionAsync(() =>
                {
                    _viewModel.NavigateToStep(6);
                });
                return;
            }
            else
            {
                OnAddAccountClick(sender, e);
                return;
            }
        }

        var result = await AnimateStepTransitionAsync(_viewModel.GoNextAsync);
        if (!result.IsSuccess && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            FluxoMessageBox.Show(this, result.ErrorMessage, "Startup Wizard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_viewModel.IsLoadingStep)
            await RunLoadingStepAsync();
    }

    private async Task RunLoadingStepAsync()
    {
        try
        {
            var outcome = await _viewModel.ExecuteLoadingFlowAsync(
                tryStageAsyncOverride: null,
                confirmRetryCycleAsync: ConfirmRetryLoadingCycleAsync);
            if (outcome is QuickSetupWizardLoadingOutcome.Success or QuickSetupWizardLoadingOutcome.Abandoned)
                await AnimateStepTransitionAsync(_viewModel.GoNextAsync);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to process startup wizard next-step action.");
            FluxoMessageBox.Show(this, FluxoLogManager.CreateFailureMessage("load startup wizard data"),
                "Startup Wizard", MessageBoxButton.OK, MessageBoxImage.Information);
            _allowClose = true;
            Close();
            return;
        }
    }

    private Task<bool> ConfirmRetryLoadingCycleAsync()
    {
        var result = FluxoMessageBox.Show(this,
            "Unable to load data after several attempts. Do you want to try again?",
            "Startup Wizard",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    public async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        var result = await _viewModel.CompleteAsync();
        if (!result.IsSuccess)
        {
            FluxoMessageBox.Show(this, result.ErrorMessage ?? "Unable to finish setup.", "Startup Wizard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _allowClose = true;
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (!_viewModel.IsFinalStep)
            {
                OnNextClick(this, new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    public async void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        _dialogService.ShowAddAccount(_viewModel.MiddlePage.Accounts.CreateAddViewModel(), this);
        await _viewModel.MiddlePage.Accounts.RefreshAsync();
    }

    public async void OnAddRecurringTransactionClick(object sender, RoutedEventArgs e)
    {
        _dialogService.ShowAddNewTransaction(_viewModel.MiddlePage.RecurringTransactions.CreateAddViewModel(), this);
        await _viewModel.MiddlePage.RecurringTransactions.RefreshAsync();
    }

    public async void OnAddSavingGoalClick(object sender, RoutedEventArgs e)
    {
        _dialogService.ShowAddSavingGoal(_viewModel.MiddlePage.SavingGoals.CreateAddViewModel(), this);
        await _viewModel.MiddlePage.SavingGoals.RefreshAsync();
    }

    public async void OnEditAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        var vm = await _viewModel.MiddlePage.Accounts.CreateEditViewModelAsync(id);
        _dialogService.ShowAddAccount(vm, this);
        await _viewModel.MiddlePage.Accounts.RefreshAsync();
    }

    public async void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        var confirmationMessage = _viewModel.MiddlePage.Accounts.BuildDeleteConfirmationMessage(id);
        var result = FluxoMessageBox.Show(this,
            confirmationMessage,
            "Delete Account", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            await _viewModel.MiddlePage.Accounts.DeleteAsync(id);
    }

    public async void OnEditRecurringTransactionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        var vm = await _viewModel.MiddlePage.RecurringTransactions.CreateEditViewModelAsync(id);
        _dialogService.ShowAddNewTransaction(vm, this);
        await _viewModel.MiddlePage.RecurringTransactions.RefreshAsync();
    }

    public async void OnDeleteRecurringTransactionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        var result = FluxoMessageBox.Show(this,
            "Are you sure you want to delete this recurring transaction?",
            "Delete Recurring Transaction", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            await _viewModel.MiddlePage.RecurringTransactions.DeleteAsync(id);
    }

    public async void OnEditSavingGoalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        var vm = await _viewModel.MiddlePage.SavingGoals.CreateEditViewModelAsync(id);
        _dialogService.ShowAddSavingGoal(vm, this);
        await _viewModel.MiddlePage.SavingGoals.RefreshAsync();
    }

    public async void OnDeleteSavingGoalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int id })
            return;

        var result = FluxoMessageBox.Show(this,
            "Are you sure you want to delete this saving goal?",
            "Delete Saving Goal", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            await _viewModel.MiddlePage.SavingGoals.DeleteAsync(id);
    }

    public void OnAllocationAdjustButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !TryParseAllocationTag(tag, out var segment, out var delta))
            return;
        _viewModel.MiddlePage.BudgetAllocation.IncrementAllocation(segment, delta);
    }

    public void OnAllocationAdjustButtonMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !TryParseAllocationTag(tag, out var segment, out var delta))
            return;
        _heldAllocationSegment = segment;
        _heldAllocationDelta = delta;
        _allocationRepeatTimer.Stop();
        _allocationHoldDelayTimer.Stop();
        _allocationHoldDelayTimer.Start();
    }

    public void OnAllocationAdjustButtonMouseUp(object sender, MouseButtonEventArgs e)
    {
        StopAllocationTimers();
    }

    public void OnAllocationAdjustButtonMouseLeave(object sender, MouseEventArgs e)
    {
        StopAllocationTimers();
    }

    private void OnAllocationHoldDelayTick(object? sender, EventArgs e)
    {
        _allocationHoldDelayTimer.Stop();
        _allocationRepeatTimer.Start();
    }

    private void OnAllocationRepeatTick(object? sender, EventArgs e)
    {
        _viewModel.MiddlePage.BudgetAllocation.IncrementAllocation(_heldAllocationSegment, _heldAllocationDelta);
    }

    private void StopAllocationTimers()
    {
        _allocationHoldDelayTimer.Stop();
        _allocationRepeatTimer.Stop();
    }

    private static bool TryParseAllocationTag(string tag, out BudgetAllocationSegment segment, out int delta)
    {
        segment = default;
        delta = 0;
        var parts = tag.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Enum.TryParse(parts[0], out segment))
            return false;
        delta = string.Equals(parts[1], "+1", StringComparison.Ordinal) ? 1 : -1;
        return true;
    }

    private Border? GetStripeForStep(int stepIndex) => MiddleStepPage?.GetStripeForStep(stepIndex);

    private bool IsMiddleStep(int stepIndex) => stepIndex >= 2 && stepIndex <= 8;

    private static bool IsLoadingFinalTransition(int fromStep, int toStep) =>
        (fromStep == 9 && toStep == 10) || (fromStep == 10 && toStep == 9);

    private UIElement? GetContentElementForStep(int stepIndex) => stepIndex switch
    {
        9 => LoadingStepPage?.ContentElement,
        10 => FinalStepPage?.ContentElement,
        _ => null
    };

    private static bool ShouldSkipHeaderDrag(DependencyObject? originalSource)
    {
        var current = originalSource;
        while (current is not null)
        {
            if (current is ButtonBase or Thumb or TextBoxBase)
                return true;

            current = DependencyObjectTree.GetParent(current);
        }

        return false;
    }

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1)
            return;

        if (e.GetPosition(this).Y > 60)
            return;

        if (ShouldSkipHeaderDrag(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch
        {
            // Ignore drag failures caused by transient mouse capture changes.
        }
    }

    private async Task AnimateStepTransitionAsync(Action changeStep)
    {
        if (_isAnimating)
            return;

        _isAnimating = true;

        try
        {
            var fromStep = _viewModel.CurrentStepIndex;
            var toStep = fromStep - 1;

            if (IsLoadingFinalTransition(fromStep, toStep))
            {
                var fromContent = GetContentElementForStep(fromStep);
                if (fromContent is not null)
                    await FadeElementAsync(fromContent, 1, 0);

                changeStep();
                SyncStripeOpacities();
                ScrollCurrentStepToTop();

                var toContent = GetContentElementForStep(_viewModel.CurrentStepIndex);
                if (toContent is not null)
                {
                    toContent.Opacity = 0;
                    await FadeElementAsync(toContent, 0, 1);
                }

                return;
            }

            if (IsCrossSectionTransition(fromStep, isForward: false))
            {
                await FadeElementAsync(ContentContainer, 1, 0);
                changeStep();
                SyncStripeOpacities();
                ScrollCurrentStepToTop();
                await FadeElementAsync(ContentContainer, 0, 1);
            }
            else
            {
                var middleContent = MiddleStepPage?.StepContentElement;
                if (middleContent is not null)
                    await FadeElementAsync(middleContent, 1, 0);

                changeStep();
                SyncStripeOpacities();
                ScrollCurrentStepToTop();

                if (middleContent is not null)
                    await FadeElementAsync(middleContent, 0, 1);
            }
        }
        finally
        {
            _isAnimating = false;
        }
    }

    private async Task<SettingsOperationResult> AnimateStepTransitionAsync(
        Func<Task<SettingsOperationResult>> changeStepAsync)
    {
        if (_isAnimating)
            return SettingsOperationResult.Success();

        _isAnimating = true;

        try
        {
            var fromStep = _viewModel.CurrentStepIndex;
            var toStep = fromStep + 1;

            if (IsLoadingFinalTransition(fromStep, toStep))
            {
                var fromContent = GetContentElementForStep(fromStep);
                if (fromContent is not null)
                    await FadeElementAsync(fromContent, 1, 0);

                var result = await changeStepAsync();
                SyncStripeOpacities();
                ScrollCurrentStepToTop();

                var toContent = GetContentElementForStep(_viewModel.CurrentStepIndex);
                if (toContent is not null)
                {
                    toContent.Opacity = 0;
                    await FadeElementAsync(toContent, 0, 1);
                }

                return result;
            }

            if (IsCrossSectionTransition(fromStep, isForward: true))
            {
                await FadeElementAsync(ContentContainer, 1, 0);
                var result = await changeStepAsync();
                SyncStripeOpacities();
                ScrollCurrentStepToTop();
                await FadeElementAsync(ContentContainer, 0, 1);
                return result;
            }
            else
            {
                var middleContent = MiddleStepPage?.StepContentElement;
                if (middleContent is not null)
                    await FadeElementAsync(middleContent, 1, 0);

                var result = await changeStepAsync();
                SyncStripeOpacities();
                ScrollCurrentStepToTop();

                if (middleContent is not null)
                    await FadeElementAsync(middleContent, 0, 1);

                return result;
            }
        }
        finally
        {
            _isAnimating = false;
        }
    }

    private bool IsCrossSectionTransition(int fromStep, bool isForward)
    {
        var toStep = isForward ? fromStep + 1 : fromStep - 1;
        return !(IsMiddleStep(fromStep) && IsMiddleStep(toStep));
    }

    private void SyncStripeOpacities()
    {
        for (var i = 2; i <= 8; i++)
        {
            var stripe = GetStripeForStep(i);
            if (stripe is null) continue;
            stripe.BeginAnimation(OpacityProperty, null);
            stripe.Opacity = i == _viewModel.CurrentStepIndex ? 1 : 0;
        }
    }

    private void ScrollCurrentStepToTop()
    {
        MiddleStepPage?.ScrollCurrentStepToTop();
    }

    private Task FadeElementAsync(UIElement element, double from, double to)
    {
        var tcs = new TaskCompletionSource();
        var animation = new DoubleAnimation(from, to, FadeDuration)
        {
            EasingFunction = new QuadraticEase
            {
                EasingMode = to < from ? EasingMode.EaseIn : EasingMode.EaseOut
            }
        };
        animation.Completed += (_, _) => tcs.SetResult();
        element.BeginAnimation(OpacityProperty, animation);
        return tcs.Task;
    }
}
