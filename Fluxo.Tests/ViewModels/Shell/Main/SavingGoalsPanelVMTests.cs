using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.DTO;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Repositories;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Shell;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public class SavingGoalsPanelVMTests
{
    [Fact]
    public void SavingGoalsPanelVM_WeeklyAverageText_UsesCurrentAmountOverCompletedWeeks()
    {
        var goal = new SavingGoalVM
        {
            CreatedOn = DateTime.Today.AddDays(-21),
            CurrentAmount = 210m,
            TargetAmount = 1000m
        };

        Assert.Equal("70", goal.WeeklyAverageText);
    }

    [Fact]
    public void SavingGoalsPanelVM_WeeklyAverageText_RoundsUp()
    {
        var goal = new SavingGoalVM
        {
            CreatedOn = DateTime.Today.AddDays(-14),
            CurrentAmount = 101m,
            TargetAmount = 1000m
        };

        Assert.Equal("51", goal.WeeklyAverageText);
    }

    [Fact]
    public void SavingGoalsPanelVM_WeeklyAverageText_ClampsNewGoalToOneWeek()
    {
        var goal = new SavingGoalVM
        {
            CreatedOn = DateTime.Today,
            CurrentAmount = 125m,
            TargetAmount = 1000m
        };

        Assert.Equal("125", goal.WeeklyAverageText);
    }

    [Fact]
    public void SavingGoalsPanelVM_EstimatedDeadlineText_ReturnsUndefinedWhenEndDateMissing()
    {
        var goal = new SavingGoalVM
        {
            SavingEndDate = null
        };

        Assert.Equal("Indefinite", goal.EstimatedDeadlineText);
    }

    [Fact]
    public async Task SavingGoalsPanelVM_LoadAsync_FiltersCompletedGoals()
    {
        var goals = new List<SavingGoalVM>
        {
            new() { Id = 1, Name = "Emergency Fund", TargetAmount = 1000m, CurrentAmount = 250m },
            new() { Id = 2, Name = "Laptop",         TargetAmount = 1500m, CurrentAmount = 1500m }
        };

        var vm = CreateVm(goals);
        await vm.LoadAsync();

        Assert.True(vm.HasSavingGoals);
        var remainingGoal = Assert.Single(vm.SavingGoals);
        Assert.Equal(1, remainingGoal.Id);
        Assert.Equal(0, vm.CurrentGoalIndex);
        Assert.Equal(1, vm.CurrentGoal?.Id);
        Assert.Equal(1, vm.GoalStepCount);
        Assert.Equal(1, vm.CurrentStepNumber);
        var activeGoal = Assert.Single(vm.SavingGoals, goal => goal.IsActive);
        Assert.Equal(remainingGoal, activeGoal);
    }

    [Fact]
    public async Task SavingGoalsPanelVM_NavigatePrevious_WrapsFromFirstToLastGoal()
    {
        var vm = CreateVm(CreateGoals(3));
        await vm.LoadAsync();

        vm.NavigatePrevious();

        Assert.Equal(2, vm.CurrentGoalIndex);
        Assert.Equal(3, vm.CurrentGoal?.Id);
        Assert.Equal(3, vm.GoalStepCount);
        Assert.Equal(3, vm.CurrentStepNumber);
        Assert.Equal(1, vm.NavigationDirection);
        Assert.True(vm.SavingGoals[2].IsActive);
        Assert.All(vm.SavingGoals.Where((_, index) => index != 2), goal => Assert.False(goal.IsActive));
    }

    [Fact]
    public async Task SavingGoalsPanelVM_NavigateNext_WrapsFromLastToFirstGoal()
    {
        var vm = CreateVm(CreateGoals(2));
        await vm.LoadAsync();

        vm.NavigatePrevious();
        vm.NavigateNext();

        Assert.Equal(0, vm.CurrentGoalIndex);
        Assert.Equal(1, vm.CurrentGoal?.Id);
        Assert.Equal(2, vm.GoalStepCount);
        Assert.Equal(1, vm.CurrentStepNumber);
        Assert.Equal(-1, vm.NavigationDirection);
        Assert.True(vm.SavingGoals[0].IsActive);
        Assert.False(vm.SavingGoals[1].IsActive);
    }

    private static SavingGoalsPanelVM CreateVm(IReadOnlyList<SavingGoalVM> goals)
    {
        var savingGoalRepository = Substitute.For<ISavingGoalRepository>();
        savingGoalRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SavingGoal>>([]));

        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SavingGoals.Returns(savingGoalRepository);

        var userSettingsRepository = Substitute.For<IUserSettingsRepository>();
        userSettingsRepository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserSettings>>([]));
        unitOfWork.UserSettings.Returns(userSettingsRepository);
        var dataOperationRunner = new InlineDataOperationRunner(unitOfWork);

        var mapper = Substitute.For<IMapper>();
        mapper.Map<IReadOnlyList<SavingGoalDto>>(Arg.Any<object>()).Returns(new List<SavingGoalDto>());
        mapper.Map<IReadOnlyList<SavingGoalVM>>(Arg.Any<object>()).Returns(goals);

        return new SavingGoalsPanelVM(
            dataOperationRunner,
            mapper,
            new WeakReferenceMessenger());
    }

    private static IReadOnlyList<SavingGoalVM> CreateGoals(int count)
    {
        return Enumerable.Range(1, count)
            .Select(id => new SavingGoalVM
            {
                Id = id,
                Name = $"Goal {id}",
                TargetAmount = 1000m,
                CurrentAmount = 100m
            })
            .ToList();
    }
}
