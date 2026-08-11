using AutoMapper;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class AllocationDataVMOverflowTests
{
    [Fact]
    public void AllocationDataVMOverflow_RemainingPresentation_UsesPositiveMagnitudeAndCorrectLabel()
    {
        var vm = CreateViewModel();
        vm.NeedsRemaining = -25m;
        vm.WantsRemaining = 0m;
        vm.InvestRemaining = 40m;

        Assert.True(vm.IsNeedsOverflowing);
        Assert.Equal(25m, vm.NeedsRemainingDisplay);
        Assert.Equal("overflowing", vm.NeedsRemainingLabel);
        Assert.False(vm.IsWantsOverflowing);
        Assert.Equal(0m, vm.WantsRemainingDisplay);
        Assert.Equal("remaining", vm.WantsRemainingLabel);
        Assert.False(vm.IsInvestOverflowing);
        Assert.Equal(40m, vm.InvestRemainingDisplay);
        Assert.Equal("remaining", vm.InvestRemainingLabel);
    }

    [Fact]
    public void AllocationDataVMOverflow_RemainingChange_NotifiesDerivedProperties()
    {
        var vm = CreateViewModel();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.NeedsRemaining = -1m;

        Assert.Contains(nameof(AllocationDataVM.IsNeedsOverflowing), changes);
        Assert.Contains(nameof(AllocationDataVM.NeedsRemainingDisplay), changes);
        Assert.Contains(nameof(AllocationDataVM.NeedsRemainingLabel), changes);
    }

    private static AllocationDataVM CreateViewModel() => new(
        Substitute.For<ITransactionService>(),
        Substitute.For<IAccountService>(),
        Substitute.For<IDataOperationRunner>(),
        Substitute.For<IMapper>());
}
