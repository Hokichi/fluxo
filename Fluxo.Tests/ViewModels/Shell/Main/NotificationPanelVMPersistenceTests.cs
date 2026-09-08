using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class NotificationPanelVMPersistenceTests
{
    [Fact]
    public async Task EntityCreated_WhenEvaluationFails_HandlesFailure()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Fluxo.Core.Entities.Transaction>>(
                new InvalidOperationException("evaluation failed")));
        var viewModel = new NotificationPanelVM(
            appData,
            messenger: new WeakReferenceMessenger());

        await viewModel.HandleNotificationEntityCreatedAsync(
            new NotificationEntityCreatedMessage(NotificationEntityKind.Account, 1));
    }
}
