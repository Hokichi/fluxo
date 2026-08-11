using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Shell.Main;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class FloatingNotificationListVMTests
{
    [Fact]
    public async Task FloatingNotificationListVM_Receive_AppendsNewestAndExpiresIndependently()
    {
        var messenger = new StrongReferenceMessenger();
        using var vm = new FloatingNotificationListVM(
            messenger, TimeSpan.FromMilliseconds(120), TimeSpan.Zero);

        messenger.Send(Message("First"));
        await Task.Delay(70);
        messenger.Send(Message("Second"));

        Assert.Equal(["First", "Second"], vm.Items.Select(item => item.Header));
        Assert.Equal("Second", vm.ScrollTarget!.Header);

        await Task.Delay(70);
        Assert.Single(vm.Items);
        Assert.Equal("Second", vm.Items[0].Header);
    }

    [Fact]
    public async Task FloatingNotificationListVM_Activate_RunsCallbackOnceAndRemovesItem()
    {
        var messenger = new StrongReferenceMessenger();
        using var vm = new FloatingNotificationListVM(messenger, TimeSpan.FromSeconds(5), TimeSpan.Zero);
        var calls = 0;
        messenger.Send(Message("Clickable", () => { calls++; return Task.CompletedTask; }));
        var item = Assert.Single(vm.Items);

        await item.ActivateCommand.ExecuteAsync(null);
        await item.ActivateCommand.ExecuteAsync(null);

        Assert.Equal(1, calls);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void FloatingNotificationListVM_Dismiss_RemovesOnlyMatchingItem()
    {
        var messenger = new StrongReferenceMessenger();
        using var vm = new FloatingNotificationListVM(messenger, TimeSpan.FromMinutes(1), TimeSpan.Zero);
        var firstId = FloatingNotificationPublisher.Publish(
            messenger, "First", string.Empty, [], NotificationSeverity.Info);
        FloatingNotificationPublisher.Publish(
            messenger, "Second", string.Empty, [], NotificationSeverity.Info);

        FloatingNotificationPublisher.Dismiss(messenger, firstId);

        Assert.Equal("Second", Assert.Single(vm.Items).Header);
    }

    [Fact]
    public void FloatingNotificationListVM_Receive_PreservesStructuredHeaderAction()
    {
        var messenger = new StrongReferenceMessenger();
        using var vm = new FloatingNotificationListVM(messenger, TimeSpan.FromMinutes(1), TimeSpan.Zero);

        messenger.Send(new ShowFloatingNotificationMessage(new FloatingNotificationRequest(
            "Checking account", string.Empty, [], NotificationSeverity.Success,
            HeaderAction: "Updated")));

        Assert.Equal("Updated", Assert.Single(vm.Items).HeaderAction);
    }

    private static ShowFloatingNotificationMessage Message(string header, Func<Task>? click = null) =>
        new(new FloatingNotificationRequest(header, string.Empty, [], NotificationSeverity.Info, click));
}
