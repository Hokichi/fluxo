using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Fluxo.Resources.CustomControls;

public enum PopupMode
{
    None,
    Update,
    Persist,
    Modify,
    Navigate,
    Functional
}

[TemplatePart(Name = "PART_ContentRoot", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_PopupOverlay", Type = typeof(UIElement))]
public class BasePopup : Window, IPopupHost
{
    private const int OverlayAnimDuration = 200; // ms
    private const int PopupAnimDuration = 180; // ms
    private const double RecenterDeltaTolerance = 0.1;

    // --- PopupTitle ---
    public static readonly DependencyProperty PopupTitleProperty =
        DependencyProperty.Register(nameof(PopupTitle), typeof(string), typeof(BasePopup),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PopupTitleContentProperty =
        DependencyProperty.Register(nameof(PopupTitleContent), typeof(object), typeof(BasePopup),
            new PropertyMetadata(null));

    // --- Footer mode ---
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(PopupMode), typeof(BasePopup),
            new PropertyMetadata(PopupMode.None, OnModeChanged));

    public static readonly DependencyProperty CanSkipProperty =
        DependencyProperty.Register(nameof(CanSkip), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanGoNextProperty =
        DependencyProperty.Register(nameof(CanGoNext), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CanFinishProperty =
        DependencyProperty.Register(nameof(CanFinish), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CanToggleBulkProperty =
        DependencyProperty.Register(nameof(CanToggleBulk), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanEditProperty =
        DependencyProperty.Register(nameof(CanEdit), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanDeleteProperty =
        DependencyProperty.Register(nameof(CanDelete), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanCloneProperty =
        DependencyProperty.Register(nameof(CanClone), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CurrentStepProperty =
        DependencyProperty.Register(nameof(CurrentStep), typeof(int), typeof(BasePopup),
            new PropertyMetadata(1, OnStepChanged));

    public static readonly DependencyProperty StepCountProperty =
        DependencyProperty.Register(nameof(StepCount), typeof(int), typeof(BasePopup),
            new PropertyMetadata(1, OnStepChanged));

    private static readonly DependencyPropertyKey IsFirstStepPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsFirstStep), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsFirstStepProperty = IsFirstStepPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsLastStepPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsLastStep), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsLastStepProperty = IsLastStepPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsSaveButtonEnabledProperty =
        DependencyProperty.Register(nameof(IsSaveButtonEnabled), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowCloseButtonProperty =
        DependencyProperty.Register(nameof(ShowCloseButton), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    public static readonly DependencyProperty ShowHeaderProperty =
        DependencyProperty.Register(nameof(ShowHeader), typeof(bool), typeof(BasePopup),
            new PropertyMetadata(true));

    private IPopupHost? _popupHost;
    private FrameworkElement? _contentRoot;
    private UIElement? _popupOverlay;
    private BalloonCheckBox? _bulkInsertCheckBox;
    private readonly PopupOverlayHandoffState _popupOverlayHandoffState = new();

    private readonly DispatcherTimer _popupOverlayDeferredHideTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(OverlayAnimDuration)
    };

    private EventHandler? _popupOverlayDeferredHideTickHandler;
    private bool _isAnimatingClose;
    private bool _isClosingForPopupHandoff;
    private bool _hasRoutedOwnerOverlayHide;
    private bool _isPopupOverlayHandoffPending;
    private Window? _previousApplicationMainWindow;
    private bool _isApplicationMainWindow;
    private bool _isSynchronizingBulkInsertToggle;

    static BasePopup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BasePopup),
            new FrameworkPropertyMetadata(typeof(BasePopup)));
    }

    public BasePopup()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Opacity = 0;

        // Implicit styles in App.Resources don't auto-apply to derived types,
        // so bind the Style explicitly to the BasePopup resource key.
        SetResourceReference(StyleProperty, typeof(BasePopup));

        Loaded += OnLoaded;
        Closed += OnClosed;
        SizeChanged += OnSizeChanged;
    }

    public string PopupTitle
    {
        get => (string)GetValue(PopupTitleProperty);
        set => SetValue(PopupTitleProperty, value);
    }

    public object? PopupTitleContent
    {
        get => GetValue(PopupTitleContentProperty);
        set => SetValue(PopupTitleContentProperty, value);
    }

    public PopupMode Mode
    {
        get => (PopupMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public bool CanSkip
    {
        get => (bool)GetValue(CanSkipProperty);
        set => SetValue(CanSkipProperty, value);
    }

    public bool CanGoNext
    {
        get => (bool)GetValue(CanGoNextProperty);
        set => SetValue(CanGoNextProperty, value);
    }

    public bool CanFinish
    {
        get => (bool)GetValue(CanFinishProperty);
        set => SetValue(CanFinishProperty, value);
    }

    public bool CanToggleBulk
    {
        get => (bool)GetValue(CanToggleBulkProperty);
        set => SetValue(CanToggleBulkProperty, value);
    }

    public event EventHandler<BulkInsertUncheckedEventArgs>? BulkInsertUnchecked;
    public event EventHandler? BulkInsertChecked;

    public bool CanEdit
    {
        get => (bool)GetValue(CanEditProperty);
        set => SetValue(CanEditProperty, value);
    }

    public bool CanDelete
    {
        get => (bool)GetValue(CanDeleteProperty);
        set => SetValue(CanDeleteProperty, value);
    }

    public bool CanClone
    {
        get => (bool)GetValue(CanCloneProperty);
        set => SetValue(CanCloneProperty, value);
    }

    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    public int StepCount
    {
        get => (int)GetValue(StepCountProperty);
        set => SetValue(StepCountProperty, value);
    }

    public bool IsFirstStep => (bool)GetValue(IsFirstStepProperty);

    public bool IsLastStep => (bool)GetValue(IsLastStepProperty);

    public bool IsSaveButtonEnabled
    {
        get => (bool)GetValue(IsSaveButtonEnabledProperty);
        set => SetValue(IsSaveButtonEnabledProperty, value);
    }

    public bool ShowCloseButton
    {
        get => (bool)GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        WireButton("PART_CloseButton", _ => OnCloseButtonClick());
        WireButton("PART_SaveButton", _ => OnSaveButtonClick());
        WireButton("PART_ApplyButton", _ => OnApplyButtonClick());
        WireButton("PART_CancelButton", _ => OnDiscardButtonClick());
        WireButton("PART_DiscardButton", _ => OnDiscardButtonClick());
        WireButton("PART_BackButton", _ => OnBackButtonClick());
        WireButton("PART_NextButton", _ => OnNextButtonClick());
        WireButton("PART_FinishButton", _ => OnFinishButtonClick());
        WireButton("PART_SkipButton", _ => OnSkipButtonClick());
        WireButton("PART_EditButton", _ => OnEditButtonClick());
        WireButton("PART_DeleteButton", _ => OnDeleteButtonClick());
        WireButton("PART_CloneButton", _ => OnCloneButtonClick());
        WireBulkInsertToggle();

        _contentRoot = GetTemplateChild("PART_ContentRoot") as FrameworkElement;
        _popupOverlay = GetTemplateChild("PART_PopupOverlay") as UIElement;
    }

    private void WireButton(string partName, Action<RoutedEventArgs> handler)
    {
        if (GetTemplateChild(partName) is ButtonBase btn)
            btn.Click += (_, e) => handler(e);
    }

    private void WireBulkInsertToggle()
    {
        if (GetTemplateChild("PART_BulkInsertCheckBox") is not BalloonCheckBox checkBox)
            return;

        _bulkInsertCheckBox = checkBox;
        checkBox.Checked += OnBulkInsertChecked;
        checkBox.Unchecked += OnBulkInsertUnchecked;
        SyncBulkInsertToggle();
    }

    private void OnBulkInsertChecked(object sender, RoutedEventArgs e)
    {
        if (!_isSynchronizingBulkInsertToggle)
        {
            SetCurrentValue(ModeProperty, PopupMode.Navigate);
            BulkInsertChecked?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnBulkInsertUnchecked(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingBulkInsertToggle)
            return;

        var eventArgs = new BulkInsertUncheckedEventArgs();
        BulkInsertUnchecked?.Invoke(this, eventArgs);
        if (eventArgs.ShouldSwitchToSaveOnly)
            SetCurrentValue(ModeProperty, PopupMode.Persist);
        else if (_bulkInsertCheckBox is not null)
            _bulkInsertCheckBox.IsChecked = true;
    }

    // Virtual button handlers (override in child popups)

    protected virtual void OnCloseButtonClick()
    {
        Close();
    }

    protected void CloseForPopupHandoff()
    {
        _isClosingForPopupHandoff = true;
        Close();
    }

    protected virtual void OnSaveButtonClick()
    {
    }

    protected virtual void OnApplyButtonClick()
    {
    }

    protected virtual void OnEditButtonClick()
    {
    }

    protected virtual void OnDeleteButtonClick()
    {
    }

    protected virtual void OnCloneButtonClick()
    {
    }

    protected virtual void OnDiscardButtonClick() => OnCloseButtonClick();

    protected virtual void OnBackButtonClick()
    {
    }

    protected virtual void OnNextButtonClick()
    {
    }

    protected virtual void OnFinishButtonClick()
    {
    }

    protected virtual void OnSkipButtonClick()
    {
    }

    protected virtual bool ShouldBubbleApplicationClose => true;

    // Keyboard shortcuts

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (TryHandlePopupShortcut(e.Key, Keyboard.Modifiers))
            e.Handled = true;
    }

    protected bool TryHandlePopupShortcut(Key key, ModifierKeys modifiers)
    {
        switch (key)
        {
            case Key.Escape:
                if (Mode is PopupMode.Update or PopupMode.Modify)
                    OnDiscardButtonClick();
                else
                    OnCloseButtonClick();
                return true;

            case Key.Enter when modifiers == ModifierKeys.Control:
                if (Mode == PopupMode.Navigate && CanSkip)
                {
                    OnSkipButtonClick();
                    return true;
                }

                return false;

            case Key.Enter when modifiers == ModifierKeys.None:
                if (Mode == PopupMode.Update)
                {
                    OnApplyButtonClick();
                    return true;
                }

                if (Mode is (PopupMode.Persist or PopupMode.Modify) && IsSaveButtonEnabled)
                {
                    OnSaveButtonClick();
                    return true;
                }

                if (Mode == PopupMode.Navigate)
                {
                    if (IsLastStep && CanFinish)
                    {
                        OnFinishButtonClick();
                        return true;
                    }

                    if (!IsLastStep && CanGoNext)
                    {
                        OnNextButtonClick();
                        return true;
                    }

                    return false;
                }

                return false;

            case Key.Back when modifiers == ModifierKeys.None:
                if (Mode == PopupMode.Navigate && !IsFirstStep)
                {
                    OnBackButtonClick();
                    return true;
                }

                return false;
        }

        return false;
    }

    private static void OnStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var popup = (BasePopup)d;
        popup.SetValue(IsFirstStepPropertyKey, popup.CurrentStep <= 1);
        popup.SetValue(IsLastStepPropertyKey, popup.StepCount <= 1 || popup.CurrentStep >= popup.StepCount);
    }

    private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BasePopup)d).SyncBulkInsertToggle();
    }

    private void SyncBulkInsertToggle()
    {
        if (_bulkInsertCheckBox is null)
            return;

        _isSynchronizingBulkInsertToggle = true;
        _bulkInsertCheckBox.IsChecked = Mode == PopupMode.Navigate;
        _isSynchronizingBulkInsertToggle = false;
    }

    // Overlay & blur on owner

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _popupHost = Owner as IPopupHost;
        _popupHost?.ShowPopupOverlay();
        BecomeApplicationMainWindow();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(PopupAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        fadeIn.Completed += (_, _) => OnFadeInCompleted();
        BeginAnimation(OpacityProperty, fadeIn);
    }

    protected virtual void OnFadeInCompleted()
    {
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || _isAnimatingClose)
            return;

        if (!e.WidthChanged && !e.HeightChanged)
            return;

        if (e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0)
            return;

        RecenterAnimated(e.PreviousSize, e.NewSize);
    }

    private void RecenterAnimated(Size previousSize, Size newSize)
    {
        var (targetLeft, targetTop) = TryGetOwnerCenteredPosition() ??
                                      GetCenterPreservingPosition(previousSize, newSize);

        if (double.IsNaN(targetLeft) || double.IsInfinity(targetLeft) ||
            double.IsNaN(targetTop) || double.IsInfinity(targetTop))
            return;

        var leftDelta = Math.Abs(targetLeft - Left);
        var topDelta = Math.Abs(targetTop - Top);

        if (leftDelta < RecenterDeltaTolerance && topDelta < RecenterDeltaTolerance)
            return;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(PopupAnimDuration);

        if (leftDelta >= RecenterDeltaTolerance)
        {
            BeginAnimation(LeftProperty, new DoubleAnimation
            {
                To = targetLeft,
                Duration = duration,
                EasingFunction = easing
            });
        }

        if (topDelta >= RecenterDeltaTolerance)
        {
            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = targetTop,
                Duration = duration,
                EasingFunction = easing
            });
        }
    }

    private (double Left, double Top)? TryGetOwnerCenteredPosition()
    {
        if (Owner is null || !Owner.IsVisible)
            return null;

        var ownerWidth = Owner.ActualWidth > 0 ? Owner.ActualWidth : Owner.Width;
        var ownerHeight = Owner.ActualHeight > 0 ? Owner.ActualHeight : Owner.Height;

        if (double.IsNaN(ownerWidth) || double.IsNaN(ownerHeight) ||
            double.IsInfinity(ownerWidth) || double.IsInfinity(ownerHeight))
            return null;

        var centeredLeft = Owner.Left + ((ownerWidth - ActualWidth) / 2d);
        var centeredTop = Owner.Top + ((ownerHeight - ActualHeight) / 2d);
        return (centeredLeft, centeredTop);
    }

    private (double Left, double Top) GetCenterPreservingPosition(Size previousSize, Size newSize)
    {
        var deltaWidth = newSize.Width - previousSize.Width;
        var deltaHeight = newSize.Height - previousSize.Height;

        var centeredLeft = Left - (deltaWidth / 2d);
        var centeredTop = Top - (deltaHeight / 2d);
        return (centeredLeft, centeredTop);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Let popup-specific Closing handlers run first.
        base.OnClosing(e);

        // Respect cancellation from popup-specific logic (e.g. unsaved-change prompts).
        if (e.Cancel)
        {
            _isClosingForPopupHandoff = false;
            return;
        }

        // Preserve modal dialog results (true/false) by letting WPF finish the close immediately.
        if (DialogResult.HasValue)
        {
            RestoreApplicationMainWindow();
            RouteOwnerPopupOverlayHide();
            return;
        }

        if (_isAnimatingClose)
        {
            RestoreApplicationMainWindow();
            return;
        }

        e.Cancel = true;
        _isAnimatingClose = true;

        RouteOwnerPopupOverlayHide();

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(PopupAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void BecomeApplicationMainWindow()
    {
        if (!ShouldBubbleApplicationClose || Application.Current is not { } application ||
            !application.Dispatcher.CheckAccess() ||
            ReferenceEquals(application.MainWindow, this))
            return;

        _previousApplicationMainWindow = application.MainWindow;
        application.MainWindow = this;
        _isApplicationMainWindow = true;
    }

    private void RestoreApplicationMainWindow()
    {
        if (!_isApplicationMainWindow || Application.Current is not { } application ||
            !application.Dispatcher.CheckAccess() ||
            !ReferenceEquals(application.MainWindow, this))
            return;

        application.MainWindow = _previousApplicationMainWindow;
        _isApplicationMainWindow = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_isAnimatingClose)
            RouteOwnerPopupOverlayHide();

        CancelPendingPopupOverlayDeferredHide();
    }

    private void RouteOwnerPopupOverlayHide()
    {
        if (_hasRoutedOwnerOverlayHide || _popupHost is null)
            return;

        if (_isClosingForPopupHandoff)
            _popupHost.HidePopupOverlayForHandoff();
        else
            _popupHost.HidePopupOverlay();

        _hasRoutedOwnerOverlayHide = true;
    }

    public void BeginPopupHandoff()
    {
        if (_popupOverlayHandoffState.ActivePopupCount <= 0)
            return;

        _isPopupOverlayHandoffPending = true;
    }

    public void ShowPopupOverlay()
    {
        if (_contentRoot is null || _popupOverlay is null)
            return;

        CancelPendingPopupOverlayDeferredHide();

        var hostAction = _popupOverlayHandoffState.OnPopupShown();
        if (hostAction != PopupOverlayHostAction.ShowOverlay)
            return;

        _contentRoot.Effect = new BlurEffect { Radius = 20, RenderingBias = RenderingBias.Performance };

        _popupOverlay.BeginAnimation(OpacityProperty, null);
        _popupOverlay.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(_popupOverlay.Opacity, 0.5, TimeSpan.FromMilliseconds(OverlayAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        _popupOverlay.BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HidePopupOverlay()
    {
        if (_isPopupOverlayHandoffPending)
        {
            _isPopupOverlayHandoffPending = false;
            HidePopupOverlayForHandoff();
            return;
        }

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
        if (_contentRoot is null || _popupOverlay is null)
            return;

        _contentRoot.Effect = null;
        _popupOverlay.BeginAnimation(OpacityProperty, null);

        var fadeOut = new DoubleAnimation(_popupOverlay.Opacity, 0, TimeSpan.FromMilliseconds(OverlayAnimDuration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (_popupOverlayHandoffState.ActivePopupCount > 0)
                return;

            _popupOverlay.BeginAnimation(OpacityProperty, null);
            _popupOverlay.Opacity = 0;
            _popupOverlay.Visibility = Visibility.Collapsed;
        };
        _popupOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }
}
