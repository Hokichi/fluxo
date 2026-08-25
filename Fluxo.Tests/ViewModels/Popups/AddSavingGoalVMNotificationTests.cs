using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class AddSavingGoalVMNotificationTests
{
    [Fact]
    public async Task AddSavingGoalVMNotification_SaveAsync_InEditMode_PublishesUpdatedAction()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetSavingGoalByIdAsync(7).Returns(new SavingGoal { Id = 7, Name = "Old goal" });
        var vm = new AddSavingGoalVM(appData) { EditingId = 7 };
        vm.NameText = "Emergency fund";
        vm.TargetAmountText = 1000m;
        ShowFloatingNotificationMessage? notification = null;
        WeakReferenceMessenger.Default.Register<ShowFloatingNotificationMessage>(
            this, (_, message) => notification = message);

        try
        {
            var result = await vm.SaveAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal("Emergency fund", notification!.Value.Header);
            Assert.Equal("Updated", notification.Value.HeaderAction);
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }

    [Fact]
    public async Task AddSavingGoalVM_SaveAsync_WhenInvalidationFailsAfterSave_ReturnsSuccess()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.AddSavingGoalAsync(Arg.Any<SavingGoal>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var vm = new AddSavingGoalVM(appData)
        {
            NameText = "Emergency fund",
            TargetAmountText = 1000m
        };
        var recipient = new object();
        WeakReferenceMessenger.Default.Register<DashboardDataInvalidatedMessage>(
            recipient,
            (_, _) => throw new InvalidOperationException("invalidation failed"));

        try
        {
            var result = await vm.SaveAsync();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        }
        finally
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }
    }
}
