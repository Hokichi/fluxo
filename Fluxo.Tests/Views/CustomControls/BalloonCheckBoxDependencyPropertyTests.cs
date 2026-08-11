using System.Windows;
using System.Windows.Media;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class BalloonCheckBoxDependencyPropertyTests
{
    [Fact]
    public void BalloonCheckBoxDependencyProperty_BalloonCheckBox_ClickTogglesAndRaisesEvents()
    {
        RunOnStaThread(() =>
        {
            var checkBox = new TestBalloonCheckBox();
            var checkedCount = 0;
            var uncheckedCount = 0;
            checkBox.Checked += (_, _) => checkedCount++;
            checkBox.Unchecked += (_, _) => uncheckedCount++;

            checkBox.InvokeClick();
            Assert.True(checkBox.IsChecked);
            Assert.Equal(1, checkedCount);

            checkBox.InvokeClick();
            Assert.False(checkBox.IsChecked);
            Assert.Equal(1, uncheckedCount);
        });
    }

    [Fact]
    public void BalloonCheckBoxDependencyProperty_BalloonCheckBox_DefaultsStateIconAndTextToButtonIconAndText()
    {
        RunOnStaThread(() =>
        {
            var icon = Geometry.Parse("M 0,0 L 1,1");
            var checkBox = new TestBalloonCheckBox
            {
                ButtonIcon = icon,
                ButtonText = "Fallback"
            };

            Assert.Same(icon, checkBox.CurrentIcon());
            Assert.Equal("Fallback", checkBox.CurrentText());

            checkBox.IsChecked = true;

            Assert.Same(icon, checkBox.CurrentIcon());
            Assert.Equal("Fallback", checkBox.CurrentText());
        });
    }

    private sealed class TestBalloonCheckBox : BalloonCheckBox
    {
        public void InvokeClick() => OnClick();

        public object? CurrentIcon() => ResolveButtonIcon();

        public string? CurrentText() => ResolveButtonText();
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
