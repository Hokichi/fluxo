using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Helpers.MainWindow;
using Fluxo.Resources.CustomControls;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Popups;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;
using Fluxo.Views.Shell.Main;

namespace Fluxo.Views.Popups;

public partial class QuickAddPopup : BasePopup
{
    private readonly IDialogService _dialogService;
    private readonly MainVM _mainViewModel;
    private readonly IMessenger _messenger;
    private readonly QuickAccessVM _viewModel;
    private bool _allowClose;
    private bool _isSavingBeforeClose;

    public QuickAddPopup(
        QuickAccessVM viewModel,
        MainVM mainViewModel,
        IDialogService dialogService,
        IMessenger messenger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _messenger = messenger;
        DataContext = viewModel;
        QuickAccessRows.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnTileClick));
        Loaded += OnLoadedAsync;
        Closing += OnClosingAsync;
        Closed += OnClosed;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        UpdateOperationalGate();
        try
        {
            await _viewModel.LoadAsync();
            UpdateOperationalGate();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "load Quick Access");
        }
    }

    private async void OnEditDoneClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsEditing)
        {
            _viewModel.BeginEditing();
            return;
        }

        try
        {
            await _viewModel.SaveAsync();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "save Quick Access");
        }
    }

    private void OnTileClick(object sender, RoutedEventArgs e)
    {
        var button = FindAncestor<Button>(e.OriginalSource as DependencyObject);
        if (button?.DataContext is not QuickAccessTileVM tile)
            return;

        e.Handled = true;
        if (_viewModel.IsEditing)
        {
            _viewModel.Toggle(tile);
            return;
        }

        if (!tile.IsActionEnabled || Owner is not MainWindow ownerWindow)
            return;

        CloseForPopupHandoff();
        ownerWindow.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await MainWindowFeatureActivationHelper.ActivateAsync(ownerWindow, tile.Target);
            }
            catch (Exception exception)
            {
                FloatingNotificationPublisher.LoggedFailure(_messenger, exception, $"open {tile.Title}");
            }
        }));
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainVM.IsSufficientFundsActionGateLocked))
            UpdateOperationalGate();
    }

    private void UpdateOperationalGate()
    {
        _viewModel.SetSufficientFundsGate(_mainViewModel.IsSufficientFundsActionGateLocked);
    }

    private async void OnClosingAsync(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.HasPendingChanges)
            return;

        e.Cancel = true;
        if (_isSavingBeforeClose)
            return;

        var answer = _dialogService.ShowQuestion(
            "Save Quick Access changes before closing?",
            "Quick Access",
            this,
            MessageBoxButton.YesNo);
        if (answer == MessageBoxResult.No)
        {
            _viewModel.DiscardChanges();
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(Close);
            return;
        }

        _isSavingBeforeClose = true;
        try
        {
            await _viewModel.SaveAsync();
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "save Quick Access");
        }
        finally
        {
            _isSavingBeforeClose = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        Loaded -= OnLoadedAsync;
        Closing -= OnClosingAsync;
        Closed -= OnClosed;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;
            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }
}
