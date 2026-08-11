using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Notifications;
using Xunit;

namespace Fluxo.Tests.Services.Notifications;

public sealed class FloatingNotificationPublisherTests
{
    [Fact]
    public void FloatingNotificationPublisher_Success_PreservesStructuredHeaderAction()
    {
        var messenger = new StrongReferenceMessenger();
        ShowFloatingNotificationMessage? received = null;
        messenger.Register<ShowFloatingNotificationMessage>(this, (_, message) => received = message);

        FloatingNotificationPublisher.Success(
            messenger, "Checking account", "Account details were saved.", true, "Updated");

        Assert.Equal("Checking account", received!.Value.Header);
        Assert.Equal("Updated", received.Value.HeaderAction);
    }

    [Fact]
    public void FloatingNotificationPublisher_Publish_ReturnsRequestId()
    {
        var messenger = new StrongReferenceMessenger();
        ShowFloatingNotificationMessage? received = null;
        messenger.Register<ShowFloatingNotificationMessage>(this, (_, message) => received = message);

        var id = FloatingNotificationPublisher.Publish(
            messenger, "Checking for updates", string.Empty, [], NotificationSeverity.Info);

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, received!.Value.Id);
    }

    [Fact]
    public void FloatingNotificationPublisher_SaveFailed_PublishesSpecificHeaderAndDeduplicatedDetails()
    {
        var messenger = new StrongReferenceMessenger();
        ShowFloatingNotificationMessage? received = null;
        messenger.Register<ShowFloatingNotificationMessage>(this, (_, message) => received = message);

        FloatingNotificationPublisher.SaveFailed(
            messenger,
            "Account not saved",
            ["Amount is required.", "Amount is required.", "Choose an account."]);

        Assert.NotNull(received);
        Assert.Equal("Account not saved", received!.Value.Header);
        Assert.Equal(["Amount is required.", "Choose an account."], received.Value.Details);
        Assert.Equal(NotificationSeverity.Warning, received.Value.Severity);
    }

    [Fact]
    public async Task FloatingNotificationPublisher_Success_WithHistoryAction_ClickRequestsHistoryDrawer()
    {
        var messenger = new StrongReferenceMessenger();
        ShowFloatingNotificationMessage? received = null;
        var openRequests = 0;
        messenger.Register<ShowFloatingNotificationMessage>(this, (_, message) => received = message);
        messenger.Register<OpenHistoryDrawerMessage>(this, (_, _) => openRequests++);

        FloatingNotificationPublisher.Success(messenger, "Expense added", "Lunch was recorded.", true);
        await received!.Value.ClickAsync!();

        Assert.Contains("Click to view in History", received.Value.Message);
        Assert.Equal(1, openRequests);
    }
}
