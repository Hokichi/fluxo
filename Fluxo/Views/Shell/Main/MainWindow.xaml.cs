using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.DataModels.Messages;
using Fluxo.Helpers.MainWindow;
using Fluxo.Helpers.Settings;
using Fluxo.Resources.Infrastructure;
using Fluxo.Resources.Theming;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.History;
using Fluxo.Services.Logging;
using Fluxo.Services.Notifications;
using Fluxo.Services.Theming;
using Fluxo.Services.Updates;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.Helpers.Popups;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using Fluxo.Views.Popups;
using Fluxo.Views.Popups.Settings;
using Microsoft.Extensions.DependencyInjection;
using Analytics = Fluxo.Views.Shell.Main.Pages.Analytics;
using Calendar = Fluxo.Views.Shell.Main.Pages.Calendar;
using Dashboard = Fluxo.Views.Shell.Main.Pages.Dashboard;
using DateRangeResolver = Fluxo.Helpers.MainWindow.DateRangeResolver;
using Ledger = Fluxo.Views.Shell.Main.Pages.Ledger;
using MainVM = Fluxo.ViewModels.Shell.Main.MainVM;

namespace Fluxo.Views.Shell.Main;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, IPopupHost
{
    public static readonly DependencyProperty IsWindowLayoutMaximizedProperty = DependencyProperty.Register(
        nameof(IsWindowLayoutMaximized),
        typeof(bool),
        typeof(MainWindow),
        new PropertyMetadata(false));

    public static readonly DependencyProperty ActivePageTitleProperty = DependencyProperty.Register(
        nameof(ActivePageTitle),
        typeof(string),
        typeof(MainWindow),
        new PropertyMetadata("Dashboard"));

    private enum MainPage
    {
        Dashboard,
        Analytics,
        Calendar,
        Ledger
    }

    private const int FadeDuration = 180; // ms
    private const int StateChangeDuration = 100; // ms
    private const int MainPageTransitionDuration = 300; // ms
    private const double HeaderSearchCollapsedWidth = 36;
    private const double HeaderSearchExpandedWidth = 160;
    private const int HeaderSearchAnimationDuration = 160; // ms
    private const int HistoryDrawerAnimationDuration = 180; // ms
    private static readonly TimeSpan AppAutoLockActiveDelay = TimeSpan.FromSeconds(10);

    private readonly DispatcherTimer _headerMenuCloseTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _popupOverlayDeferredHideTimer = new() { Interval = TimeSpan.FromMilliseconds(FadeDuration) };
    private readonly DispatcherTimer _appAutoLockActiveDelayTimer = new() { Interval = AppAutoLockActiveDelay };
    private readonly DispatcherTimer _appAutoLockCountdownTimer = new();
    private readonly IAppDataService _appData;
    private readonly ThemeService _themeService;
    private readonly LogMemoryManager _logMemoryManager;
    private readonly MainVM _mainVM;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly FloatingNotificationOverlayWindow _floatingNotificationOverlay;
    private readonly IMessenger _messenger;
    private readonly TransactionPopupAddTagHost _addTagHost;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IAppUpdateInteractionService _appUpdateInteractionService;
    private readonly PopupOverlayHandoffState _popupOverlayHandoffState = new();
    private readonly ObservableCollection<HeaderQuickSearchResult> _headerSearchResults = [];
    private Rect _currentBounds;
    private bool _hasCompletedPendingDeletionCleanup;
    private bool _hasInitializedDashboardPanels;
    private bool _hasShownFloatingSummaries;
    private bool _isHeaderMenuPinned;
    private bool _isMaximized;
    private bool _isStateChangeTransitionActive;
    private bool _wasMinimized;
    private bool _isPointerOverHeaderMenuButton;
    private bool _isPointerOverHeaderMenuPopup;
    private bool _isMainPageTransitionActive;
    private bool _isPreparingMainPage;
    private bool _isHeaderSearchExpanded;
    private bool _isHistoryDrawerOpen;
    private bool _isHistoryDrawerAnimating;
    private int _headerSearchAnimationGeneration;
    private EventHandler? _popupOverlayDeferredHideTickHandler;
    private MainPage _activeMainPage = MainPage.Dashboard;
    private IServiceScope? _dashboardPageScope;
    private Dashboard? _dashboardPageView;
    private IServiceScope? _analyticsPageScope;
    private Analytics? _analyticsPageView;
    private IServiceScope? _calendarPageScope;
    private Calendar? _calendarPageView;
    private IServiceScope? _ledgerPageScope;
    private Ledger? _ledgerPageView;

    private EventHandler? _renderHandler;

    public bool IsWindowLayoutMaximized
    {
        get => (bool)GetValue(IsWindowLayoutMaximizedProperty);
        private set => SetValue(IsWindowLayoutMaximizedProperty, value);
    }

    public string ActivePageTitle
    {
        get => (string)GetValue(ActivePageTitleProperty);
        private set => SetValue(ActivePageTitleProperty, value);
    }

    public MainWindow(
        MainVM mainVM,
        IAppDataService appData,
        ThemeService themeService,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        FloatingNotificationOverlayWindow floatingNotificationOverlay,
        IMessenger messenger,
        IAppUpdateService appUpdateService,
        IAppUpdateInteractionService appUpdateInteractionService)
    {
        InitializeComponent();

        _mainVM = mainVM;
        _appData = appData;
        _themeService = themeService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _floatingNotificationOverlay = floatingNotificationOverlay;
        _messenger = messenger;
        _addTagHost = new TransactionPopupAddTagHost(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(), dialogService, messenger,
            ownerToken => Application.Current?.Windows.OfType<TransactionPopup>()
                .FirstOrDefault(popup => popup.IsOwnedBy(ownerToken)));
        _appUpdateService = appUpdateService;
        _appUpdateInteractionService = appUpdateInteractionService;
        HeaderThemeToggle.IsChecked = _themeService.CurrentTheme == ApplicationTheme.Dark;
        _logMemoryManager = new LogMemoryManager(_appData, _mainVM.ReloadCurrentDataAsync);
        WeakReferenceMessenger.Default.Register<MainWindow, NavigateToLedgerRequestedMessage>(
            this,
            static (recipient, message) => _ = recipient.NavigateToLedgerFromDashboardAsync());
        _messenger.Register<MainWindow, OpenHistoryDrawerMessage>(this,
            static (recipient, _) => recipient.OpenHistoryDrawer());
        _messenger.Register<MainWindow, NotificationProcessingRequestedMessage>(this,
            static (recipient, message) => _ = recipient.OpenNotificationProcessingAsync(message.Value.Category, message.Value.EntityIds));
        _messenger.Register<MainWindow, DashboardDailyDateRequestedMessage>(this,
            static (recipient, message) =>
            {
                if (recipient._activeMainPage == MainPage.Dashboard &&
                    recipient._mainVM.Dashboard.ViewModeToggle.SelectedMainContentViewMode == MainContentViewMode.Daily)
                    message.Reply(recipient._mainVM.DaySpinner.SelectedDay.Date);
            });

        HeaderSearchResultsList.ItemsSource = _headerSearchResults;
        HistoryItemsControl.ItemsSource = _logMemoryManager.HistoryEntries;
        _logMemoryManager.StateChanged += OnHistoryManagerStateChanged;
        UpdateHistoryAvailability();
        DataContext = _mainVM;
        _mainVM.PropertyChanged += OnMainViewModelPropertyChanged;

        Loaded += async (_, _) =>
        {
            if (!_hasInitializedDashboardPanels)
            {
                _hasInitializedDashboardPanels = true;
                await InitializeDashboardPanelsAsync();
            }

            EnsureDashboardPageLoaded();
            SetDashboardMainContentHitTestVisible(!_mainVM.IsAppLocked);
            MainPageHost.Content = _dashboardPageView;
            UpdateMainNavigationCheckedState(_activeMainPage);
            UpdateHeaderDaySelectorVisibility(_activeMainPage);
            UpdateHeaderDaySpinnerPagePolicy(_activeMainPage);
            _currentBounds = new Rect(Left, Top, Width, Height);
            UpdateExpandRestoreButtonIcon();
            RefreshAppLockVisualState();
            ResetAppAutoLockActivity();
            _floatingNotificationOverlay.Attach(this);
            ShowFloatingSummariesOnce();
            await ShowUpdateCardAsync();
            FadeIn();
        };

        Closing += OnWindowClosing;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        StateChanged += OnWindowStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseMove += OnWindowPreviewInputActivity;
        PreviewMouseWheel += OnWindowPreviewInputActivity;
        PreviewMouseLeftButtonDown += OnWindowPreviewMouseLeftButtonDown;
        _headerMenuCloseTimer.Tick += OnHeaderMenuCloseTimerTick;
        _appAutoLockActiveDelayTimer.Tick += OnAppAutoLockActiveDelayTimerTick;
        _appAutoLockCountdownTimer.Tick += OnAppAutoLockCountdownTimerTick;
    }

    internal static ApplicationTheme ResolveThemeFromToggle(bool? isChecked)
    {
        return isChecked == true ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    private async void OnHeaderThemeToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggle)
            return;

        toggle.IsEnabled = false;

        try
        {
            await _themeService.SwitchThemeAsync(ResolveThemeFromToggle(toggle.IsChecked));
        }
        catch (Exception exception)
        {
            toggle.IsChecked = _themeService.CurrentTheme == ApplicationTheme.Dark;
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "switch application theme");
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private void MainWindow_OnMouseMove(object sender, MouseEventArgs e)
    {
        var isEligibleHeaderDrag = Mouse.LeftButton == MouseButtonState.Pressed &&
                                   e.GetPosition(this).Y < 60 &&
                                   (e.OriginalSource is not DependencyObject source || !IsInteractiveElement(source));
        var restoreMode = MainWindowDragStateDecider.DecideRestoreMode(
            _isMaximized,
            _isStateChangeTransitionActive,
            isEligibleHeaderDrag);

        if (restoreMode == MainWindowRestoreMode.Noop)
            return;

        try
        {
            if (restoreMode == MainWindowRestoreMode.InstantRestoreAndDrag)
                RestoreInstantlyForDrag();

            DragMove();
        }
        catch (Exception exception)
        {
            FluxoLogManager.LogFailureForProcess(exception, "drag the main window");
        }
    }

    // ── Fade helpers ────────────────────────────────────────────────

    private void FadeIn(Action? onCompleted = null)
    {
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(FadeDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        if (onCompleted is not null)
            anim.Completed += (_, _) => onCompleted();

        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOut(Action onCompleted)
    {
        var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(FadeDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        anim.Completed += (_, _) => onCompleted();
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeContentOut(Action onCompleted)
    {
        FadeElements(
            GetStateChangeFadeElements(),
            0,
            EasingMode.EaseIn,
            onCompleted);
    }

    private void FadeContentIn(Action? onCompleted = null)
    {
        FadeElements(
            GetStateChangeFadeElements(),
            1,
            EasingMode.EaseOut,
            onCompleted);
    }

    private UIElement[] GetStateChangeFadeElements()
    {
        return [ContentGrid, MainPageHost, FloatingSideNavigationRail];
    }

    private static void FadeElements(UIElement[] elements, double toOpacity, EasingMode easingMode, Action? onCompleted = null)
    {
        if (elements.Length == 0)
        {
            onCompleted?.Invoke();
            return;
        }

        var pending = elements.Length;
        foreach (var element in elements)
            FadeElement(element, toOpacity, easingMode, () =>
            {
                pending--;
                if (pending == 0)
                    onCompleted?.Invoke();
            });
    }

    private static void FadeElement(UIElement element, double toOpacity, EasingMode easingMode, Action? onCompleted = null)
    {
        element.BeginAnimation(OpacityProperty, null);

        var animation = new DoubleAnimation(element.Opacity, toOpacity, TimeSpan.FromMilliseconds(FadeDuration))
        {
            EasingFunction = new CubicEase { EasingMode = easingMode }
        };

        if (onCompleted is not null)
            animation.Completed += (_, _) => onCompleted();

        element.BeginAnimation(OpacityProperty, animation);
    }

    // ── SystemCommand handlers ───────────────────────────────────────

    private void OnCloseWindow(object sender, ExecutedRoutedEventArgs e)
    {
        FadeOut(() => SystemCommands.CloseWindow(this));
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_hasCompletedPendingDeletionCleanup)
            return;

        if (Application.Current is App app && await app.TryHandleMainWindowClosingToTrayAsync(this))
        {
            e.Cancel = true;
            return;
        }

        if (!App.TryCloseOwnedWindows(this))
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;

        try
        {
            _hasCompletedPendingDeletionCleanup = true;
            _mainVM.PropertyChanged -= OnMainViewModelPropertyChanged;
            WeakReferenceMessenger.Default.Unregister<NavigateToLedgerRequestedMessage>(this);
            _messenger.Unregister<OpenHistoryDrawerMessage>(this);
            _addTagHost.Dispose();
            Activated -= OnWindowActivated;
            Deactivated -= OnWindowDeactivated;
            StateChanged -= OnWindowStateChanged;
            PreviewKeyDown -= OnPreviewKeyDown;
            PreviewMouseMove -= OnWindowPreviewInputActivity;
            PreviewMouseWheel -= OnWindowPreviewInputActivity;
            PreviewMouseLeftButtonDown -= OnWindowPreviewMouseLeftButtonDown;
            _headerMenuCloseTimer.Tick -= OnHeaderMenuCloseTimerTick;
            _appAutoLockActiveDelayTimer.Tick -= OnAppAutoLockActiveDelayTimerTick;
            _appAutoLockCountdownTimer.Tick -= OnAppAutoLockCountdownTimerTick;
            _headerMenuCloseTimer.Stop();
            StopAppAutoLockTimers();
            _logMemoryManager.StateChanged -= OnHistoryManagerStateChanged;
            _logMemoryManager.Dispose();
        }
        finally
        {
            CancelPendingPopupOverlayDeferredHide();
            DisposeMainPages();
            Application.Current.Shutdown(0);
        }
    }

    private void OnMinimizeWindow(object sender, ExecutedRoutedEventArgs e)
    {
        FadeOut(() => SystemCommands.MinimizeWindow(this));
    }

    public void HideToTray()
    {
        CancelPendingPopupOverlayDeferredHide();
        StartAppAutoLockCountdown();
        CloseHeaderMenu();
        CloseHeaderNotificationPopup();
        CollapseHeaderSearch();

        // If close-to-tray happens after a fade-out close animation, the window can
        // remain at zero opacity. Normalize before hiding so next restore is visible.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        ShowInTaskbar = false;
        Hide();
    }

    public void ShowFromTray()
    {
        // Always clear any previous opacity animation/state before showing.
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        ShowInTaskbar = true;

        if (!IsVisible)
            Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        // Force foreground activation from tray restores. A simple Activate/Focus
        // is not always enough after interacting with a tray popup.
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
        Keyboard.Focus(this);
        ResetAppAutoLockActivity();

        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
        }, DispatcherPriority.ApplicationIdle);

        FadeIn();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _wasMinimized = true;
            StartAppAutoLockCountdown();
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
        }
        else if (WindowState == WindowState.Normal && _wasMinimized)
        {
            _wasMinimized = false;
            ResetAppAutoLockActivity();
            FadeIn();
        }
    }

    private void OnExpandRestoreWindow(object sender, RoutedEventArgs e)
    {
        if (_isMaximized)
            AnimateToRestored();
        else
            AnimateToMaximized();
    }

    private void UpdateExpandRestoreButtonIcon()
    {
        if (ExpandRestoreButton is null)
            return;

        var iconKey = _isMaximized ? "CompressAlt" : "ExpandAlt";
        ExpandRestoreButton.ButtonIcon = FindResource(iconKey);
        ExpandRestoreButton.ButtonText = _isMaximized ? "Restore" : "Maximize";
    }

    private void AnimateToMaximized()
    {
        if (_isMaximized || _isStateChangeTransitionActive)
            return;

        var from = new Rect(Left, Top, Width, Height);
        _isMaximized = true;
        UpdateExpandRestoreButtonIcon();

        var maximizedBounds = GetMonitorBounds();
        _currentBounds = maximizedBounds;
        AnimateStateChange(from, maximizedBounds, true);
    }

    private void AnimateToRestored()
    {
        if (!_isMaximized || _isStateChangeTransitionActive)
            return;

        var from = new Rect(Left, Top, Width, Height);
        _isMaximized = false;
        UpdateExpandRestoreButtonIcon();

        var workArea = GetMonitorWorkArea();
        var restoreBounds = WindowRestoreBoundsResolver.ResolveCenteredRestoreBounds(workArea);
        _currentBounds = restoreBounds;
        AnimateStateChange(from, restoreBounds, false);
    }

    private void RestoreInstantlyForDrag()
    {
        ClearWindowBoundsAnimations();

        var preRestorePointer = Mouse.GetPosition(this);
        var preRestoreWidth = Width;
        var preRestoreHeight = Height;

        var workArea = GetMonitorWorkArea();
        var restoreBounds = WindowRestoreBoundsResolver.ResolveCenteredRestoreBounds(workArea);
        var anchoredRestoreBounds = ResolvePointerAnchoredRestoreBounds(
            restoreBounds,
            workArea,
            preRestorePointer,
            preRestoreWidth,
            preRestoreHeight);

        RootBorder.BorderThickness = new Thickness(1);

        Left = anchoredRestoreBounds.Left;
        Top = anchoredRestoreBounds.Top;
        Width = anchoredRestoreBounds.Width;
        Height = anchoredRestoreBounds.Height;

        _isMaximized = false;
        IsWindowLayoutMaximized = false;
        _isStateChangeTransitionActive = false;
        _currentBounds = anchoredRestoreBounds;
        UpdateExpandRestoreButtonIcon();
    }

    private Rect ResolvePointerAnchoredRestoreBounds(
        Rect restoreBounds,
        Rect workArea,
        Point preRestorePointer,
        double preRestoreWidth,
        double preRestoreHeight)
    {
        var cursorScreenDevice = PointToScreen(preRestorePointer);
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var cursorScreenDip = fromDevice.Transform(cursorScreenDevice);

        var relativeX = preRestoreWidth <= 0 ? 0 : Math.Clamp(preRestorePointer.X / preRestoreWidth, 0, 1);
        var relativeY = preRestoreHeight <= 0 ? 0 : Math.Clamp(preRestorePointer.Y / preRestoreHeight, 0, 1);

        var targetLeft = cursorScreenDip.X - (restoreBounds.Width * relativeX);
        var targetTop = cursorScreenDip.Y - (restoreBounds.Height * relativeY);

        return new Rect(
            ClampWindowOrigin(targetLeft, workArea.Left, workArea.Right - restoreBounds.Width),
            ClampWindowOrigin(targetTop, workArea.Top, workArea.Bottom - restoreBounds.Height),
            restoreBounds.Width,
            restoreBounds.Height);
    }

    private static double ClampWindowOrigin(double value, double min, double max)
    {
        if (max < min)
            return min;

        return Math.Clamp(value, min, max);
    }

    private void AnimateStateChange(Rect from, Rect to, bool maximizing)
    {
        _isStateChangeTransitionActive = true;

        FadeContentOut(() =>
        {
            RootBorder.BorderThickness = maximizing ? new Thickness(0) : new Thickness(1);
            MainGrid.Margin = maximizing ? new Thickness(0) : new Thickness(8);
            RootBorder.CornerRadius = maximizing ? new CornerRadius(0) : new CornerRadius(8);
            IsWindowLayoutMaximized = maximizing;

            AnimateBounds(from, to, maximizing, () =>
            {
                FadeContentIn(() =>
                {
                    _isStateChangeTransitionActive = false;
                });
            });
        });
    }

    private void AnimateBounds(Rect from, Rect to, bool maximizing, Action onCompleted)
    {
        ClearWindowBoundsAnimations();

        var ease = new CubicEase
        {
            EasingMode = maximizing ? EasingMode.EaseOut : EasingMode.EaseInOut
        };
        var durationMs = (double)StateChangeDuration;
        var startTime = TimeSpan.Zero;

        _renderHandler = (sender, e) =>
        {
            var timestamp = ((RenderingEventArgs)e).RenderingTime;
            if (startTime == TimeSpan.Zero)
                startTime = timestamp;

            var t = Math.Min(1.0, (timestamp - startTime).TotalMilliseconds / durationMs);
            var eased = ease.Ease(t);

            var currentBounds = WindowBoundsInterpolator.Interpolate(from, to, eased);
            Left = currentBounds.Left;
            Top = currentBounds.Top;
            Width = currentBounds.Width;
            Height = currentBounds.Height;

            if (t >= 1.0)
            {
                Left = to.Left;
                Top = to.Top;
                Width = to.Width;
                Height = to.Height;
                CompositionTarget.Rendering -= _renderHandler;
                _renderHandler = null;
                onCompleted();
            }
        };

        CompositionTarget.Rendering += _renderHandler;
    }

    private void ClearWindowBoundsAnimations()
    {
        if (_renderHandler is not null)
        {
            CompositionTarget.Rendering -= _renderHandler;
            _renderHandler = null;
        }

        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
    }

    // ── Monitor work area ───────────────────────────────────────────

    private Rect GetMonitorWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);

        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice
                         ?? Matrix.Identity;

        return new Rect(
            info.rcWork.Left * fromDevice.M11,
            info.rcWork.Top * fromDevice.M22,
            (info.rcWork.Right - info.rcWork.Left) * fromDevice.M11,
            (info.rcWork.Bottom - info.rcWork.Top) * fromDevice.M22);
    }

    private Rect GetMonitorBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);

        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice
                         ?? Matrix.Identity;

        return new Rect(
            info.rcMonitor.Left * fromDevice.M11,
            info.rcMonitor.Top * fromDevice.M22,
            (info.rcMonitor.Right - info.rcMonitor.Left) * fromDevice.M11,
            (info.rcMonitor.Bottom - info.rcMonitor.Top) * fromDevice.M22);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ── Shared UI helpers ───────────────────────────────────────────

    private async Task InitializeDashboardPanelsAsync()
    {
        try
        {
            await _mainVM.Initialize();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "initialize dashboard panels");
        }
    }

    private void OnTopBorderHitAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || !IsActive)
            return;

        if (HeaderMenuPopup.IsOpen)
            return;

        if (_isMaximized)
            AnimateToRestored();
        else
            AnimateToMaximized();

        e.Handled = true;
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        for (var current = source; current is not null; current = DependencyObjectTree.GetParent(current))
            if (current is ScrollBar or Thumb or ButtonBase or ListViewItem or TextBoxBase or Selector)
                return true;

        return false;
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsAppLocked())
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
                UnlockAppUiFromUser();

            e.Handled = true;
            return;
        }

        RecordAppAutoLockInputActivity();

        if (_isHistoryDrawerOpen && e.Key == Key.Escape)
        {
            CloseHistoryDrawer();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsToggleHistoryShortcut(e.Key, Keyboard.Modifiers))
        {
            ToggleHistoryDrawer();
            e.Handled = true;
            return;
        }

        if (!IsTextInputElementFocused() &&
            MainWindowShortcutMatcher.IsUndoShortcut(e.Key, Keyboard.Modifiers) &&
            _logMemoryManager.CanUndo)
        {
            if (!IsSufficientFundsActionGateLocked())
                await UndoLogMemoryAsync();
            e.Handled = true;
            return;
        }

        if (!IsTextInputElementFocused() &&
            MainWindowShortcutMatcher.IsRedoShortcut(e.Key, Keyboard.Modifiers) &&
            _logMemoryManager.CanRedo)
        {
            if (!IsSufficientFundsActionGateLocked())
                await RedoLogMemoryAsync();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsToggleAppLockShortcut(e.Key, Keyboard.Modifiers))
        {
            LockAppUiFromUser();
            e.Handled = true;
            return;
        }

        if (_isHeaderSearchExpanded && e.Key == Key.Escape)
        {
            CollapseHeaderSearch();
            e.Handled = true;
            return;
        }

        if (HeaderNotificationPopup.IsOpen && e.Key == Key.Escape)
        {
            CloseHeaderNotificationPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenHotkeysOverviewShortcut(e.Key, Keyboard.Modifiers))
        {
            OpenHotkeysOverviewPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenQuickAccessShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            OpenQuickAddPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenNewTransactionShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            OpenAddNewTransactionPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenRecurringNewTransactionShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            OpenRecurringAddNewTransactionPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenSearchShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            ExpandHeaderSearch();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenSettingsShortcut(e.Key, Keyboard.Modifiers))
        {
            OpenSettingsPopup();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenAccountsListPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenAddAccountShortcut(e.Key, Keyboard.Modifiers))
        {
            OpenAddAccountPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenAddSavingGoalShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            OpenAddSavingGoalPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenPlanningShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            OpenPlanningReport();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenBudgetForecastShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            OpenBudgetForecast();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenDataManagementShortcut(e.Key, Keyboard.Modifiers))
        {
            OpenDataManagementPopup();
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenAnalyticsShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            await NavigateToMainPageAsync(MainPage.Analytics);
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenDashboardShortcut(e.Key, Keyboard.Modifiers))
        {
            await NavigateToMainPageAsync(MainPage.Dashboard);
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenCalendarShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            await NavigateToMainPageAsync(MainPage.Calendar);
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsOpenLedgerShortcut(e.Key, Keyboard.Modifiers))
        {
            if (IsSufficientFundsActionGateLocked())
            {
                e.Handled = true;
                return;
            }

            await NavigateToMainPageAsync(MainPage.Ledger);
            e.Handled = true;
            return;
        }

        if (MainWindowShortcutMatcher.IsToggleNotificationsShortcut(e.Key, Keyboard.Modifiers))
        {
            ToggleHeaderNotificationPopup();
            e.Handled = true;
            return;
        }

        if (await TryHandleDashboardPeriodShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (await TryHandleViewModeShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleLedgerShortcut(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

    }

    private async Task<bool> TryHandleDashboardPeriodShortcut(Key key, ModifierKeys modifiers)
    {
        if (_activeMainPage != MainPage.Dashboard)
            return false;

        if (MainWindowShortcutMatcher.IsNavigateDashboardPreviousPeriodShortcut(key, modifiers))
        {
            await _mainVM.DaySpinner.SelectAdjacentVisibleDayFromUserAsync(-1, this);
            return true;
        }

        if (MainWindowShortcutMatcher.IsNavigateDashboardNextPeriodShortcut(key, modifiers))
        {
            await _mainVM.DaySpinner.SelectAdjacentVisibleDayFromUserAsync(1, this);
            return true;
        }

        if (MainWindowShortcutMatcher.IsNavigateDashboardCurrentPeriodShortcut(key, modifiers))
        {
            if (_mainVM.Dashboard.ViewModeToggle.IsAtCurrentPeriod)
                return true;

            await _mainVM.Dashboard.ViewModeToggle.MoveToCurrentPeriodFromUserAsync(this);
            return true;
        }

        return false;
    }

    private async Task<bool> TryHandleViewModeShortcut(Key key, ModifierKeys modifiers)
    {
        if (!MainWindowShortcutMatcher.TryGetViewModeShortcut(key, modifiers, out var viewMode))
            return false;

        if (_activeMainPage != MainPage.Dashboard)
            return false;

        await _mainVM.Dashboard.ViewModeToggle.SetSelectedMainContentViewFromUserAsync(viewMode, this);
        return true;
    }

    private bool TryHandleLedgerShortcut(Key key, ModifierKeys modifiers)
    {
        if (_activeMainPage != MainPage.Ledger || _ledgerPageView is null)
            return false;

        if (MainWindowShortcutMatcher.IsLedgerExportShortcut(key, modifiers))
        {
            _ledgerPageView.ExportDataFromShortcutAsync();
            return true;
        }

        if (MainWindowShortcutMatcher.IsLedgerClearFiltersShortcut(key, modifiers))
        {
            _ledgerPageView.ClearFiltersFromShortcutAsync();
            return true;
        }

        if (MainWindowShortcutMatcher.IsLedgerAscendingSortShortcut(key, modifiers))
        {
            _ledgerPageView.ApplyAmountSortDirectionFromShortcutAsync(LedgerAmountSortDirection.Ascending);
            return true;
        }

        if (MainWindowShortcutMatcher.IsLedgerDescendingSortShortcut(key, modifiers))
        {
            _ledgerPageView.ApplyAmountSortDirectionFromShortcutAsync(LedgerAmountSortDirection.Descending);
            return true;
        }

        return false;
    }

    private void OnHeaderSearchButtonClick(object sender, RoutedEventArgs e)
    {
        if (IsAppLocked() || IsSufficientFundsActionGateLocked())
            return;

        ExpandHeaderSearch();
    }

    private async Task ShowUpdateCardAsync()
    {
        try
        {
            var update = await _appUpdateService.CheckForUpdatesAsync(AppVersionResolver.ResolveCurrentVersion());
            if (update.Status != AppUpdateCheckStatus.UpdateAvailable || string.IsNullOrWhiteSpace(update.LatestVersion))
                return;

            FloatingNotificationPublisher.Publish(_messenger,
                $"fluxo version {update.LatestVersion} is available",
                "Click to start update", [], NotificationSeverity.Info,
                () => _appUpdateInteractionService.HandleAvailableUpdateAsync(update, this));
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "check for updates");
        }
    }

    private void ShowFloatingSummariesOnce()
    {
        if (_hasShownFloatingSummaries)
            return;

        _hasShownFloatingSummaries = true;
        var upcomingCount = _mainVM.UpcomingEventsPanel.Events.Count;
        if (upcomingCount > 0)
        {
            FloatingNotificationPublisher.Publish(_messenger,
                $"You have {upcomingCount} upcoming events",
                "Click to view upcoming events", [], NotificationSeverity.Info,
                async () => await NavigateToMainPageAsync(MainPage.Dashboard));
        }

        var notificationCount = _mainVM.NotificationPanel.NotificationItems.Count;
        if (notificationCount > 0)
        {
            FloatingNotificationPublisher.Publish(_messenger,
                $"You have {notificationCount} notifications",
                "Click to view notifications", [], NotificationSeverity.Info,
                () =>
                {
                    HeaderNotificationPopup.IsOpen = true;
                    return Task.CompletedTask;
                });
        }
    }

    private void OnHeaderQuickAddButtonClick(object sender, RoutedEventArgs e)
    {
        if (IsAppLocked() || IsSufficientFundsActionGateLocked())
            return;

        OpenAddNewTransactionPopup();
    }

    private void OnHeaderAppLockButtonClick(object sender, RoutedEventArgs e)
    {
        if (IsAppLocked())
            UnlockAppUiFromUser();
        else
            LockAppUiFromUser();

        HeaderAppLockButton.IsChecked = _mainVM.IsAppLocked;
    }

    private void OnHeaderNotificationButtonClick(object sender, RoutedEventArgs e)
    {
        if (IsAppLocked())
            return;

        ToggleHeaderNotificationPopup();
    }

    private void OnHeaderHistoryButtonClick(object sender, RoutedEventArgs e)
    {
        if (!IsAppLocked())
            ToggleHistoryDrawer();
    }

    private void ToggleHeaderNotificationPopup()
    {
        HeaderNotificationPopup.IsOpen = !HeaderNotificationPopup.IsOpen;
    }

    private void OnHeaderSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_activeMainPage == MainPage.Ledger)
            WeakReferenceMessenger.Default.Send(new LedgerSearchTextChangedMessage(HeaderSearchBox.Text));

        UpdateHeaderSearchResults();
    }

    private void OnHeaderSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CollapseHeaderSearch();
        e.Handled = true;
    }

    private void OnHeaderSearchRegionPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_isHeaderSearchExpanded || e.NewFocus is not DependencyObject newFocus)
            return;

        if (DependencyObjectTree.IsDescendantOf(newFocus, HeaderSearchRegion))
            return;

        if (_activeMainPage == MainPage.Ledger && !ShouldCollapseHeaderSearchOnExternalClick())
            return;

        CollapseHeaderSearch();
    }

    private void OnHeaderSearchResultItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: HeaderQuickSearchResult result })
            return;

        CollapseHeaderSearch();

        OpenTransactionDetailPopup(result.Transaction);

        e.Handled = true;
    }

    private void ExpandHeaderSearch()
    {
        if (!_isHeaderSearchExpanded)
        {
            _isHeaderSearchExpanded = true;
            var animationGeneration = ++_headerSearchAnimationGeneration;
            HeaderSearchButton.Visibility = Visibility.Collapsed;
            HeaderSearchInputBorder.Visibility = Visibility.Visible;
            HeaderSearchInputBorder.IsHitTestVisible = true;
            HeaderSearchInputBorder.Width = HeaderSearchCollapsedWidth;
            HeaderSearchInputBorder.Opacity = 0;
            AnimateHeaderSearchInput(HeaderSearchExpandedWidth, 1, () =>
            {
                if (animationGeneration == _headerSearchAnimationGeneration)
                    HeaderSearchInputBorder.Width = HeaderSearchExpandedWidth;
            });
        }

        HeaderSearchBox.Focus();
        HeaderSearchBox.SelectAll();
        UpdateHeaderSearchResults();
    }

    private void CollapseHeaderSearch()
    {
        if (!_isHeaderSearchExpanded)
            return;

        _isHeaderSearchExpanded = false;
        var animationGeneration = ++_headerSearchAnimationGeneration;
        HeaderSearchResultsPopup.IsOpen = false;
        HeaderSearchNoResultsText.Visibility = Visibility.Collapsed;
        HeaderSearchBox.Text = string.Empty;
        WeakReferenceMessenger.Default.Send(new LedgerSearchTextChangedMessage(string.Empty));
        _headerSearchResults.Clear();
        HeaderSearchInputBorder.IsHitTestVisible = false;
        AnimateHeaderSearchInput(HeaderSearchCollapsedWidth, 0, () =>
        {
            if (animationGeneration != _headerSearchAnimationGeneration)
                return;

            HeaderSearchInputBorder.Visibility = Visibility.Collapsed;
            HeaderSearchButton.Visibility = Visibility.Visible;
            HeaderSearchInputBorder.Width = HeaderSearchExpandedWidth;
            HeaderSearchInputBorder.Opacity = 0;
        });
    }

    private void AnimateHeaderSearchInput(double targetWidth, double targetOpacity, Action? completed = null)
    {
        var duration = TimeSpan.FromMilliseconds(HeaderSearchAnimationDuration);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var currentWidth = HeaderSearchInputBorder.ActualWidth > 0
            ? HeaderSearchInputBorder.ActualWidth
            : HeaderSearchInputBorder.Width;

        var widthAnimation = new DoubleAnimation(currentWidth, targetWidth, duration)
        {
            EasingFunction = easing
        };

        var opacityAnimation = new DoubleAnimation(HeaderSearchInputBorder.Opacity, targetOpacity, duration)
        {
            EasingFunction = easing
        };

        if (completed is not null)
            opacityAnimation.Completed += (_, _) => completed();

        HeaderSearchInputBorder.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        HeaderSearchInputBorder.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
    }

    private void UpdateHeaderSearchResults()
    {
        if (!_isHeaderSearchExpanded)
            return;

        if (_activeMainPage == MainPage.Ledger)
        {
            HeaderSearchResultsPopup.IsOpen = false;
            HeaderSearchNoResultsText.Visibility = Visibility.Collapsed;
            _headerSearchResults.Clear();
            return;
        }

        var query = HeaderSearchBox.Text;
        var matches = HeaderQuickSearchEngine.Search(
            _mainVM.BudgetPanel.GetAllTransactions(),
            query).ToList();

        _headerSearchResults.Clear();

        if (matches.Count == 0)
        {
            var normalizedQuery = query?.Trim();
            if (string.IsNullOrEmpty(normalizedQuery) || normalizedQuery.Length <= 3)
            {
                HeaderSearchResultsPopup.IsOpen = false;
                HeaderSearchNoResultsText.Visibility = Visibility.Collapsed;
                return;
            }

            HeaderSearchResultsPopup.IsOpen = true;
            HeaderSearchNoResultsText.Visibility = Visibility.Visible;
            return;
        }

        HeaderSearchResultsPopup.IsOpen = true;
        HeaderSearchNoResultsText.Visibility = Visibility.Collapsed;
        foreach (var match in matches)
            _headerSearchResults.Add(match);
    }

    private void OnQuickAddButtonClick(object sender, RoutedEventArgs e)
    {
        CloseHeaderMenu();
        if (IsSufficientFundsActionGateLocked())
            return;

        OpenQuickAddPopup();
    }

    private void OnHotkeysButtonClick(object sender, RoutedEventArgs e)
    {
        CloseHeaderMenu();
        OpenHotkeysOverviewPopup();
    }

    private void OnHeaderMenuButtonMouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerOverHeaderMenuButton = true;
        _headerMenuCloseTimer.Stop();
        OpenHeaderMenu(false);
    }

    private void OnHeaderMenuButtonMouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerOverHeaderMenuButton = false;
        ScheduleHeaderMenuClose();
    }

    private void OnHeaderMenuPopupMouseEnter(object sender, MouseEventArgs e)
    {
        _isPointerOverHeaderMenuPopup = true;
        _headerMenuCloseTimer.Stop();
    }

    private void OnHeaderMenuPopupMouseLeave(object sender, MouseEventArgs e)
    {
        _isPointerOverHeaderMenuPopup = false;
        ScheduleHeaderMenuClose();
    }

    private void OnHeaderMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (IsAppLocked())
            return;

        OpenHeaderMenu(true);
    }

    private void OnAccountsButtonClick(object sender, RoutedEventArgs e)
    {
        CloseHeaderMenu();
        OpenAccountsListPopup();
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        CloseHeaderMenu();
        OpenSettingsPopup();
    }

    private async void OnHomeNavigationClick(object sender, RoutedEventArgs e)
    {
        await NavigateToMainPageAsync(MainPage.Dashboard);
    }

    private async void OnAnalyticsNavigationClick(object sender, RoutedEventArgs e)
    {
        await NavigateToMainPageAsync(MainPage.Analytics);
    }

    private async void OnCalendarNavigationClick(object sender, RoutedEventArgs e)
    {
        await NavigateToMainPageAsync(MainPage.Calendar);
    }

    private async void OnLedgerNavigationClick(object sender, RoutedEventArgs e)
    {
        await NavigateToMainPageAsync(MainPage.Ledger);
    }

    private void OnAddAccountButtonClick(object sender, RoutedEventArgs e)
    {
        OpenAddAccountPopup();
    }

    private void OnDashboardSpendingAmountGateActionClick(object sender, RoutedEventArgs e)
    {
        OpenAddAccountPopup();
    }

    private void OnAppLockOverlayClick(object sender, RoutedEventArgs e)
    {
        UnlockAppUiFromUser();
    }

    public void OpenQuickAddPopup(TransactionPopupDraft? draft = null)
    {
        if (draft is { } popupDraft)
        {
            if (IsSufficientFundsActionGateLocked())
                return;

            OpenAddNewTransactionPopup(popupDraft);
            return;
        }

        if (IsSufficientFundsActionGateLocked())
            return;

        _dialogService.ShowQuickAdd(this);
    }

    public void OpenAddNewTransactionPopupForCategory(ExpenseCategory category)
    {
        OpenAddNewTransactionPopup(new TransactionPopupDraft(
            true,
            string.Empty,
            0m,
            null,
            DateTime.Today,
            string.Empty,
            category,
            null));
    }

    public void OpenAddNewTransactionPopup(TransactionPopupDraft? draft = null)
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        _dialogService.ShowAddNewTransaction(TransactionPopupRequest.Add(draft), this);
    }

    public void OpenRecurringAddNewTransactionPopup()
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        _dialogService.ShowAddNewTransaction(TransactionPopupRequest.AddRecurring(isLocked: false), this);
    }

    public async void OpenTransactionDetailPopup(TransactionVM transaction)
    {
        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var targetTransaction = await TransactionDetailTargetResolver.ResolveAsync(transaction, appData);
        if (targetTransaction is null)
            return;

        _dialogService.ShowAddNewTransaction(TransactionPopupRequest.View(targetTransaction), this);
    }

    private async Task OpenNotificationProcessingAsync(string category, IReadOnlyList<int> entityIds)
    {
        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        TransactionPopupRequest request;

        if (category == nameof(NotificationGroupCategory.LatePayment))
        {
            var accounts = (await appData.GetAccountsAsync()).Where(account => entityIds.Contains(account.Id)).Select(account => new AccountVM
            {
                Id = account.Id, Name = account.Name, AccountType = account.AccountType, SpentAmount = account.SpentAmount,
                DeductSource = account.DeductSource, IsEnabled = account.IsEnabled
            }).ToList();
            request = new TransactionPopupRequest { Kind = TransactionPopupRequestKind.RepaymentProcessing, Accounts = accounts };
        }
        else if (category == nameof(NotificationGroupCategory.GoalOverdue))
        {
            var goals = (await appData.GetSavingGoalsAsync()).Where(goal => entityIds.Contains(goal.Id)).Select(goal => new SavingGoalVM
            { Id = goal.Id, Name = goal.Name, CurrentAmount = goal.CurrentAmount, TargetAmount = goal.TargetAmount, SavingEndDate = goal.SavingEndDate }).ToList();
            request = new TransactionPopupRequest { Kind = TransactionPopupRequestKind.GoalProcessing, Goals = goals };
        }
        else if (category == nameof(NotificationGroupCategory.RecurringTransactionOverdue))
        {
            var recurring = (await appData.GetRecurringTransactionsAsync()).Where(item => entityIds.Contains(item.Id)).Select(item => new RecurringTransactionVM
            {
                Id = item.Id, Name = item.Name, Amount = item.Amount, Type = item.Type, Category = item.Category,
                Source = new AccountVM { Id = item.SourceId }, Tag = item.TagId is null ? null : new TagVM { Id = item.TagId.Value }
            }).ToList();
            request = new TransactionPopupRequest { Kind = TransactionPopupRequestKind.RecurringProcessing, RecurringTransactions = recurring };
        }
        else return;

        _dialogService.ShowAddNewTransaction(request, this);
    }

    public async void OpenLedgerTransactionDetailPopup(int transactionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var targetTransaction = await TransactionDetailTargetResolver.ResolveAsync(transactionId, appData);
        if (targetTransaction is null)
            return;

        _dialogService.ShowAddNewTransaction(TransactionPopupRequest.View(targetTransaction), this);
    }

    public void OpenAccountsListPopup()
    {
        _dialogService.ShowAccountsList(this);
    }

    public void OpenAddAccountPopup()
    {
        _dialogService.ShowAddAccount(this);
    }

    public void OpenAddSavingGoalPopup()
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        _dialogService.ShowAddSavingGoal(this);
    }

    public async void OpenEditSavingGoalPopup(int savingGoalId)
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var goal = await appData.GetSavingGoalByIdAsync(savingGoalId);
        if (goal is null)
            return;

        _dialogService.ShowAddSavingGoal(new AddSavingGoalVM(_mainVM, appData)
        {
            EditingId = goal.Id,
            NameText = goal.Name,
            TargetAmountText = goal.TargetAmount,
            CurrentAmountText = goal.CurrentAmount,
            EndDate = goal.SavingEndDate,
            HasDefiniteEndDate = goal.SavingEndDate.HasValue
        }, this);
    }

    public void OpenSettingsPopup()
    {
        _dialogService.ShowSettings(this);
    }

    public void OpenHotkeysOverviewPopup()
    {
        _dialogService.ShowHotkeysOverview(this);
    }

    public void OpenDataManagementPopup()
    {
        _dialogService.ShowDataManagement(this);
    }

    public async void OpenQuickSetupWizardPopup()
    {
        using var scope = _serviceProvider.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<SettingsPersonalizationTabVM>();
        await SettingsSetupWizardFlow.RunAsync(this, viewModel);
    }

    public async Task CheckForUpdatesFromQuickAccessAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<SettingsPersonalizationTabVM>();
        await SettingsUpdateCheckFlow.CheckForUpdatesAsync(this, viewModel);
    }

    public void OpenPlanningReport()
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        _dialogService.ShowPlanningReport(this);
    }

    public void OpenBudgetForecast()
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        _dialogService.ShowBudgetForecast(this);
    }

    public void OpenAnalyticsPopup()
    {
        if (IsSufficientFundsActionGateLocked())
            return;

        _ = OpenAnalyticsPopupAsync();
    }

    private async Task OpenAnalyticsPopupAsync()
    {
        await NavigateToMainPageAsync(MainPage.Analytics);
    }

    private async Task NavigateToLedgerFromDashboardAsync()
    {
        var selectedMode = _mainVM.Dashboard.ViewModeToggle.SelectedMainContentViewMode;
        if (selectedMode == MainContentViewMode.AllTime)
        {
            WeakReferenceMessenger.Default.Send(new LedgerAllTimeRequestedMessage());
        }
        else
        {
            var range = selectedMode == MainContentViewMode.AllocationPeriod
                ? _mainVM.BudgetPanel.GetCurrentAllocationPeriodRange(DateTime.Today)
                : DateRangeResolver.Resolve(
                    _mainVM.DaySpinner.SelectedDay.Date == default
                        ? DateTime.Today
                        : _mainVM.DaySpinner.SelectedDay.Date,
                    selectedMode);
            WeakReferenceMessenger.Default.Send(
                new LedgerDateRangeRequestedMessage(range.From, range.To));
        }

        await NavigateToMainPageAsync(MainPage.Ledger);
    }

    private async Task NavigateToMainPageAsync(MainPage page)
    {
        if (page != MainPage.Dashboard && IsSufficientFundsActionGateLocked())
        {
            UpdateMainNavigationCheckedState(_activeMainPage);
            return;
        }

        if (_activeMainPage == page || _isMainPageTransitionActive || _isPreparingMainPage)
        {
            UpdateMainNavigationCheckedState(_activeMainPage);
            return;
        }

        UpdateMainNavigationCheckedState(_activeMainPage);
        _isPreparingMainPage = true;
        SetMainNavigationEnabled(false);
        CloseHeaderMenu();

        try
        {
            UIElement? nextPage = null;
            await _dialogService.ShowToastWhileAsync(
                GetMainPageLoadingMessage(page),
                async () =>
                {
                    nextPage = ResolveMainPageView(page);
                    await TransitionToMainPageAsync(nextPage);
                    await PrepareMainPageContentAsync(page);
                    _activeMainPage = page;
                    RefreshActivePageTitle();
                    UpdateMainNavigationCheckedState(_activeMainPage);
                    UpdateHeaderDaySelectorVisibility(_activeMainPage);
                    UpdateHeaderDaySpinnerPagePolicy(_activeMainPage);
                },
                this);
        }
        catch (Exception exception)
        {
            var label = GetMainPageLabel(page);
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, $"open {label}");
            UpdateMainNavigationCheckedState(_activeMainPage);
        }
        finally
        {
            _isPreparingMainPage = false;
            SetMainNavigationEnabled(true);
        }
    }

    private UIElement ResolveMainPageView(MainPage page)
    {
        switch (page)
        {
            case MainPage.Dashboard:
                EnsureDashboardPageLoaded();
                return _dashboardPageView!;

            case MainPage.Analytics:
                EnsureAnalyticsPageLoaded();
                return _analyticsPageView!;

            case MainPage.Calendar:
                EnsureCalendarPageLoaded();
                return _calendarPageView!;

            case MainPage.Ledger:
                EnsureLedgerPageLoaded();
                return _ledgerPageView!;

            default:
                EnsureDashboardPageLoaded();
                return _dashboardPageView!;
        }
    }

    private async Task PrepareMainPageContentAsync(MainPage page)
    {
        switch (page)
        {
            case MainPage.Dashboard:
                PublishDashboardViewMode();
                if (_mainVM.Dashboard.IsInitialized)
                    await _mainVM.Dashboard.ReloadCurrentDataAsync();
                else
                    await _mainVM.Dashboard.Initialize();
                return;

            case MainPage.Analytics:
                await _analyticsPageView!.PrepareForOpenAsync(showInternalToast: false);
                return;

            case MainPage.Calendar:
                await _calendarPageView!.PrepareForOpenAsync();
                return;

            case MainPage.Ledger:
                await _ledgerPageView!.PrepareForOpenAsync();
                return;

            default:
                return;
        }
    }

    private void EnsureDashboardPageLoaded()
    {
        if (_dashboardPageView is not null)
            return;

        _dashboardPageScope = _serviceProvider.CreateScope();
        _dashboardPageView = _dashboardPageScope.ServiceProvider.GetRequiredService<Dashboard>();
        SetDashboardMainContentHitTestVisible(!_mainVM.IsAppLocked);
    }

    private void EnsureAnalyticsPageLoaded()
    {
        if (_analyticsPageView is not null)
            return;

        _analyticsPageScope = _serviceProvider.CreateScope();
        _analyticsPageView = _analyticsPageScope.ServiceProvider.GetRequiredService<Analytics>();
    }

    private void EnsureCalendarPageLoaded()
    {
        if (_calendarPageView is not null)
            return;

        _calendarPageScope = _serviceProvider.CreateScope();
        _calendarPageView = _calendarPageScope.ServiceProvider.GetRequiredService<Calendar>();
    }

    private void EnsureLedgerPageLoaded()
    {
        if (_ledgerPageView is not null)
            return;

        _ledgerPageScope = _serviceProvider.CreateScope();
        _ledgerPageView = _ledgerPageScope.ServiceProvider.GetRequiredService<Ledger>();
    }

    private async Task TransitionToMainPageAsync(UIElement nextPage)
    {
        _isMainPageTransitionActive = true;
        try
        {
            await FadeElementAsync(MainPageHost, 0, EasingMode.EaseIn, MainPageTransitionDuration);

            MainPageHost.BeginAnimation(OpacityProperty, null);
            MainPageHost.Content = nextPage;
            MainPageHost.Visibility = Visibility.Visible;
            MainPageHost.Opacity = 0;

            await FadeElementAsync(MainPageHost, 1, EasingMode.EaseOut, MainPageTransitionDuration);
        }
        finally
        {
            _isMainPageTransitionActive = false;
        }
    }

    private static Task FadeElementAsync(UIElement element, double toOpacity, EasingMode easingMode, int durationMilliseconds)
    {
        element.BeginAnimation(OpacityProperty, null);

        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation(element.Opacity, toOpacity, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = easingMode }
        };

        animation.Completed += (_, _) => completion.SetResult();
        element.BeginAnimation(OpacityProperty, animation);
        return completion.Task;
    }

    private void SetMainNavigationEnabled(bool isEnabled)
    {
        HomeNavigationButton.IsEnabled = isEnabled;
        AnalyticsNavigationButton.IsEnabled = isEnabled;
        CalendarNavigationButton.IsEnabled = isEnabled;
        LedgerNavigationButton.IsEnabled = isEnabled;
    }

    private void UpdateMainNavigationCheckedState(MainPage page)
    {
        HomeNavigationButton.IsChecked = page == MainPage.Dashboard;
        AnalyticsNavigationButton.IsChecked = page == MainPage.Analytics;
        CalendarNavigationButton.IsChecked = page == MainPage.Calendar;
        LedgerNavigationButton.IsChecked = page == MainPage.Ledger;
    }

    private void UpdateHeaderDaySelectorVisibility(MainPage page)
    {
        DaySpinnerControlHost.Visibility = page == MainPage.Dashboard
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateHeaderDaySpinnerPagePolicy(MainPage page)
    {
        _mainVM.DaySpinner.AllowFuturePeriodNavigation = page == MainPage.Dashboard;
    }

    private static string GetMainPageLabel(MainPage page)
    {
        return page switch
        {
            MainPage.Dashboard => "Dashboard",
            MainPage.Analytics => "Analytics",
            MainPage.Calendar => "Calendar",
            MainPage.Ledger => "Ledger",
            _ => "Dashboard"
        };
    }

    private static string GetMainPageTitle(MainPage page) => page switch
    {
        MainPage.Dashboard => "Dashboard",
        MainPage.Analytics => "Analytics",
        MainPage.Calendar => "Calendar",
        MainPage.Ledger => "Ledger",
        _ => "Dashboard"
    };

    private static string GetMainPageLoadingMessage(MainPage page)
    {
        return page switch
        {
            MainPage.Dashboard => "Loading Dashboard",
            MainPage.Analytics => "Loading Analytics",
            MainPage.Calendar => "Loading Calendar",
            MainPage.Ledger => "Loading Ledger",
            _ => "Loading Dashboard"
        };
    }

    public void OpenAccountDetailPopup(AccountVM account)
    {
        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var popupViewModel = new AccountDetailVM(_mainVM, account.Id, appData);
        _dialogService.ShowAccountDetail(popupViewModel, this);
    }

    public void ToggleAccountFilter(AccountVM? account)
    {
        _mainVM.ToggleAccountFilter(account);
    }

    public async Task ExecuteDeleteAccountActionAsync(AccountVM account)
    {
        await ExecuteAccountSettingsActionAsync(account, SettingsBatchAction.Delete, true);
    }

    public async Task ExecuteUnpinAccountActionAsync(AccountVM account)
    {
        await ExecuteAccountSettingsActionAsync(account, SettingsBatchAction.Unpin);
    }

    public async Task ExecuteDisableAccountActionAsync(AccountVM account)
    {
        await ExecuteAccountSettingsActionAsync(account, SettingsBatchAction.Disable);
    }

    public void OpenTransferFundsPopup(AccountVM account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.CanTransfer)
            return;

        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var transferVm = new TransferFundsVM(_mainVM, account, appData);
        _dialogService.ShowTransferFunds(transferVm, this);
    }

    public void OpenRepaymentPopup(AccountVM account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.IsCredit)
            return;

        _dialogService.ShowAddNewTransaction(new TransactionPopupRequest
        {
            Kind = TransactionPopupRequestKind.Repayment,
            Account = account
        }, this);
    }

    public void OpenAccountReconciliationPopup(AccountVM account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.CanReconcile)
            return;

        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var reconciliationVm = new AccountReconciliationVM(
            _mainVM.BudgetPanel.Accounts,
            account,
            appData,
            _mainVM.ReloadCurrentDataAsync);
        _dialogService.ShowAccountReconciliation(reconciliationVm, this);
    }

    public async void OpenTransactionDetailPopupForEditing(TransactionVM transaction)
    {
        using var scope = _serviceProvider.CreateScope();
        var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();
        var targetTransaction = await TransactionDetailTargetResolver.ResolveAsync(transaction, appData);
        if (targetTransaction is null)
            return;

        _dialogService.ShowAddNewTransaction(new TransactionPopupRequest
        {
            Kind = TransactionPopupRequestKind.EditTransaction,
            Transaction = targetTransaction
        }, this);
    }

    private void PublishDashboardViewMode()
    {
        var viewModeToggle = _mainVM.Dashboard.ViewModeToggle;
        viewModeToggle.SetSelectedMainContentViewCommand.Execute(viewModeToggle.SelectedMainContentViewMode);
    }

    private static bool ShouldReduceMotion()
    {
        return !SystemParameters.ClientAreaAnimation;
    }

    private void DisposeMainPages()
    {
        MainPageHost.Content = null;

        _dashboardPageView = null;
        _dashboardPageScope?.Dispose();
        _dashboardPageScope = null;

        _analyticsPageView = null;
        _analyticsPageScope?.Dispose();
        _analyticsPageScope = null;

        _calendarPageView = null;
        _calendarPageScope?.Dispose();
        _calendarPageScope = null;

        _ledgerPageView = null;
        _ledgerPageScope?.Dispose();
        _ledgerPageScope = null;

        _activeMainPage = MainPage.Dashboard;
        _isMainPageTransitionActive = false;
        _isPreparingMainPage = false;
    }

    // ── Popup overlay & blur ────────────────────────────────────────

    public void BeginPopupHandoff()
    {
        // Compatibility marker for hosts that still announce a handoff before closing.
    }

    public void ShowPopupOverlay()
    {
        CancelPendingPopupOverlayDeferredHide();

        var hostAction = _popupOverlayHandoffState.OnPopupShown();
        if (hostAction != PopupOverlayHostAction.ShowOverlay)
            return;

        ApplyPopupBlur();
        PopupOverlay.BeginAnimation(OpacityProperty, null);
        PopupOverlay.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(PopupOverlay.Opacity, 0.5, TimeSpan.FromMilliseconds(FadeDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        PopupOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void ToggleHistoryDrawer()
    {
        if (_isHistoryDrawerAnimating)
            return;

        if (_isHistoryDrawerOpen)
            CloseHistoryDrawer();
        else
            OpenHistoryDrawer();
    }

    private void OpenHistoryDrawer()
    {
        if (_isHistoryDrawerOpen || _isHistoryDrawerAnimating || IsAppLocked())
            return;

        CollapseHeaderSearch();
        CloseHeaderNotificationPopup();
        CloseHeaderMenu();

        _isHistoryDrawerOpen = true;
        _isHistoryDrawerAnimating = true;
        HistoryDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        HistoryDrawerTransform.X = HistoryDrawer.Width + HistoryDrawer.Margin.Right;
        HistoryDrawer.Visibility = Visibility.Visible;
        PopupOverlay.IsHitTestVisible = true;
        ShowPopupOverlay();

        var animation = new DoubleAnimation(
            HistoryDrawerTransform.X,
            0,
            TimeSpan.FromMilliseconds(HistoryDrawerAnimationDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) =>
        {
            HistoryDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            HistoryDrawerTransform.X = 0;
            _isHistoryDrawerAnimating = false;
        };
        HistoryDrawerTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void CloseHistoryDrawer()
    {
        if (!_isHistoryDrawerOpen || _isHistoryDrawerAnimating)
            return;

        _isHistoryDrawerOpen = false;
        _isHistoryDrawerAnimating = true;
        var animation = new DoubleAnimation(
            0,
            HistoryDrawer.Width + HistoryDrawer.Margin.Right,
            TimeSpan.FromMilliseconds(HistoryDrawerAnimationDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        animation.Completed += (_, _) =>
        {
            HistoryDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            HistoryDrawerTransform.X = HistoryDrawer.Width + HistoryDrawer.Margin.Right;
            HistoryDrawer.Visibility = Visibility.Collapsed;
            PopupOverlay.IsHitTestVisible = false;
            _isHistoryDrawerAnimating = false;
            HidePopupOverlay();
        };
        HistoryDrawerTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void OnPopupOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isHistoryDrawerOpen)
            return;

        CloseHistoryDrawer();
        e.Handled = true;
    }

    private async void OnHistoryActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not BalloonCheckBox { DataContext: LogMemoryEntry entry } checkBox)
            return;

        var operation = entry.IsReverted ? "Reapply" : "Revert";
        var confirmed = FluxoMessageBox.Show(
            this,
            $"{operation} \"{entry.Description}\"?",
            "History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmed != MessageBoxResult.Yes)
        {
            checkBox.GetBindingExpression(BalloonCheckBox.IsCheckedProperty)?.UpdateTarget();
            return;
        }

        HistoryItemsControl.IsEnabled = false;
        try
        {
            if (!await _logMemoryManager.ToggleAsync(entry))
                checkBox.GetBindingExpression(BalloonCheckBox.IsCheckedProperty)?.UpdateTarget();
        }
        catch (Exception exception)
        {
            checkBox.GetBindingExpression(BalloonCheckBox.IsCheckedProperty)?.UpdateTarget();
            FloatingNotificationPublisher.LoggedFailure(
                _messenger,
                exception,
                $"{operation.ToLowerInvariant()} history action");
        }
        finally
        {
            HistoryItemsControl.IsEnabled = true;
        }
    }

    private async void OnHistoryUndoButtonClick(object sender, RoutedEventArgs e)
    {
        if (!IsSufficientFundsActionGateLocked())
            await UndoLogMemoryAsync();
    }

    private async void OnHistoryRedoButtonClick(object sender, RoutedEventArgs e)
    {
        if (!IsSufficientFundsActionGateLocked())
            await RedoLogMemoryAsync();
    }

    private async void OnRevertToHistoryItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not BalloonButton { DataContext: LogMemoryEntry entry } ||
            IsSufficientFundsActionGateLocked())
            return;

        HistoryItemsControl.IsEnabled = false;
        try
        {
            await _logMemoryManager.RevertToAsync(entry);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(
                _messenger,
                exception,
                "revert history to selected action");
        }
        finally
        {
            HistoryItemsControl.IsEnabled = true;
        }
    }

    private async Task UndoLogMemoryAsync()
    {
        try
        {
            await _logMemoryManager.UndoAsync();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "undo last action");
        }
    }

    private async Task RedoLogMemoryAsync()
    {
        try
        {
            await _logMemoryManager.RedoAsync();
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "redo last action");
        }
    }

    private static bool IsTextInputElementFocused()
    {
        return Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox;
    }

    private void OnHistoryManagerStateChanged(object? sender, EventArgs e)
    {
        UpdateHistoryAvailability();
    }

    private void UpdateHistoryAvailability()
    {
        HistoryUndoButton.IsEnabled = _logMemoryManager.CanUndo;
        HistoryRedoButton.IsEnabled = _logMemoryManager.CanRedo;
    }

    public void HidePopupOverlay()
    {
        var hostAction = _popupOverlayHandoffState.OnPopupHidden();
        if (hostAction != PopupOverlayHostAction.HideOverlay)
            return;

        HidePopupOverlayCore();
    }

    public void HidePopupOverlayForHandoff()
    {
        var hostAction = _popupOverlayHandoffState.OnPopupHiddenForHandoff(out var deferredHideGeneration);
        if (hostAction == PopupOverlayHostAction.HideOverlay)
        {
            HidePopupOverlayCore();
            return;
        }

        if (hostAction != PopupOverlayHostAction.DeferHide)
            return;

        SchedulePopupOverlayDeferredHide(deferredHideGeneration);
    }

    private void SchedulePopupOverlayDeferredHide(int deferredHideGeneration)
    {
        CancelPendingPopupOverlayDeferredHide();
        _popupOverlayDeferredHideTickHandler = (_, _) => OnPopupOverlayDeferredHideTimerTick(deferredHideGeneration);
        _popupOverlayDeferredHideTimer.Tick += _popupOverlayDeferredHideTickHandler;
        _popupOverlayDeferredHideTimer.Start();
    }

    private void OnPopupOverlayDeferredHideTimerTick(int deferredHideGeneration)
    {
        CancelPendingPopupOverlayDeferredHide();

        var hostAction = _popupOverlayHandoffState.ResolveDeferredHide(deferredHideGeneration);
        if (hostAction != PopupOverlayHostAction.HideOverlay)
            return;

        HidePopupOverlayCore();
    }

    private void CancelPendingPopupOverlayDeferredHide()
    {
        _popupOverlayDeferredHideTimer.Stop();
        if (_popupOverlayDeferredHideTickHandler is null)
            return;

        _popupOverlayDeferredHideTimer.Tick -= _popupOverlayDeferredHideTickHandler;
        _popupOverlayDeferredHideTickHandler = null;
    }

    private void HidePopupOverlayCore()
    {
        PopupOverlay.BeginAnimation(OpacityProperty, null);

        var fadeOut = new DoubleAnimation(PopupOverlay.Opacity, 0, TimeSpan.FromMilliseconds(FadeDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (_popupOverlayHandoffState.ActivePopupCount > 0)
                return;

            PopupOverlay.BeginAnimation(OpacityProperty, null);
            PopupOverlay.Opacity = 0;
            PopupOverlay.Visibility = Visibility.Collapsed;
            ClearPopupBlur();
        };
        PopupOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ApplyPopupBlur()
    {
        if (IsAppLocked())
        {
            ApplyAppLockBlur();
            return;
        }

        ContentGrid.Effect = CreatePopupBlurEffect();
        MainPageHost.Effect = CreatePopupBlurEffect();
        FloatingSideNavigationRail.Effect = CreatePopupBlurEffect();
    }

    private void ClearPopupBlur()
    {
        if (IsAppLocked())
        {
            ApplyAppLockBlur();
            return;
        }

        ContentGrid.Effect = null;
        MainPageHost.Effect = null;
        FloatingSideNavigationRail.Effect = null;
    }

    private static BlurEffect CreatePopupBlurEffect()
    {
        return new BlurEffect { Radius = 20, RenderingBias = RenderingBias.Performance };
    }

    private async Task ExecuteAccountSettingsActionAsync(AccountVM account,
        SettingsBatchAction action,
        bool requireDeleteConfirmation = false)
    {
        ArgumentNullException.ThrowIfNull(account);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var appData = scope.ServiceProvider.GetRequiredService<IAppDataService>();

            if (requireDeleteConfirmation)
            {
                var confirmationMessage = await AccountDeletionConfirmationHelper.BuildDeleteConfirmationMessageAsync(
                    appData,
                    account.Id,
                    account.Name);

                if (_dialogService.ShowWarning(confirmationMessage, "Settings", this, MessageBoxButton.YesNo) !=
                    MessageBoxResult.Yes)
                    return;
            }

            var settingsViewModel = scope.ServiceProvider.GetRequiredService<SettingsVM>();
            await settingsViewModel.LoadAsync();

            var result = await settingsViewModel.ExecuteAccountItemActionAsync(account.Id, action);
            if (!result.IsSuccess)
                _dialogService.ShowInformation(result.ErrorMessage, "Settings", this);
        }
        catch (Exception exception)
        {
            FloatingNotificationPublisher.LoggedFailure(_messenger, exception, "update account");
        }
    }

    private void OpenHeaderMenu(bool pinned)
    {
        _isHeaderMenuPinned = pinned || _isHeaderMenuPinned;
        _headerMenuCloseTimer.Stop();
        HeaderMenuPopup.IsOpen = true;
    }

    private void CloseHeaderMenu()
    {
        _headerMenuCloseTimer.Stop();
        _isHeaderMenuPinned = false;
        _isPointerOverHeaderMenuButton = false;
        _isPointerOverHeaderMenuPopup = false;
        HeaderMenuPopup.IsOpen = false;
    }

    private void CloseHeaderNotificationPopup()
    {
        HeaderNotificationPopup.IsOpen = false;
    }

    private void ScheduleHeaderMenuClose()
    {
        if (_isHeaderMenuPinned)
            return;

        _headerMenuCloseTimer.Stop();
        _headerMenuCloseTimer.Start();
    }

    private void OnHeaderMenuCloseTimerTick(object? sender, EventArgs e)
    {
        _headerMenuCloseTimer.Stop();

        if (_isHeaderMenuPinned || _isPointerOverHeaderMenuButton || _isPointerOverHeaderMenuPopup)
            return;

        HeaderMenuPopup.IsOpen = false;
    }

    private void OnWindowPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RecordAppAutoLockInputActivity();

        if (e.OriginalSource is not DependencyObject source)
            return;

        if (_isHeaderSearchExpanded &&
            !DependencyObjectTree.IsDescendantOf(source, HeaderSearchRegion) &&
            ShouldCollapseHeaderSearchOnExternalClick())
            CollapseHeaderSearch();

        if (HeaderNotificationPopup.IsOpen &&
            !DependencyObjectTree.IsDescendantOf(source, HeaderNotificationPanel) &&
            DependencyObjectTree.FindAncestor<BalloonButton>(source) != HeaderNotificationButton)
            CloseHeaderNotificationPopup();

        if (!_isHeaderMenuPinned)
            return;

        if (DependencyObjectTree.FindAncestor<BalloonButton>(source) == HeaderMenuButton)
            return;

        CloseHeaderMenu();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        CollapseHeaderSearch();
        CloseHeaderMenu();
        CloseHeaderNotificationPopup();
        StartAppAutoLockCountdown();
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        ResetAppAutoLockActivity();
    }

    private void OnWindowPreviewInputActivity(object sender, InputEventArgs e)
    {
        RecordAppAutoLockInputActivity();
    }

    private void OnAppAutoLockActiveDelayTimerTick(object? sender, EventArgs e)
    {
        _appAutoLockActiveDelayTimer.Stop();
        StartAppAutoLockCountdown();
    }

    private void OnAppAutoLockCountdownTimerTick(object? sender, EventArgs e)
    {
        _appAutoLockCountdownTimer.Stop();

        if (!CanAutoLockUi())
            return;

        if (IsWindowActiveForAutoLock())
        {
            ResetAppAutoLockActivity();
            return;
        }

        LockAppUiFromUser();
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainVM.IsAppLocked):
                RefreshAppLockVisualState();
                if (_mainVM.IsAppLocked)
                    StopAppAutoLockTimers();
                else
                    ResetAppAutoLockActivity();
                break;

            case nameof(MainVM.IsAppAutoLocked):
            case nameof(MainVM.AppAutoLockedInterval):
                ResetAppAutoLockActivity();
                break;
        }
    }

    private void RecordAppAutoLockInputActivity()
    {
        if (IsAppLocked())
            return;

        ResetAppAutoLockActivity();
    }

    private void ResetAppAutoLockActivity()
    {
        if (!CanAutoLockUi())
        {
            StopAppAutoLockTimers();
            return;
        }

        _appAutoLockCountdownTimer.Stop();
        _appAutoLockActiveDelayTimer.Stop();

        if (IsWindowActiveForAutoLock())
            _appAutoLockActiveDelayTimer.Start();
        else
            StartAppAutoLockCountdown();
    }

    private void StartAppAutoLockCountdown()
    {
        if (!CanAutoLockUi())
        {
            StopAppAutoLockTimers();
            return;
        }

        _appAutoLockActiveDelayTimer.Stop();
        _appAutoLockCountdownTimer.Stop();
        _appAutoLockCountdownTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _mainVM.AppAutoLockedInterval));
        _appAutoLockCountdownTimer.Start();
    }

    private bool CanAutoLockUi()
    {
        return _mainVM.IsAppAutoLocked && !_mainVM.IsAppLocked;
    }

    private bool IsWindowActiveForAutoLock()
    {
        return IsVisible && WindowState != WindowState.Minimized &&
               (IsActive || HasActiveOwnedWindow());
    }

    private bool HasActiveOwnedWindow()
    {
        foreach (Window ownedWindow in OwnedWindows)
            if (ownedWindow.IsVisible && ownedWindow.IsActive)
                return true;

        return false;
    }

    private void StopAppAutoLockTimers()
    {
        _appAutoLockActiveDelayTimer.Stop();
        _appAutoLockCountdownTimer.Stop();
    }

    private void LockAppUiFromUser()
    {
        if (IsAppLocked())
            return;

        CloseHeaderMenu();
        CloseHeaderNotificationPopup();
        CollapseHeaderSearch();
        _mainVM.LockUi();
        RefreshAppLockVisualState();
    }

    private void UnlockAppUiFromUser()
    {
        if (!IsAppLocked())
            return;

        if (!_mainVM.HasUiLockingPassword)
        {
            _mainVM.TryUnlockUi(null);
            RefreshAppLockVisualState();
            ResetAppAutoLockActivity();
            return;
        }

        _dialogService.ShowAppUnlock(_mainVM.TryUnlockUi, this);
        RefreshAppLockVisualState();
        if (!_mainVM.IsAppLocked)
            ResetAppAutoLockActivity();
    }

    private void RefreshAppLockVisualState()
    {
        if (HeaderAppLockButton is not null)
            HeaderAppLockButton.IsChecked = _mainVM.IsAppLocked;

        RefreshActivePageTitle();
        SetDashboardMainContentHitTestVisible(!_mainVM.IsAppLocked);

        if (_mainVM.IsAppLocked)
            ApplyAppLockBlur();
        else if (_popupOverlayHandoffState.ActivePopupCount <= 0)
            ClearAppLockBlur();
        else
            ApplyPopupBlur();
    }

    private void ApplyAppLockBlur()
    {
        ContentGrid.Effect = null;
        DashboardSpendingAmountGateContent.Effect = CreatePopupBlurEffect();
        MainPageHost.Effect = CreatePopupBlurEffect();
        FloatingSideNavigationRail.Effect = CreatePopupBlurEffect();
    }

    private void ClearAppLockBlur()
    {
        DashboardSpendingAmountGateContent.Effect = null;
        ContentGrid.Effect = null;
        MainPageHost.Effect = null;
        FloatingSideNavigationRail.Effect = null;
    }

    private void SetDashboardMainContentHitTestVisible(bool isHitTestVisible)
    {
        if (_dashboardPageView?.MainContentGrid is null)
            return;

        _dashboardPageView.MainContentGrid.IsHitTestVisible = isHitTestVisible;
    }

    private void RefreshActivePageTitle()
    {
        ActivePageTitle = _mainVM.IsAppLocked ? "Locked" : GetMainPageTitle(_activeMainPage);
    }

    private bool ShouldCollapseHeaderSearchOnExternalClick()
    {
        return _activeMainPage != MainPage.Ledger || string.IsNullOrWhiteSpace(HeaderSearchBox.Text);
    }

    private bool IsDashboardSpendingAmountGateLocked()
    {
        return _mainVM.IsDashboardSpendingAmountGateLocked;
    }

    private bool IsSufficientFundsActionGateLocked()
    {
        return _mainVM.IsAnyActionGateLocked;
    }

    private bool IsAppLocked()
    {
        return _mainVM.IsAppLocked;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
}
