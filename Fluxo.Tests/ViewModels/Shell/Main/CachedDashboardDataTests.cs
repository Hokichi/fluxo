using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Notifications;
using Fluxo.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Shell.Main;

public sealed class CachedDashboardDataTests
{
    [Fact]
    public async Task SnoozeAllNotifications_UpdatesCachedSettingAndSavesOnce()
    {
        var appData = Substitute.For<IAppDataService>();
        var setting = new UserSettings
        {
            Name = UserSettingNames.NotificationsSnoozeEndDate,
            Value = string.Empty
        };
        appData.GetUserSettingByNameAsync(
            UserSettingNames.NotificationsSnoozeEndDate,
            Arg.Any<CancellationToken>()).Returns(setting);
        var viewModel = new NotificationPanelVM(
            appData,
            evaluator: new StartupNotificationEvaluator(appData));

        await viewModel.SnoozeAllNotificationsCommand.ExecuteAsync(null);

        Assert.NotEmpty(setting.Value);
        appData.Received(1).UpdateUserSetting(setting);
        await appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
