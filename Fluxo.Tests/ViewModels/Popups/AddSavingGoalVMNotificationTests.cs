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
        var vm = new AddSavingGoalVM(null!, appData) { EditingId = 7 };
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
}
