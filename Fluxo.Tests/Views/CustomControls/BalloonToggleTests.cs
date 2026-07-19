using System.Windows.Input;
using System.Windows.Media;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class BalloonToggleTests
{
    [Fact]
    public void Click_CyclesStatesThenReturnsToUntoggledToggleVisuals()
    {
        RunOnStaThread(() =>
        {
            var firstCommand = new CountingCommand();
            var secondCommand = new CountingCommand();
            var toggle = new TestBalloonToggle { ButtonText = "Toggle" };
            toggle.States.Add(new BalloonToggleState { ButtonText = "First", OnChecked = firstCommand });
            toggle.States.Add(new BalloonToggleState { ButtonText = "Second", OnChecked = secondCommand });

            Assert.False(toggle.IsCycling);
            Assert.Equal("Toggle", toggle.ResolvedButtonText);

            toggle.InvokeClick();
            Assert.True(toggle.IsCycling);
            Assert.Equal("First", toggle.ResolvedButtonText);
            Assert.Equal(1, firstCommand.ExecuteCount);

            toggle.InvokeClick();
            Assert.Equal("Second", toggle.ResolvedButtonText);
            Assert.Equal(1, secondCommand.ExecuteCount);

            toggle.InvokeClick();
            Assert.False(toggle.IsCycling);
            Assert.Equal("Toggle", toggle.ResolvedButtonText);
            Assert.Equal(1, firstCommand.ExecuteCount);
            Assert.Equal(1, secondCommand.ExecuteCount);
        });
    }

    [Fact]
    public void Click_WithNoStates_RemainsUntoggled()
    {
        RunOnStaThread(() =>
        {
            var toggle = new TestBalloonToggle();
            toggle.InvokeClick();
            Assert.False(toggle.IsCycling);
        });
    }

    [Fact]
    public void Click_SkipsStateWhoseCommandCannotExecute()
    {
        RunOnStaThread(() =>
        {
            var skipped = new CountingCommand(canExecute: false);
            var selected = new CountingCommand();
            var toggle = new TestBalloonToggle();
            toggle.States.Add(new BalloonToggleState { ButtonText = "Skipped", OnChecked = skipped });
            toggle.States.Add(new BalloonToggleState { ButtonText = "Selected", OnChecked = selected });

            toggle.InvokeClick();

            Assert.Equal("Selected", toggle.ResolvedButtonText);
            Assert.Equal(0, skipped.ExecuteCount);
            Assert.Equal(1, selected.ExecuteCount);
        });
    }

    [Fact]
    public void SelectState_IgnoresStateWhoseCommandCannotExecute()
    {
        RunOnStaThread(() =>
        {
            var state = new BalloonToggleState
            {
                ButtonText = "Unavailable",
                OnChecked = new CountingCommand(canExecute: false)
            };
            var toggle = new BalloonToggle();
            toggle.States.Add(state);

            toggle.SelectState(state);

            Assert.False(toggle.IsCycling);
        });
    }

    [Fact]
    public void RemovingActiveState_ResetsToUntoggledFirstState()
    {
        RunOnStaThread(() =>
        {
            var first = new BalloonToggleState { ButtonText = "First" };
            var second = new BalloonToggleState { ButtonText = "Second" };
            var toggle = new TestBalloonToggle { ButtonText = "Toggle" };
            toggle.States.Add(first);
            toggle.States.Add(second);
            toggle.InvokeClick();
            toggle.InvokeClick();

            toggle.States.Remove(second);

            Assert.False(toggle.IsCycling);
            Assert.Equal("Toggle", toggle.ResolvedButtonText);
        });
    }

    [Fact]
    public void OpenStatePopup_SuppressesCycle_AndSelectionActivatesState()
    {
        RunOnStaThread(() =>
        {
            var command = new CountingCommand();
            var state = new BalloonToggleState { ButtonText = "Chosen", OnChecked = command };
            var toggle = new TestBalloonToggle();
            toggle.States.Add(state);

            Assert.True(toggle.TryOpenStatePopup());
            toggle.InvokeClick();
            Assert.False(toggle.IsCycling);

            toggle.SelectState(state);
            Assert.True(toggle.IsCycling);
            Assert.Equal("Chosen", toggle.ResolvedButtonText);
            Assert.Equal(1, command.ExecuteCount);
        });
    }

    [Fact]
    public void OpenStatePopup_WithNoStates_DoesNothing()
    {
        RunOnStaThread(() => Assert.False(new BalloonToggle().TryOpenStatePopup()));
    }

    [Fact]
    public void SelectingActivePopupState_ExecutesCommandAgain()
    {
        RunOnStaThread(() =>
        {
            var command = new CountingCommand();
            var state = new BalloonToggleState { OnChecked = command };
            var toggle = new BalloonToggle();
            toggle.States.Add(state);

            toggle.SelectState(state);
            toggle.SelectState(state);

            Assert.True(toggle.IsCycling);
            Assert.Equal(2, command.ExecuteCount);
        });
    }

    [Fact]
    public void Backgrounds_UseToggleWhenUntoggledAndStateWhenToggled()
    {
        RunOnStaThread(() =>
        {
            var state = new BalloonToggleState
            {
                DefaultBackground = Brushes.Red,
                HoverBackground = Brushes.Orange
            };
            var toggle = new TestBalloonToggle
            {
                DefaultBackground = Brushes.Black,
                HoveredBackground = Brushes.White
            };

            toggle.States.Add(state);

            Assert.Same(Brushes.Black, toggle.ResolvedRestingBackground);
            Assert.Same(Brushes.White, toggle.ResolvedHoveredBackground);

            toggle.SelectState(state);

            Assert.Same(Brushes.Red, toggle.ResolvedRestingBackground);
            Assert.Same(Brushes.Orange, toggle.ResolvedHoveredBackground);
            Assert.Same(Brushes.Black, toggle.DefaultBackground);
            Assert.Same(Brushes.White, toggle.HoveredBackground);
        });
    }

    [Fact]
    public void PopupStates_ShowAllWhenUntoggledAndHideActiveStateWhenToggled()
    {
        RunOnStaThread(() =>
        {
            var first = new BalloonToggleState { ButtonText = "First" };
            var second = new BalloonToggleState { ButtonText = "Second" };
            var toggle = new BalloonToggle();
            toggle.States.Add(first);
            toggle.States.Add(second);

            Assert.Equal(new[] { first, second }, toggle.GetPopupStates());

            toggle.SelectState(first);

            Assert.Equal(new[] { second }, toggle.GetPopupStates());
        });
    }

    [Fact]
    public void Presentation_UsesToggleValuesUntilStateIsActive()
    {
        RunOnStaThread(() =>
        {
            var toggleIcon = new object();
            var stateIcon = new object();
            var state = new BalloonToggleState
            {
                ButtonIcon = stateIcon,
                ButtonText = "State"
            };
            var toggle = new TestBalloonToggle
            {
                ButtonIcon = toggleIcon,
                ButtonText = "Toggle"
            };

            toggle.States.Add(state);

            Assert.Same(toggleIcon, toggle.ResolvedButtonIcon);
            Assert.Equal("Toggle", toggle.ResolvedButtonText);

            toggle.SelectState(state);

            Assert.Same(stateIcon, toggle.ResolvedButtonIcon);
            Assert.Equal("State", toggle.ResolvedButtonText);
            Assert.Same(toggleIcon, toggle.ButtonIcon);
            Assert.Equal("Toggle", toggle.ButtonText);
        });
    }

    private sealed class TestBalloonToggle : BalloonToggle
    {
        public Brush ResolvedRestingBackground => ResolveRestingBackground();
        public Brush ResolvedHoveredBackground => ResolveHoveredBackground();
        public object? ResolvedButtonIcon => ResolveButtonIcon();
        public string? ResolvedButtonText => ResolveButtonText();
        public void InvokeClick() => OnClick();
    }

    private sealed class CountingCommand(bool canExecute = true) : ICommand
    {
        public int ExecuteCount { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => canExecute;
        public void Execute(object? parameter) => ExecuteCount++;
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
