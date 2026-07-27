using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Fluxo.Helpers.Behaviors;

public static class ScrollViewerAnimationBehavior
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "VerticalOffset",
            typeof(double),
            typeof(ScrollViewerAnimationBehavior),
            new PropertyMetadata(0d, OnVerticalOffsetChanged));

    public static double GetVerticalOffset(DependencyObject obj) => (double)obj.GetValue(VerticalOffsetProperty);
    public static void SetVerticalOffset(DependencyObject obj, double value) => obj.SetValue(VerticalOffsetProperty, value);

    public static void AnimateToVerticalOffset(
        ScrollViewer scrollViewer,
        double targetVerticalOffset,
        TimeSpan duration,
        IEasingFunction? easingFunction = null)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        var clampedTarget = Math.Clamp(targetVerticalOffset, 0d, scrollViewer.ScrollableHeight);
        var startVerticalOffset = scrollViewer.VerticalOffset;

        scrollViewer.BeginAnimation(VerticalOffsetProperty, null);

        if (Math.Abs(startVerticalOffset - clampedTarget) < 0.5d)
        {
            scrollViewer.ScrollToVerticalOffset(clampedTarget);
            return;
        }

        var animation = new DoubleAnimation
        {
            From = startVerticalOffset,
            To = clampedTarget,
            Duration = new Duration(duration),
            EasingFunction = easingFunction,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            scrollViewer.BeginAnimation(VerticalOffsetProperty, null);
            scrollViewer.ScrollToVerticalOffset(clampedTarget);
        };

        scrollViewer.BeginAnimation(VerticalOffsetProperty, animation);
    }

    private static void OnVerticalOffsetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is ScrollViewer scrollViewer)
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
    }
}
