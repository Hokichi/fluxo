using System.Windows.Input;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class BasePopupKeyboardTests
{
    [Fact]
    public void BasePopupKeyboard_CtrlEnter_SkipsOnlyWhenNavigatePopupAllowsSkipping()
    {
        RunInSta(() =>
        {
            var popup = new TestPopup { Mode = PopupMode.Navigate, CanSkip = true, StepCount = 2 };

            Assert.True(popup.Handle(Key.Enter, ModifierKeys.Control));
            Assert.Equal(1, popup.SkipCount);
            Assert.Equal(0, popup.NextCount);

            Assert.True(popup.Handle(Key.Enter, ModifierKeys.None));
            Assert.Equal(1, popup.NextCount);
        });
    }

    [Fact]
    public void BasePopupKeyboard_Enter_DoesNotNavigateWhenPopupCannotGo_Next()
    {
        RunInSta(() =>
        {
            var popup = new TestPopup { Mode = PopupMode.Navigate, CanGoNext = false, StepCount = 2 };

            Assert.False(popup.Handle(Key.Enter, ModifierKeys.None));
            Assert.Equal(0, popup.NextCount);
        });
    }

    [Fact]
    public void BasePopupKeyboard_Enter_DoesNotFinishWhenPopupCannot_Finish()
    {
        RunInSta(() =>
        {
            var popup = new TestPopup { Mode = PopupMode.Navigate, CanFinish = false, StepCount = 1 };

            Assert.False(popup.Handle(Key.Enter, ModifierKeys.None));
            Assert.Equal(0, popup.FinishCount);
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception caught) { exception = caught; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception is not null) throw exception;
    }

    private sealed class TestPopup : BasePopup
    {
        public int NextCount { get; private set; }
        public int FinishCount { get; private set; }
        public int SkipCount { get; private set; }

        public bool Handle(Key key, ModifierKeys modifiers) => TryHandlePopupShortcut(key, modifiers);

        protected override void OnNextButtonClick() => NextCount++;
        protected override void OnFinishButtonClick() => FinishCount++;
        protected override void OnSkipButtonClick() => SkipCount++;
    }
}
