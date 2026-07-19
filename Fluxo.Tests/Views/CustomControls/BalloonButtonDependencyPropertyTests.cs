using System.Windows;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class BalloonButtonDependencyPropertyTests
{
    [Fact]
    public void BalloonButton_CoercesShouldExpandFalse_WhenShouldShowTextIsTrue()
    {
        RunOnStaThread(() =>
        {
            var button = new BalloonButton
            {
                ShouldExpand = true,
                ShouldShowText = true
            };

            Assert.False(button.ShouldExpand);
        });
    }

    [Fact]
    public void BalloonButton_CalculatesAutoOpenWidthFromTextAndChrome()
    {
        var width = BalloonButton.CalculateAutoOpenWidth(
            buttonSize: 28,
            iconSize: 8,
            padding: new Thickness(6, 0, 10, 0),
            textWidth: 52,
            textMargin: new Thickness(8, 0, 8, 0));

        Assert.Equal(96, width);
    }

    [Fact]
    public void BalloonButton_UsesButtonSizeAsMinimumOpenWidth()
    {
        Assert.Equal(28, BalloonButton.ResolveOpenWidth(28, 20));
    }

    [Fact]
    public void BalloonButton_UsesAutoOpenWidth_WhenContentNeedsMoreSpace()
    {
        Assert.Equal(150, BalloonButton.ResolveOpenWidth(28, 150));
    }

    [Fact]
    public void BalloonButton_UsesAutoOpenWidth()
    {
        Assert.Equal(94, BalloonButton.ResolveOpenWidth(28, 94));
    }

    [Fact]
    public void BalloonButton_MeasuresShouldShowTextContent()
    {
        RunOnStaThread(() =>
        {
            var button = new BalloonButton
            {
                ButtonSize = 28,
                ButtonText = "New Transaction",
                FontSize = 12,
                IconSize = 18,
                Padding = new Thickness(6, 0, 10, 0),
                ShouldShowText = true
            };

            Assert.True(button.GetEffectiveOpenWidth() > 96);
        });
    }

    [Fact]
    public void BalloonButton_MeasuresExpansionContent()
    {
        RunOnStaThread(() =>
        {
            var button = new BalloonButton
            {
                ButtonSize = 28,
                ButtonText = "New Transaction",
                FontSize = 12,
                IconSize = 18,
                Padding = new Thickness(6, 0, 10, 0),
                ShouldExpand = true
            };

            Assert.True(button.GetEffectiveOpenWidth() > 96);
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
}
