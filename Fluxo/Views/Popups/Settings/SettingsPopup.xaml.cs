using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Resources.Infrastructure;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;

namespace Fluxo.Views.Popups.Settings;

public partial class SettingsPopup : BasePopup, IRecipient<SettingsDialogRequestedMessage>,
    IRecipient<SettingsPopupCloseRequestedMessage>, IRecipient<SettingsPendingChangesChangedMessage>
{
    private static readonly Duration TabFadeDuration = new(TimeSpan.FromMilliseconds(150));

    private readonly IDialogService _dialogService;
    private readonly IMessenger _messenger;
    private readonly SettingsVM _viewModel;
    private bool _allowClose;
    private bool _isLoaded;
    private bool _isHandlingCloseRequest;
    private Task<SettingsOperationResult>? _configurationSaveTask;
    private bool _isSelectingTab;

    internal IMessenger Messenger => _messenger;

    public SettingsPopup(SettingsVM viewModel, IDialogService dialogService, IMessenger messenger)
    {
        InitializeComponent();

        _dialogService = dialogService;
        _messenger = messenger;
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoadedAsync;
        Closing += OnPopupClosing;
        Closed += OnPopupClosed;

        _messenger.RegisterAll(this);
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadAsync();
            _isLoaded = true;
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogError(exception, "Unable to load settings popup.");
            TryShowMessage(FluxoLogManager.CreateFailureMessage("load settings"), "Settings");
            _allowClose = true;
            Close();
        }
    }

    private async void OnPopupClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        if (_isHandlingCloseRequest)
            return;

        _isHandlingCloseRequest = true;

        try
        {
            if (_viewModel.HasPendingConfigurationChanges)
                await SaveConfigurationChangesAsync();
        }
        catch (Exception exception)
        {
            _viewModel.RevertConfigurationChanges();
            FluxoLogManager.LogError(exception, "Unable to finish saving settings while closing the popup.");
            TryShowMessage(FluxoLogManager.CreateFailureMessage("persist settings"), "Settings");
        }
        finally
        {
            _allowClose = true;
            _isHandlingCloseRequest = false;
            _ = Dispatcher.BeginInvoke(Close, DispatcherPriority.Background);
        }
    }

    private async void OnSettingsTabPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isSelectingTab ||
            sender is not RadioButton targetTab ||
            targetTab.IsChecked.GetValueOrDefault())
            return;

        e.Handled = true;
        if (!await CanLeaveCurrentSettingsTabAsync())
            return;

        _isSelectingTab = true;
        try
        {
            await CrossfadeSettingsTabAsync(targetTab);
        }
        finally
        {
            _isSelectingTab = false;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!IsSettingsTextInputFocused())
        {
            switch (e.Key)
            {
                case Key.Right when IsFocusWithin(SettingsSideMenu):
                    FocusActiveSettingsContent();
                    e.Handled = true;
                    return;

                case Key.Left when IsFocusWithin(SettingsContentHost):
                    FocusSettingsSideMenu();
                    e.Handled = true;
                    return;

                case Key.Up:
                    _ = NavigateSettingsTabAsync(-1);
                    e.Handled = true;
                    return;

                case Key.Down:
                    _ = NavigateSettingsTabAsync(1);
                    e.Handled = true;
                    return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private void FocusActiveSettingsContent()
    {
        var activeContent = GetCheckedTabButton() is { } currentTab
            ? GetContentForTab(currentTab)
            : null;

        if (activeContent is null)
            return;

        if (!FocusFirstFocusableDescendant(activeContent))
            SettingsContentHost.Focus();
    }

    private void FocusSettingsSideMenu()
    {
        var targetTab = GetCheckedTabButton() ?? AccountsTabButton;
        targetTab.Focus();
    }

    private static bool IsFocusWithin(DependencyObject container)
    {
        return Keyboard.FocusedElement is DependencyObject focusedElement &&
               DependencyObjectTree.IsDescendantOf(focusedElement, container);
    }

    private static bool FocusFirstFocusableDescendant(DependencyObject container)
    {
        if (container is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } element &&
            element.Focus())
        {
            return true;
        }

        foreach (var child in DependencyObjectTree.GetChildren(container))
            if (FocusFirstFocusableDescendant(child))
                return true;

        return false;
    }

    private async Task NavigateSettingsTabAsync(int offset)
    {
        if (_isSelectingTab)
            return;

        var tabs = GetOrderedTabButtons();
        var currentTab = GetCheckedTabButton();
        var currentIndex = Array.IndexOf(tabs, currentTab);
        if (currentIndex < 0)
            return;

        var targetIndex = (currentIndex + offset + tabs.Length) % tabs.Length;
        var targetTab = tabs[targetIndex];
        if (ReferenceEquals(targetTab, currentTab))
            return;

        if (!await CanLeaveCurrentSettingsTabAsync())
            return;

        _isSelectingTab = true;
        try
        {
            await CrossfadeSettingsTabAsync(targetTab);
        }
        finally
        {
            _isSelectingTab = false;
        }
    }

    private RadioButton[] GetOrderedTabButtons()
    {
        return
        [
            AccountsTabButton,
            BudgetTabButton,
            RecurringTransactionsTabButton,
            GoalsTabButton,
            IoUsTabButton,
            TagsTabButton,
            PreferencesTabButton,
            AboutTabButton
        ];
    }

    private async Task CrossfadeSettingsTabAsync(RadioButton targetTab)
    {
        var currentTab = GetCheckedTabButton();
        var currentContent = currentTab is null ? null : GetContentForTab(currentTab);
        var targetContent = GetContentForTab(targetTab);

        if (targetContent is null || ReferenceEquals(currentContent, targetContent))
        {
            targetTab.IsChecked = true;
            return;
        }

        targetContent.BeginAnimation(OpacityProperty, null);
        targetContent.Visibility = Visibility.Visible;
        targetContent.Opacity = 0;

        targetTab.IsChecked = true;

        if (currentContent is null)
        {
            await FadeElementAsync(targetContent, 0, 1);
            return;
        }

        currentContent.BeginAnimation(OpacityProperty, null);
        currentContent.Opacity = 1;

        await Task.WhenAll(
            FadeElementAsync(currentContent, 1, 0),
            FadeElementAsync(targetContent, 0, 1));

        currentContent.Visibility = Visibility.Collapsed;
        currentContent.Opacity = 0;
        targetContent.Opacity = 1;
    }

    private RadioButton? GetCheckedTabButton()
    {
        if (AccountsTabButton.IsChecked.GetValueOrDefault())
            return AccountsTabButton;

        if (BudgetTabButton.IsChecked.GetValueOrDefault())
            return BudgetTabButton;

        if (RecurringTransactionsTabButton.IsChecked.GetValueOrDefault())
            return RecurringTransactionsTabButton;

        if (GoalsTabButton.IsChecked.GetValueOrDefault())
            return GoalsTabButton;

        if (TagsTabButton.IsChecked.GetValueOrDefault())
            return TagsTabButton;

        if (IoUsTabButton.IsChecked.GetValueOrDefault())
            return IoUsTabButton;

        if (PreferencesTabButton.IsChecked.GetValueOrDefault())
            return PreferencesTabButton;

        if (AboutTabButton.IsChecked.GetValueOrDefault())
            return AboutTabButton;

        return null;
    }

    private FrameworkElement? GetContentForTab(RadioButton tabButton)
    {
        if (ReferenceEquals(tabButton, AccountsTabButton))
            return AccountsTabContent;

        if (ReferenceEquals(tabButton, BudgetTabButton))
            return BudgetTabContent;

        if (ReferenceEquals(tabButton, RecurringTransactionsTabButton))
            return RecurringTransactionsTabContent;

        if (ReferenceEquals(tabButton, GoalsTabButton))
            return GoalsTabContent;

        if (ReferenceEquals(tabButton, IoUsTabButton))
            return IoUsTabContent;

        if (ReferenceEquals(tabButton, TagsTabButton))
            return TagsTabContent;

        if (ReferenceEquals(tabButton, PreferencesTabButton))
            return PersonalizationTabContent;

        if (ReferenceEquals(tabButton, AboutTabButton))
            return AboutTabContent;

        return null;
    }

    private static Task FadeElementAsync(UIElement element, double from, double to)
    {
        var tcs = new TaskCompletionSource<bool>();

        var animation = new DoubleAnimation(from, to, TabFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = to < from ? EasingMode.EaseIn : EasingMode.EaseOut }
        };

        animation.Completed += (_, _) => tcs.TrySetResult(true);
        element.BeginAnimation(OpacityProperty, animation);

        return tcs.Task;
    }

    private async Task<bool> CanLeaveCurrentSettingsTabAsync()
    {
        if (PreferencesTabButton.IsChecked.GetValueOrDefault() &&
            _viewModel.HasPendingPersonalizationConfigurationChanges)
        {
            var result = await SaveConfigurationChangesAsync();
            return result.IsSuccess || !_viewModel.HasPendingPersonalizationConfigurationChanges;
        }

        if (!BudgetTabButton.IsChecked.GetValueOrDefault() ||
            !_viewModel.HasPendingBudgetConfigurationChanges)
        {
            return true;
        }

        if (_viewModel.CanSaveBudgetConfiguration)
        {
            var result = await SaveConfigurationChangesAsync();
            return result.IsSuccess || !_viewModel.HasPendingBudgetConfigurationChanges;
        }

        var message = string.IsNullOrWhiteSpace(_viewModel.BudgetConfigurationErrorMessage)
            ? "Budget Allocation is not valid."
            : _viewModel.BudgetConfigurationErrorMessage;

        if (FluxoMessageBox.Show(this, $"{message}\n\nDo you want to adjust it?", "Budget Allocation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            return false;
        }

        _viewModel.DiscardBudgetConfigurationChanges();
        return true;
    }

    public void Receive(SettingsDialogRequestedMessage message)
    {
        var request = message.Value;
        switch (request.RequestType)
        {
            case SettingsDialogRequestType.AddAccount when request.Payload is null:
                _dialogService.ShowAddAccount(_viewModel.CreateAddAccountViewModel(), this);
                break;

            case SettingsDialogRequestType.AddAccount when request.Payload is AddAccountVM addAccount:
                _dialogService.ShowAddAccount(addAccount, this);
                break;

            case SettingsDialogRequestType.AddRecurringTransaction when request.Payload is TransactionPopupRequest popupRequest:
                _dialogService.ShowAddNewTransaction(popupRequest, this);
                break;

            case SettingsDialogRequestType.AddSavingGoal when request.Payload is AddSavingGoalVM addSavingGoal:
                _dialogService.ShowAddSavingGoal(addSavingGoal, this);
                break;

            case SettingsDialogRequestType.AccountDetail
                when request.Payload is AccountDetailVM accountDetail:
                _dialogService.ShowAccountDetail(accountDetail, this);
                break;

            case SettingsDialogRequestType.AddTag when request.Payload is SettingsTagDialogRequest tagDialogRequest:
                _dialogService.ShowAddTag(tagDialogRequest.ViewModel, tagDialogRequest.SaveTagAsync, this);
                break;

            case SettingsDialogRequestType.AddTag when request.Payload is SettingsTagsTabVM tagsTab:
                _dialogService.ShowAddTag(tagsTab, this);
                break;

            case SettingsDialogRequestType.FeaturePlaceholder:
                _dialogService.ShowFeaturePlaceholder(request.Title ?? "Settings",
                    request.Message ?? "This flow is still being built.", this);
                break;
        }
    }

    public void Receive(SettingsPopupCloseRequestedMessage message)
    {
        if (message.Value.AllowClose)
            _allowClose = true;

        Close();
    }

    public void Receive(SettingsPendingChangesChangedMessage message)
    {
        if (!_isLoaded || !message.Value.HasPendingChanges)
            return;

        if (message.Value.TabKey == SettingsTabKey.Budget)
        {
            if (!_viewModel.BudgetTab.IsConfigurationPageSelected &&
                _viewModel.BudgetTab.HasPendingConfigurationChanges)
            {
                _ = SaveConfigurationChangesAsync();
            }

            return;
        }

        if (message.Value.TabKey != SettingsTabKey.Personalization ||
            _viewModel.PersonalizationTab.IsNotificationPageSelected ||
            IsPersonalizationTextInputFocused())
        {
            return;
        }

        RequestPersonalizationAutosave();
    }

    internal void RequestPersonalizationAutosave()
    {
        if (!_isLoaded || !_viewModel.HasPendingPersonalizationConfigurationChanges)
            return;

        _ = SaveConfigurationChangesAsync();
    }

    internal void ShowDataManagement()
    {
        _dialogService.ShowDataManagement(this);
    }

    internal Task ShowToastWhileAsync(string message, Func<Task> work)
    {
        return _dialogService.ShowToastWhileAsync(message, work, this);
    }

    internal Task<T> ShowToastWhileAsync<T>(string message, Func<global::Fluxo.Views.Popups.ToastPopup, Task<T>> work)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Toast message cannot be empty.", nameof(message));

        ArgumentNullException.ThrowIfNull(work);

        T? result = default;
        global::Fluxo.Views.Popups.ToastPopup? popup = null;
        popup = new global::Fluxo.Views.Popups.ToastPopup(message, async () =>
        {
            result = await work(popup!).ConfigureAwait(true);
        })
        {
            Owner = this
        };

        popup.ShowDialog();

        if (popup.ExecutionException is not null)
            ExceptionDispatchInfo.Capture(popup.ExecutionException).Throw();

        return Task.FromResult(result!);
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        _messenger.UnregisterAll(this);
    }

    private Task<SettingsOperationResult> SaveConfigurationChangesAsync()
    {
        if (_configurationSaveTask is not null)
            return _configurationSaveTask;

        var saveTask = SaveConfigurationChangesCoreAsync();
        _configurationSaveTask = saveTask;
        return ClearConfigurationSaveTaskAsync(saveTask);
    }

    private async Task<SettingsOperationResult> SaveConfigurationChangesCoreAsync()
    {
        SettingsOperationResult result;
        do
        {
            var notification = ResolveSaveNotification();
            result = await _viewModel.SaveConfigurationChangesAsync();
            if (!result.IsSuccess)
            {
                TryShowMessage(result.ErrorMessage, "Settings");
                return result;
            }

            try
            {
                FloatingNotificationPublisher.Success(
                    _messenger,
                    notification.Header,
                    notification.Message,
                    headerAction: notification.Action);
            }
            catch (Exception exception)
            {
                FluxoLogManager.LogWarning(
                    exception,
                    "Settings were saved, but the success notification could not be shown.");
            }
        } while (_viewModel.HasPendingConfigurationChanges);

        return result;
    }

    private async Task<SettingsOperationResult> ClearConfigurationSaveTaskAsync(
        Task<SettingsOperationResult> saveTask)
    {
        try
        {
            return await saveTask;
        }
        finally
        {
            if (ReferenceEquals(_configurationSaveTask, saveTask))
                _configurationSaveTask = null;
        }
    }

    private (string Header, string Action, string Message) ResolveSaveNotification()
    {
        if (_viewModel.BudgetTab.HasPendingConfigurationChanges)
            return ("Budget configuration", "Updated", "Budget configuration was saved.");
        if (_viewModel.BudgetTab.HasPendingAllocationChanges)
            return ("Budget allocation", "Updated", "Budget allocation settings were saved.");
        if (_viewModel.PersonalizationTab.HasPendingPasswordChange)
            return ("Password", "Updated", "Unlock password was saved.");
        if (_viewModel.PersonalizationTab.HasPendingAutoLockEnabledChange)
            return _viewModel.PersonalizationTab.IsAppAutoLocked
                ? ("Auto-lock", "Enabled", "The UI will lock after inactivity.")
                : ("Auto-lock", "Disabled", "The UI will no longer lock after inactivity.");
        if (_viewModel.PersonalizationTab.HasPendingAutoLockIntervalChange)
            return ("Auto-lock", "wait time changed",
                $"The UI will lock after {_viewModel.PersonalizationTab.AppAutoLockedInterval} seconds of inactivity.");
        if (_viewModel.PersonalizationTab.HasPendingNotificationChanges)
            return ("Notifications", "Updated", "Notification preferences were saved.");
        return ("Preferences", "Updated", "Personalization preferences were saved.");
    }

    private bool IsPersonalizationTextInputFocused()
    {
        return Keyboard.FocusedElement is FrameworkElement element &&
               element is TextBox or PasswordBox &&
               ReferenceEquals(element.DataContext, _viewModel.PersonalizationTab);
    }

    private static bool IsSettingsTextInputFocused()
    {
        return Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;
    }

    private void TryShowMessage(string? message, string title)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            _dialogService.ShowInformation(message, title, this);
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogWarning(exception, "Unable to show a settings message.");
        }
    }
}
