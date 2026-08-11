using System.Collections.ObjectModel;
using Xunit;

namespace Fluxo.Tests.ViewModels.CustomControls;

public sealed class StepNavigatorControlTests
{
    [Fact]
    public void StepNavigatorControl_UpdateDotStates_CurrentStep1_FirstDotActiveOthersUpcoming()
    {
        var dots = MakeDots(3);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 1);

        Assert.True(dots[0].IsActive);
        Assert.False(dots[0].IsCompleted);
        Assert.False(dots[1].IsActive);
        Assert.False(dots[1].IsCompleted);
        Assert.False(dots[2].IsActive);
        Assert.False(dots[2].IsCompleted);
    }

    [Fact]
    public void StepNavigatorControl_UpdateDotStates_CurrentStep2_FirstCompletedSecondActive()
    {
        var dots = MakeDots(3);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 2);

        Assert.False(dots[0].IsActive);
        Assert.True(dots[0].IsCompleted);
        Assert.True(dots[1].IsActive);
        Assert.False(dots[1].IsCompleted);
        Assert.False(dots[2].IsActive);
        Assert.False(dots[2].IsCompleted);
    }

    [Fact]
    public void StepNavigatorControl_UpdateDotStates_CurrentStep3_TwoCompletedLastActive()
    {
        var dots = MakeDots(3);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 3);

        Assert.False(dots[0].IsActive);
        Assert.True(dots[0].IsCompleted);
        Assert.False(dots[1].IsActive);
        Assert.True(dots[1].IsCompleted);
        Assert.True(dots[2].IsActive);
        Assert.False(dots[2].IsCompleted);
    }

    [Fact]
    public void StepNavigatorControl_UpdateDotStates_NavigateBackward_PreviousCompletedBecomesUpcoming()
    {
        var dots = MakeDots(3);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 3);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 2);

        Assert.False(dots[0].IsActive);
        Assert.True(dots[0].IsCompleted);
        Assert.True(dots[1].IsActive);
        Assert.False(dots[1].IsCompleted);
        Assert.False(dots[2].IsActive);
        Assert.False(dots[2].IsCompleted);
    }

    [Fact]
    public void StepNavigatorControl_UpdateDotStates_ExactlyOneDotIsActive()
    {
        var dots = MakeDots(5);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 3);

        Assert.Equal(1, dots.Count(d => d.IsActive));
    }

    [Fact]
    public void StepNavigatorControl_UpdateDotStates_ActiveDotIsNeverCompleted()
    {
        var dots = MakeDots(5);
        StepNavigatorControl.UpdateDotStates(dots, currentStep: 3);

        Assert.False(dots[2].IsActive && dots[2].IsCompleted);
    }

    [Fact]
    public void StepNavigatorControl_CalculateVisibleWindow_PaginationDisabled_ShowsAllDots()
    {
        var window = StepNavigatorControl.CalculateVisibleWindow(
            stepCount: 12,
            currentStep: 7,
            paginationCount: 5,
            shouldPaginate: false);

        Assert.Equal(0, window.Start);
        Assert.Equal(12, window.Count);
    }

    [Fact]
    public void StepNavigatorControl_CalculateVisibleWindow_CurrentStepInFirstCycle_ShowsFirstPage()
    {
        var window = StepNavigatorControl.CalculateVisibleWindow(
            stepCount: 12,
            currentStep: 3,
            paginationCount: 5,
            shouldPaginate: true);

        Assert.Equal(0, window.Start);
        Assert.Equal(5, window.Count);
    }

    [Fact]
    public void StepNavigatorControl_CalculateVisibleWindow_CurrentStepInSecondCycle_ShowsSecondPage()
    {
        var window = StepNavigatorControl.CalculateVisibleWindow(
            stepCount: 12,
            currentStep: 8,
            paginationCount: 5,
            shouldPaginate: true);

        Assert.Equal(5, window.Start);
        Assert.Equal(5, window.Count);
    }

    [Fact]
    public void StepNavigatorControl_CalculateVisibleWindow_CurrentStepInLastCycle_ShowsRemainingDots()
    {
        var window = StepNavigatorControl.CalculateVisibleWindow(
            stepCount: 12,
            currentStep: 11,
            paginationCount: 5,
            shouldPaginate: true);

        Assert.Equal(10, window.Start);
        Assert.Equal(2, window.Count);
    }

    [Fact]
    public void StepNavigatorControl_UpdateDotStates_WithWindowOffset_UsesAbsoluteStepIndexForCycle()
    {
        var dots = MakeDots(5);

        StepNavigatorControl.UpdateDotStates(
            dots,
            currentStep: 8,
            windowStart: 5);

        Assert.True(dots[0].IsCompleted);
        Assert.True(dots[1].IsCompleted);
        Assert.True(dots[2].IsActive);
        Assert.False(dots[2].IsCompleted);
        Assert.False(dots[3].IsActive);
        Assert.False(dots[3].IsCompleted);
        Assert.False(dots[4].IsActive);
        Assert.False(dots[4].IsCompleted);
    }

    [Fact]
    public void StepNavigatorControl_BuildStepCounterText_ValidStepAndCount_ReturnsCurrentAndTotal()
    {
        var text = StepNavigatorControl.BuildStepCounterText(stepCount: 8, currentStep: 3);

        Assert.Equal("3/8", text);
    }

    [Fact]
    public void StepNavigatorControl_BuildStepCounterText_CurrentStepOutOfRange_ClampsToValidBounds()
    {
        var lower = StepNavigatorControl.BuildStepCounterText(stepCount: 8, currentStep: 0);
        var upper = StepNavigatorControl.BuildStepCounterText(stepCount: 8, currentStep: 42);

        Assert.Equal("1/8", lower);
        Assert.Equal("8/8", upper);
    }

    [Fact]
    public void StepNavigatorControl_BuildStepCounterText_NoSteps_ReturnsZeroOverZero()
    {
        var text = StepNavigatorControl.BuildStepCounterText(stepCount: 0, currentStep: 1);

        Assert.Equal("0/0", text);
    }

    private static ObservableCollection<StepNavigatorDotVM> MakeDots(int count)
    {
        var dots = new ObservableCollection<StepNavigatorDotVM>();
        for (var i = 0; i < count; i++)
            dots.Add(new StepNavigatorDotVM { IsFirst = i == 0 });
        return dots;
    }
}
