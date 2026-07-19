using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using SavingGoalsPanelVM = Fluxo.ViewModels.Shell.Main.SavingGoalsPanelVM;

namespace Fluxo.Views.Shell.Main.Sections;

public partial class SavingGoalsPanel : UserControl
{
    private const double SwipeDistanceThreshold = 48;
    private static readonly TimeSpan GoalProgressTransitionDuration = TimeSpan.FromMilliseconds(300);
    private static readonly IEasingFunction GoalTransitionEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private bool _isAnimating;
    private bool _isSwipeTracking;
    private Point _swipeStartPoint;
    private SavingGoalVM? _displayedGoal;
    private SavingGoalVM? _trackedGoalForProgress;
    private SavingGoalsPanelVM? _viewModel;

    public static readonly DependencyProperty AnimatedProgressRatioProperty = DependencyProperty.Register(
        nameof(AnimatedProgressRatio),
        typeof(double),
        typeof(SavingGoalsPanel),
        new PropertyMetadata(0d));

    public double AnimatedProgressRatio
    {
        get => (double)GetValue(AnimatedProgressRatioProperty);
        set => SetValue(AnimatedProgressRatioProperty, value);
    }

    public SavingGoalsPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
            AttachViewModel(DataContext as SavingGoalsPanelVM);

        SyncCurrentGoalWithoutAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelSwipeTracking();
        DetachViewModel();
        AttachDisplayedGoalProgressTracking(null);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(e.OldValue, e.NewValue))
            return;

        DetachViewModel();
        AttachViewModel(e.NewValue as SavingGoalsPanelVM);
        SyncCurrentGoalWithoutAnimation();
    }

    private void AttachViewModel(SavingGoalsPanelVM? viewModel)
    {
        _viewModel = viewModel;
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModel()
    {
        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
            return;

        if (e.PropertyName is nameof(SavingGoalsPanelVM.CurrentGoal) or nameof(SavingGoalsPanelVM.HasSavingGoals))
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(SyncOrAnimateCurrentGoal);
                return;
            }

            SyncOrAnimateCurrentGoal();
        }
    }

    private void OnNavigatePreviousClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _isAnimating || !_viewModel.HasMultipleSavingGoals)
            return;

        _viewModel.NavigatePrevious();
    }

    private void OnNavigateNextClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _isAnimating || !_viewModel.HasMultipleSavingGoals)
            return;

        _viewModel.NavigateNext();
    }

    private void OnAddSavingGoalClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is global::Fluxo.Views.Shell.Main.MainWindow mainWindow)
            mainWindow.OpenAddSavingGoalPopup();
    }

    private void OnEditSavingGoalClick(object sender, RoutedEventArgs e)
    {
        var goal = _viewModel?.CurrentGoal;
        if (goal is null ||
            Window.GetWindow(this) is not global::Fluxo.Views.Shell.Main.MainWindow mainWindow)
            return;

        mainWindow.OpenEditSavingGoalPopup(goal.Id);
    }

    private void OnAddGoalFundsClick(object sender, RoutedEventArgs e)
    {
        var goal = _viewModel?.CurrentGoal;
        if (goal is null ||
            Window.GetWindow(this) is not global::Fluxo.Views.Shell.Main.MainWindow mainWindow)
            return;

        mainWindow.OpenAddNewTransactionPopup(new TransactionPopupDraft(
            false,
            string.Empty,
            0m,
            null,
            DateTime.Today,
            string.Empty,
            null,
            null,
            true,
            goal.Id,
            LockTransactionType: true));
    }

    private void OnCarouselViewportPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel is null || _isAnimating || !_viewModel.HasMultipleSavingGoals)
            return;

        _isSwipeTracking = true;
        _swipeStartPoint = e.GetPosition(CarouselViewport);
        CarouselViewport.CaptureMouse();
    }

    private void OnCarouselViewportPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isSwipeTracking || _viewModel is null || _isAnimating || !_viewModel.HasMultipleSavingGoals)
            return;

        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            CancelSwipeTracking();
    }

    private void OnCarouselViewportPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_isSwipeTracking)
            return;

        var endPoint = e.GetPosition(CarouselViewport);
        CompleteSwipeTracking(endPoint, e);
    }

    private void OnCarouselViewportLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _isSwipeTracking = false;
    }

    private void SyncOrAnimateCurrentGoal()
    {
        if (_viewModel is null)
        {
            SyncCurrentGoalWithoutAnimation();
            return;
        }

        var incomingGoal = _viewModel.CurrentGoal;

        if (_isAnimating ||
            _displayedGoal is null ||
            incomingGoal is null ||
            ReferenceEquals(_displayedGoal, incomingGoal) ||
            _viewModel.NavigationDirection == 0 ||
            !_viewModel.HasMultipleSavingGoals)
        {
            SyncCurrentGoalWithoutAnimation();
            return;
        }

        RunGoalProgressTransition(_displayedGoal, incomingGoal);
    }

    private void SyncCurrentGoalWithoutAnimation()
    {
        _isAnimating = false;
        BeginAnimation(AnimatedProgressRatioProperty, null);
        _displayedGoal = _viewModel?.CurrentGoal;
        AttachDisplayedGoalProgressTracking(_displayedGoal);
        AnimatedProgressRatio = _displayedGoal is null ? 0d : (double)_displayedGoal.ProgressRatio;
        CurrentGoalPresenter.Content = _displayedGoal;
    }

    private void RunGoalProgressTransition(SavingGoalVM outgoingGoal, SavingGoalVM incomingGoal)
    {
        _isAnimating = true;

        var outgoingProgressRatio = (double)outgoingGoal.ProgressRatio;
        var incomingProgressRatio = (double)incomingGoal.ProgressRatio;

        CurrentGoalPresenter.Content = incomingGoal;

        BeginAnimation(AnimatedProgressRatioProperty, null);
        AnimatedProgressRatio = incomingProgressRatio;

        var progressAnimation = new DoubleAnimation
        {
            From = outgoingProgressRatio,
            To = incomingProgressRatio,
            Duration = GoalProgressTransitionDuration,
            EasingFunction = GoalTransitionEasing,
            FillBehavior = FillBehavior.Stop
        };

        progressAnimation.Completed += (_, _) =>
        {
            _isAnimating = false;
            _displayedGoal = incomingGoal;
            AttachDisplayedGoalProgressTracking(_displayedGoal);

            BeginAnimation(AnimatedProgressRatioProperty, null);
            AnimatedProgressRatio = incomingProgressRatio;
            CurrentGoalPresenter.Content = incomingGoal;
        };

        BeginAnimation(AnimatedProgressRatioProperty, progressAnimation);
    }

    private void AttachDisplayedGoalProgressTracking(SavingGoalVM? goal)
    {
        if (ReferenceEquals(_trackedGoalForProgress, goal))
            return;

        if (_trackedGoalForProgress is not null)
            _trackedGoalForProgress.PropertyChanged -= OnTrackedGoalPropertyChanged;

        _trackedGoalForProgress = goal;

        if (_trackedGoalForProgress is not null)
            _trackedGoalForProgress.PropertyChanged += OnTrackedGoalPropertyChanged;
    }

    private void OnTrackedGoalPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SavingGoalVM.ProgressRatio) || _isAnimating)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateAnimatedProgressFromDisplayedGoal);
            return;
        }

        UpdateAnimatedProgressFromDisplayedGoal();
    }

    private void UpdateAnimatedProgressFromDisplayedGoal()
    {
        BeginAnimation(AnimatedProgressRatioProperty, null);
        AnimatedProgressRatio = _displayedGoal is null ? 0d : (double)_displayedGoal.ProgressRatio;
    }

    private void CompleteSwipeTracking(Point endPoint, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isSwipeTracking = false;

        if (CarouselViewport.IsMouseCaptured)
            CarouselViewport.ReleaseMouseCapture();

        if (_viewModel is null || _isAnimating || !_viewModel.HasMultipleSavingGoals)
            return;

        var horizontalDistance = endPoint.X - _swipeStartPoint.X;
        var verticalDistance = endPoint.Y - _swipeStartPoint.Y;
        var absHorizontalDistance = Math.Abs(horizontalDistance);
        var absVerticalDistance = Math.Abs(verticalDistance);

        if (absHorizontalDistance < SwipeDistanceThreshold || absHorizontalDistance <= absVerticalDistance)
            return;

        if (horizontalDistance > 0)
            _viewModel.NavigatePrevious();
        else
            _viewModel.NavigateNext();

        e.Handled = true;
    }

    private void CancelSwipeTracking()
    {
        _isSwipeTracking = false;

        if (CarouselViewport.IsMouseCaptured)
            CarouselViewport.ReleaseMouseCapture();
    }
}
