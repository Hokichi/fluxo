using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Fluxo.Resources.Infrastructure;
using Fluxo.Services.Logging;

namespace Fluxo.Helpers.Behaviors;

public static class ScrollOnDemandBehavior
{
    private const double PreloadThreshold = 100d;

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(ScrollOnDemandState),
            typeof(ScrollOnDemandBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollOnDemandBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty LoadMoreCommandProperty =
        DependencyProperty.RegisterAttached(
            "LoadMoreCommand",
            typeof(ICommand),
            typeof(ScrollOnDemandBehavior),
            new PropertyMetadata(null, OnTriggerPropertyChanged));

    public static readonly DependencyProperty HasMoreItemsProperty =
        DependencyProperty.RegisterAttached(
            "HasMoreItems",
            typeof(bool),
            typeof(ScrollOnDemandBehavior),
            new PropertyMetadata(false, OnTriggerPropertyChanged));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.RegisterAttached(
            "IsLoading",
            typeof(bool),
            typeof(ScrollOnDemandBehavior),
            new PropertyMetadata(false, OnTriggerPropertyChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static ICommand? GetLoadMoreCommand(DependencyObject obj) => (ICommand?)obj.GetValue(LoadMoreCommandProperty);
    public static void SetLoadMoreCommand(DependencyObject obj, ICommand? value) => obj.SetValue(LoadMoreCommandProperty, value);

    public static bool GetHasMoreItems(DependencyObject obj) => (bool)obj.GetValue(HasMoreItemsProperty);
    public static void SetHasMoreItems(DependencyObject obj, bool value) => obj.SetValue(HasMoreItemsProperty, value);

    public static bool GetIsLoading(DependencyObject obj) => (bool)obj.GetValue(IsLoadingProperty);
    public static void SetIsLoading(DependencyObject obj, bool value) => obj.SetValue(IsLoadingProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl itemsControl)
            return;

        if ((bool)e.NewValue)
            Attach(itemsControl);
        else
            Detach(itemsControl);
    }

    private static void OnTriggerPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl itemsControl || !GetIsEnabled(itemsControl))
            return;

        TryLoadMore(itemsControl, isInitial: false);
    }

    private static void Attach(ItemsControl itemsControl)
    {
        var state = GetOrCreateState(itemsControl);
        if (state.IsAttached)
            return;

        itemsControl.Loaded += OnLoaded;
        itemsControl.Unloaded += OnUnloaded;
        state.IsAttached = true;

        if (itemsControl.IsLoaded)
            OnLoaded(itemsControl, new RoutedEventArgs(FrameworkElement.LoadedEvent, itemsControl));
    }

    private static void Detach(ItemsControl itemsControl)
    {
        var state = GetState(itemsControl);
        if (state is null)
            return;

        itemsControl.Loaded -= OnLoaded;
        itemsControl.Unloaded -= OnUnloaded;
        state.InitialLoadScheduled = false;
        state.InitialLoadRequested = false;
        state.InitialLoadVersion++;
        DetachScrollViewer(state);
        state.IsAttached = false;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl itemsControl)
            return;

        var state = GetOrCreateState(itemsControl);
        AttachScrollViewer(itemsControl, state);

        if (state.InitialLoadScheduled || state.InitialLoadRequested)
            return;

        state.InitialLoadScheduled = true;
        var scheduledVersion = ++state.InitialLoadVersion;
        itemsControl.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            () =>
            {
                state.InitialLoadScheduled = false;
                if (!state.IsAttached || !GetIsEnabled(itemsControl) || !itemsControl.IsLoaded || state.InitialLoadVersion != scheduledVersion)
                    return;

                AttachScrollViewer(itemsControl, state);
                TryLoadMore(itemsControl, isInitial: true);
            });
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ItemsControl itemsControl)
            return;

        var state = GetState(itemsControl);
        if (state is null)
            return;

        state.InitialLoadScheduled = false;
        state.InitialLoadRequested = false;
        state.InitialLoadVersion++;
        DetachScrollViewer(state);
    }

    private static void AttachScrollViewer(ItemsControl itemsControl, ScrollOnDemandState state)
    {
        var scrollViewer = FindScrollViewer(itemsControl);
        if (scrollViewer is null || ReferenceEquals(scrollViewer, state.ScrollViewer))
            return;

        DetachScrollViewer(state);

        state.ScrollViewer = scrollViewer;
        state.ScrollViewer.ScrollChanged += state.ScrollChangedHandler;
    }

    private static void DetachScrollViewer(ScrollOnDemandState state)
    {
        if (state.ScrollViewer is null)
            return;

        state.ScrollViewer.ScrollChanged -= state.ScrollChangedHandler;
        state.ScrollViewer = null;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject start)
    {
        if (start is ItemsControl itemsControl && ShouldUseOwnedScrollViewer(itemsControl))
        {
            if (itemsControl.Template?.FindName("PART_ScrollViewer", itemsControl) is ScrollViewer templateScrollViewer)
                return templateScrollViewer;

            if (FindTemplateOwnedDescendantScrollViewer(itemsControl, itemsControl) is { } descendantTemplateScrollViewer)
                return descendantTemplateScrollViewer;
        }

        return DependencyObjectTree.FindAncestor<ScrollViewer>(DependencyObjectTree.GetParent(start));
    }

    private static bool ShouldUseOwnedScrollViewer(ItemsControl itemsControl) =>
        ScrollViewer.GetVerticalScrollBarVisibility(itemsControl) != ScrollBarVisibility.Disabled;

    private static ScrollViewer? FindTemplateOwnedDescendantScrollViewer(DependencyObject root, ItemsControl owner)
    {
        foreach (var child in DependencyObjectTree.GetChildren(root))
        {
            if (child is ScrollViewer scrollViewer && ReferenceEquals(scrollViewer.TemplatedParent, owner))
                return scrollViewer;

            if (FindTemplateOwnedDescendantScrollViewer(child, owner) is { } nested)
                return nested;
        }

        return null;
    }

    private static void TryLoadMore(ItemsControl itemsControl, bool isInitial)
    {
        if (!GetIsEnabled(itemsControl))
            return;

        var state = GetOrCreateState(itemsControl);
        if (state.IsExecutingCommand)
            return;

        if (isInitial)
        {
            if (state.InitialLoadRequested)
                return;

            state.InitialLoadRequested = true;
        }

        if (!ShouldTrigger(itemsControl, state.ScrollViewer, isInitial))
            return;

        var command = GetLoadMoreCommand(itemsControl);
        if (command is null)
            return;

        try
        {
            if (!command.CanExecute(null))
                return;

            state.IsExecutingCommand = true;
            command.Execute(null);
        }
        catch (Exception exception)
        {
            // Command-side failures should never break UI scrolling.
            FluxoLogManager.LogWarning(exception, "ScrollOnDemandBehavior command execution failed.");
            Trace.TraceWarning("ScrollOnDemandBehavior command execution failed: {0}", exception);
        }
        finally
        {
            state.IsExecutingCommand = false;
        }
    }

    private static bool ShouldTrigger(ItemsControl owner, ScrollViewer? scrollViewer, bool isInitial)
    {
        if (!GetHasMoreItems(owner) || GetIsLoading(owner))
            return false;

        if (isInitial)
            return true;

        if (scrollViewer is null)
            return false;

        var distanceFromBottom = scrollViewer.ExtentHeight - (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight);
        return distanceFromBottom <= PreloadThreshold;
    }

    private static ScrollOnDemandState GetOrCreateState(ItemsControl itemsControl)
    {
        var state = GetState(itemsControl);
        if (state is not null)
            return state;

        state = new ScrollOnDemandState(itemsControl);
        itemsControl.SetValue(StateProperty, state);
        return state;
    }

    private static ScrollOnDemandState? GetState(ItemsControl itemsControl) =>
        (ScrollOnDemandState?)itemsControl.GetValue(StateProperty);

    private sealed class ScrollOnDemandState
    {
        public ScrollOnDemandState(ItemsControl owner)
        {
            ScrollChangedHandler = (_, _) => TryLoadMore(owner, isInitial: false);
        }

        public bool IsAttached { get; set; }
        public bool InitialLoadScheduled { get; set; }
        public int InitialLoadVersion { get; set; }
        public bool InitialLoadRequested { get; set; }
        public bool IsExecutingCommand { get; set; }
        public ScrollViewer? ScrollViewer { get; set; }
        public ScrollChangedEventHandler ScrollChangedHandler { get; }
    }
}
