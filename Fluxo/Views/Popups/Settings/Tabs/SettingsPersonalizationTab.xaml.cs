using System.ComponentModel;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Helpers.Settings;
using Fluxo.Views.Popups.Settings;

namespace Fluxo.Views.Popups.Settings.Tabs;

public partial class SettingsPersonalizationTab : UserControl
{
    private readonly DispatcherTimer _usernameAutosaveTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _passwordAutosaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _isLoaded;
    private bool _isSyncingUiLockPassword;
    private SettingsPersonalizationTabVM? _trackedViewModel;

    public SettingsPersonalizationTab()
    {
        InitializeComponent();

        _usernameAutosaveTimer.Tick += OnUsernameAutosaveTimerTick;
        _passwordAutosaveTimer.Tick += OnPasswordAutosaveTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        TrackViewModel(DataContext as SettingsPersonalizationTabVM);
        SyncUiLockPasswordFieldsFromViewModel();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _usernameAutosaveTimer.Stop();
        _passwordAutosaveTimer.Stop();
        TrackViewModel(null);
    }

    private void OnUsernameTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isLoaded || !UsernameTextBox.IsKeyboardFocusWithin)
            return;

        _usernameAutosaveTimer.Stop();
        _usernameAutosaveTimer.Start();
    }

    private void OnUsernameTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        _usernameAutosaveTimer.Stop();
        RequestPersonalizationAutosave();
    }

    private void OnUsernameAutosaveTimerTick(object? sender, EventArgs e)
    {
        _usernameAutosaveTimer.Stop();
        RequestPersonalizationAutosave();
    }

    private void RequestPersonalizationAutosave()
    {
        if (Window.GetWindow(this) is SettingsPopup settingsPopup)
            settingsPopup.RequestPersonalizationAutosave();
    }

    private void OnDataManagementClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is SettingsPopup settingsPopup)
            settingsPopup.ShowDataManagement();
    }

    private async void OnRunSetupWizardClick(object sender, RoutedEventArgs e)
    {
        await SettingsSetupWizardFlow.RunAsync(
            Window.GetWindow(this),
            DataContext as Fluxo.ViewModels.Popups.Settings.SettingsPersonalizationTabVM);
    }

    private void OnUiLockingPasswordBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingUiLockPassword || DataContext is not SettingsPersonalizationTabVM viewModel)
            return;

        _isSyncingUiLockPassword = true;
        viewModel.UiLockingPassword = UiLockingPasswordBox.Password;
        UiLockingPasswordVisibleTextBox.Text = UiLockingPasswordBox.Password;
        _isSyncingUiLockPassword = false;
        RestartPasswordAutosaveTimer();
    }

    private void OnUiLockingPasswordVisibleTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isSyncingUiLockPassword || DataContext is not SettingsPersonalizationTabVM viewModel)
            return;

        _isSyncingUiLockPassword = true;
        viewModel.UiLockingPassword = UiLockingPasswordVisibleTextBox.Text;
        UiLockingPasswordBox.Password = UiLockingPasswordVisibleTextBox.Text;
        _isSyncingUiLockPassword = false;
        RestartPasswordAutosaveTimer();
    }

    private void RestartPasswordAutosaveTimer()
    {
        if (!_isLoaded)
            return;

        _passwordAutosaveTimer.Stop();
        _passwordAutosaveTimer.Start();
    }

    private void OnPasswordAutosaveTimerTick(object? sender, EventArgs e)
    {
        _passwordAutosaveTimer.Stop();
        RequestPersonalizationAutosave();
    }

    private void OnUiLockingPasswordLostFocus(object sender, RoutedEventArgs e)
    {
        _passwordAutosaveTimer.Stop();
        RequestPersonalizationAutosave();
    }

    private void TrackViewModel(SettingsPersonalizationTabVM? viewModel)
    {
        if (ReferenceEquals(_trackedViewModel, viewModel))
            return;

        if (_trackedViewModel is not null)
            _trackedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _trackedViewModel = viewModel;

        if (_trackedViewModel is not null)
            _trackedViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPersonalizationTabVM.UiLockingPassword))
            SyncUiLockPasswordFieldsFromViewModel();
    }

    private void SyncUiLockPasswordFieldsFromViewModel()
    {
        if (DataContext is not SettingsPersonalizationTabVM viewModel)
            return;

        var password = viewModel.UiLockingPassword ?? string.Empty;
        if (string.Equals(UiLockingPasswordBox.Password, password, StringComparison.Ordinal) &&
            string.Equals(UiLockingPasswordVisibleTextBox.Text, password, StringComparison.Ordinal))
        {
            return;
        }

        _isSyncingUiLockPassword = true;
        UiLockingPasswordBox.Password = password;
        UiLockingPasswordVisibleTextBox.Text = password;
        _isSyncingUiLockPassword = false;
    }
}
