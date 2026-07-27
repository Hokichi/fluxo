using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Fluxo.Core.Enums;
using Fluxo.Resources.Infrastructure;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups.Planning;
using Fluxo.Helpers.Behaviors;

namespace Fluxo.Views.Popups.Planning;

public partial class PlanningReportPopup : BasePopup
{
    private static readonly TimeSpan AllocationTransitionDuration = TimeSpan.FromMilliseconds(240);
    private static readonly IEasingFunction AllocationTransitionEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly TimeSpan AddRowScrollDuration = TimeSpan.FromMilliseconds(100);
    private static readonly IEasingFunction AddRowScrollEasing = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly HashSet<string> AllocationMetricPropertyNames =
    [
        nameof(PlanningReportVM.NeedsUsage),
        nameof(PlanningReportVM.WantsUsage),
        nameof(PlanningReportVM.InvestUsage),
        nameof(PlanningReportVM.NeedsOverflow),
        nameof(PlanningReportVM.WantsOverflow),
        nameof(PlanningReportVM.InvestOverflow),
        nameof(PlanningReportVM.NeedsUsagePercent),
        nameof(PlanningReportVM.WantsUsagePercent),
        nameof(PlanningReportVM.InvestUsagePercent)
    ];

    private readonly PlanningReportVM _viewModel;
    private bool _isAllocationAnimationQueued;

    public static readonly DependencyProperty AnimatedNeedsUsageProperty = DependencyProperty.Register(
        nameof(AnimatedNeedsUsage),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedWantsUsageProperty = DependencyProperty.Register(
        nameof(AnimatedWantsUsage),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedInvestUsageProperty = DependencyProperty.Register(
        nameof(AnimatedInvestUsage),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedNeedsOverflowProperty = DependencyProperty.Register(
        nameof(AnimatedNeedsOverflow),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedWantsOverflowProperty = DependencyProperty.Register(
        nameof(AnimatedWantsOverflow),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedInvestOverflowProperty = DependencyProperty.Register(
        nameof(AnimatedInvestOverflow),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedNeedsUsagePercentProperty = DependencyProperty.Register(
        nameof(AnimatedNeedsUsagePercent),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedWantsUsagePercentProperty = DependencyProperty.Register(
        nameof(AnimatedWantsUsagePercent),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public static readonly DependencyProperty AnimatedInvestUsagePercentProperty = DependencyProperty.Register(
        nameof(AnimatedInvestUsagePercent),
        typeof(double),
        typeof(PlanningReportPopup),
        new PropertyMetadata(0d));

    public PlanningReportPopup(PlanningReportVM viewModel)
    {
        AllocationCategories = CreateAllocationCategories();
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public IReadOnlyList<AllocationCategoryOption> AllocationCategories { get; }

    public double AnimatedNeedsUsage
    {
        get => (double)GetValue(AnimatedNeedsUsageProperty);
        set => SetValue(AnimatedNeedsUsageProperty, value);
    }

    public double AnimatedWantsUsage
    {
        get => (double)GetValue(AnimatedWantsUsageProperty);
        set => SetValue(AnimatedWantsUsageProperty, value);
    }

    public double AnimatedInvestUsage
    {
        get => (double)GetValue(AnimatedInvestUsageProperty);
        set => SetValue(AnimatedInvestUsageProperty, value);
    }

    public double AnimatedNeedsOverflow
    {
        get => (double)GetValue(AnimatedNeedsOverflowProperty);
        set => SetValue(AnimatedNeedsOverflowProperty, value);
    }

    public double AnimatedWantsOverflow
    {
        get => (double)GetValue(AnimatedWantsOverflowProperty);
        set => SetValue(AnimatedWantsOverflowProperty, value);
    }

    public double AnimatedInvestOverflow
    {
        get => (double)GetValue(AnimatedInvestOverflowProperty);
        set => SetValue(AnimatedInvestOverflowProperty, value);
    }

    public double AnimatedNeedsUsagePercent
    {
        get => (double)GetValue(AnimatedNeedsUsagePercentProperty);
        set => SetValue(AnimatedNeedsUsagePercentProperty, value);
    }

    public double AnimatedWantsUsagePercent
    {
        get => (double)GetValue(AnimatedWantsUsagePercentProperty);
        set => SetValue(AnimatedWantsUsagePercentProperty, value);
    }

    public double AnimatedInvestUsagePercent
    {
        get => (double)GetValue(AnimatedInvestUsagePercentProperty);
        set => SetValue(AnimatedInvestUsagePercentProperty, value);
    }

    private static IReadOnlyList<AllocationCategoryOption> CreateAllocationCategories()
    {
        return
        [
            new AllocationCategoryOption(ExpenseCategory.Needs, "Needs"),
            new AllocationCategoryOption(ExpenseCategory.Wants, "Wants"),
            new AllocationCategoryOption(ExpenseCategory.Savings, "Invest")
        ];
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        StopAllocationAnimations();
        SetAnimatedAllocationValues(
            needsUsage: 0d,
            wantsUsage: 0d,
            investUsage: 0d,
            needsOverflow: 0d,
            wantsOverflow: 0d,
            investOverflow: 0d,
            needsUsagePercent: 0d,
            wantsUsagePercent: 0d,
            investUsagePercent: 0d);
        AnimateAllocationMetricsToCurrentTargets();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!AllocationMetricPropertyNames.Contains(e.PropertyName ?? string.Empty))
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(QueueAllocationAnimation);
            return;
        }

        QueueAllocationAnimation();
    }

    private void QueueAllocationAnimation()
    {
        if (_isAllocationAnimationQueued)
            return;

        _isAllocationAnimationQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _isAllocationAnimationQueued = false;
            if (!IsLoaded)
                return;

            AnimateAllocationMetricsToCurrentTargets();
        }, DispatcherPriority.Render);
    }

    private void AnimateAllocationMetricsToCurrentTargets()
    {
        AnimateValue(AnimatedNeedsUsageProperty, _viewModel.NeedsUsage);
        AnimateValue(AnimatedWantsUsageProperty, _viewModel.WantsUsage);
        AnimateValue(AnimatedInvestUsageProperty, _viewModel.InvestUsage);
        AnimateValue(AnimatedNeedsOverflowProperty, _viewModel.NeedsOverflow);
        AnimateValue(AnimatedWantsOverflowProperty, _viewModel.WantsOverflow);
        AnimateValue(AnimatedInvestOverflowProperty, _viewModel.InvestOverflow);
        AnimateValue(AnimatedNeedsUsagePercentProperty, _viewModel.NeedsUsagePercent);
        AnimateValue(AnimatedWantsUsagePercentProperty, _viewModel.WantsUsagePercent);
        AnimateValue(AnimatedInvestUsagePercentProperty, _viewModel.InvestUsagePercent);
    }

    private void AnimateValue(DependencyProperty property, double target)
    {
        BeginAnimation(property, null);
        var current = (double)GetValue(property);

        if (Math.Abs(current - target) < 0.0001d)
        {
            SetValue(property, target);
            return;
        }

        var animation = new DoubleAnimation
        {
            From = current,
            To = target,
            Duration = AllocationTransitionDuration,
            EasingFunction = AllocationTransitionEasing,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) => SetValue(property, target);
        BeginAnimation(property, animation);
    }

    private void StopAllocationAnimations()
    {
        BeginAnimation(AnimatedNeedsUsageProperty, null);
        BeginAnimation(AnimatedWantsUsageProperty, null);
        BeginAnimation(AnimatedInvestUsageProperty, null);
        BeginAnimation(AnimatedNeedsOverflowProperty, null);
        BeginAnimation(AnimatedWantsOverflowProperty, null);
        BeginAnimation(AnimatedInvestOverflowProperty, null);
        BeginAnimation(AnimatedNeedsUsagePercentProperty, null);
        BeginAnimation(AnimatedWantsUsagePercentProperty, null);
        BeginAnimation(AnimatedInvestUsagePercentProperty, null);
    }

    private void SetAnimatedAllocationValues(
        double needsUsage,
        double wantsUsage,
        double investUsage,
        double needsOverflow,
        double wantsOverflow,
        double investOverflow,
        double needsUsagePercent,
        double wantsUsagePercent,
        double investUsagePercent)
    {
        AnimatedNeedsUsage = needsUsage;
        AnimatedWantsUsage = wantsUsage;
        AnimatedInvestUsage = investUsage;
        AnimatedNeedsOverflow = needsOverflow;
        AnimatedWantsOverflow = wantsOverflow;
        AnimatedInvestOverflow = investOverflow;
        AnimatedNeedsUsagePercent = needsUsagePercent;
        AnimatedWantsUsagePercent = wantsUsagePercent;
        AnimatedInvestUsagePercent = investUsagePercent;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (IsFocusedWithin(ExpenseScrollViewer))
                AddExpenseRow();
            else if (IsFocusedWithin(IncomeScrollViewer))
                AddIncomeRow();

            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnAddIncomeClick(object sender, RoutedEventArgs e)
    {
        AddIncomeRow();
    }

    private async void OnLoadRecurringIncomesClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadRecurringIncomesAsync();
        QueueScrollToBottom(IncomeScrollViewer);
    }

    private void OnRemoveIncomeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TransactionVM income })
            _viewModel.RemoveIncome(income);
    }

    private void OnAddExpenseClick(object sender, RoutedEventArgs e)
    {
        AddExpenseRow();
    }

    private async void OnLoadRecurringExpensesClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadRecurringExpensesAsync();
        QueueScrollToBottom(ExpenseScrollViewer);
    }

    private void AddIncomeRow()
    {
        _viewModel.AddIncome(new TransactionVM { OccurredOn = DateTime.Now });
        QueueScrollToBottom(IncomeScrollViewer);
    }

    private void AddExpenseRow()
    {
        _viewModel.AddExpense(new TransactionVM
        {
            ExpenseCategory = ExpenseCategory.Needs
        });

        QueueScrollToBottom(ExpenseScrollViewer);
    }

    private static bool IsFocusedWithin(DependencyObject? container)
    {
        if (container is null || Keyboard.FocusedElement is not DependencyObject focusedElement)
            return false;

        return DependencyObjectTree.IsDescendantOf(focusedElement, container);
    }

    private void OnRemoveExpenseClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TransactionVM expense })
            _viewModel.RemoveExpense(expense);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopAllocationAnimations();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Loaded -= OnLoaded;
        Closed -= OnClosed;
        _viewModel.Dispose();
    }

    private void QueueScrollToBottom(ScrollViewer? scrollViewer)
    {
        if (scrollViewer is null)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            scrollViewer.UpdateLayout();
            ScrollViewerAnimationBehavior.AnimateToVerticalOffset(
                scrollViewer,
                scrollViewer.ScrollableHeight,
                AddRowScrollDuration,
                AddRowScrollEasing);
        }, DispatcherPriority.Loaded);
    }

    public sealed class AllocationCategoryOption(ExpenseCategory value, string label)
    {
        public ExpenseCategory Value { get; } = value;
        public string Label { get; } = label;
    }
}
