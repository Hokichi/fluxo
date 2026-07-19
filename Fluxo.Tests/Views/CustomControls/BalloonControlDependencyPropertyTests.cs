using System.Windows;
using System.Windows.Input;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class BalloonControlDependencyPropertyTests
{
    [Fact]
    public void BalloonButton_InheritsSharedBalloonControl()
    {
        Assert.True(typeof(BalloonControl).IsAssignableFrom(typeof(BalloonButton)));
        Assert.Equal(typeof(BalloonControl), BalloonControl.ButtonTextProperty.OwnerType);
        Assert.Equal(typeof(BalloonControl), BalloonControl.DefaultBackgroundProperty.OwnerType);
        Assert.Null(typeof(BalloonControl).GetProperty("ActiveBackground"));
        Assert.Null(typeof(BalloonButton).GetProperty("ActiveBackground"));
    }

    [Fact]
    public void BalloonControl_ExposesStrokePropertiesWithInvisibleDefaults()
    {
        Assert.Equal(typeof(BalloonControl), BalloonControl.StrokeBrushProperty.OwnerType);
        Assert.Equal(typeof(BalloonControl), BalloonControl.StrokeThicknessProperty.OwnerType);
        Assert.Null(BalloonControl.StrokeBrushProperty.GetMetadata(typeof(BalloonControl)).DefaultValue);
        Assert.Equal(0d, BalloonControl.StrokeThicknessProperty.GetMetadata(typeof(BalloonControl)).DefaultValue);
    }

    [Fact]
    public void BalloonControl_DefaultsIconSizeTo12()
    {
        RunOnStaThread(() => Assert.Equal(12d, new BalloonControl().IconSize));
    }

    [Fact]
    public void BalloonControl_CalculatesAutoOpenWidth()
    {
        Assert.Equal(96, BalloonControl.CalculateAutoOpenWidth(
            28,
            8,
            new Thickness(6, 0, 10, 0),
            52,
            new Thickness(8, 0, 8, 0)));
    }

    [Fact]
    public void BalloonControl_CalculatesAutoOpenWidthWithSubText()
    {
        Assert.Equal(138, BalloonControl.CalculateAutoOpenWidth(
            28,
            8,
            new Thickness(6, 0, 10, 0),
            52,
            18));
    }

    [Fact]
    public void BalloonControl_CoercesShouldExpandFalse_WhenShouldShowTextIsTrue()
    {
        RunOnStaThread(() =>
        {
            var button = new BalloonControl
            {
                ShouldShowText = true,
                ShouldExpand = true
            };

            Assert.False(button.ShouldExpand);
        });
    }

    [Fact]
    public void BalloonControl_ExposesSubText()
    {
        RunOnStaThread(() =>
        {
            var button = new BalloonControl { SubText = "Ctrl+S" };

            Assert.Equal("Ctrl+S", button.SubText);
        });
    }

    [Fact]
    public void BalloonControl_ExpandsFromButtonSize_WhenWidthIsAuto()
    {
        RunOnStaThread(() =>
        {
            var button = new TestBalloonControl
            {
                ButtonSize = 28,
                ButtonText = "Save",
                ShouldExpand = true
            };

            button.RaiseMouseEnter();

            Assert.Equal(28d, (double)button.GetAnimationBaseValue(FrameworkElement.WidthProperty));
        });
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private sealed class TestBalloonControl : BalloonControl
    {
        public void RaiseMouseEnter() =>
            OnMouseEnter(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount));
    }
}
