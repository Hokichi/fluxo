using System.Windows;
using System.Windows.Controls;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class BalloonRadioButtonTests
{
    [Fact]
    public void BalloonRadioButton_Click_SelectsOneRadioAndCannotToggleItOff()
    {
        RunOnStaThread(() =>
        {
            var panel = new StackPanel();
            var first = new TestBalloonRadioButton { GroupName = "Modes" };
            var second = new TestBalloonRadioButton { GroupName = "Modes" };
            panel.Children.Add(first);
            panel.Children.Add(second);

            first.InvokeClick();
            first.InvokeClick();
            Assert.True(first.IsChecked);

            second.InvokeClick();
            Assert.False(first.IsChecked);
            Assert.True(second.IsChecked);
        });
    }

    [Fact]
    public void BalloonRadioButton_DifferentGroups_StaySelected()
    {
        RunOnStaThread(() =>
        {
            var panel = new StackPanel();
            var first = new TestBalloonRadioButton { GroupName = "A", UncheckedText = "First" };
            var second = new TestBalloonRadioButton { GroupName = "B", CheckedText = "Second" };
            panel.Children.Add(first);
            panel.Children.Add(second);

            first.InvokeClick();
            second.InvokeClick();

            Assert.True(first.IsChecked);
            Assert.True(second.IsChecked);
        });
    }

    private sealed class TestBalloonRadioButton : BalloonRadioButton
    {
        public void InvokeClick() => OnClick();

    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { exception = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null) throw exception;
    }
}
