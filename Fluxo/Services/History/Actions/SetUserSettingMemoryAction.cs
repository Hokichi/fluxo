using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed class SetUserSettingMemoryAction(
    UserSettingMemorySnapshot before,
    UserSettingMemorySnapshot after) : ILogMemoryAction
{
    public string Description => "Update setting";
    public string Title => $"{after.Name} Updated";
    public string Summary => "Setting updated";
    public string Details => string.Empty;

    public Task RevertAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(appData, before, cancellationToken);
    }

    public Task ReapplyAsync(IAppDataService appData, CancellationToken cancellationToken = default)
    {
        return ApplySnapshotAsync(appData, after, cancellationToken);
    }

    private static async Task ApplySnapshotAsync(IAppDataService appData, UserSettingMemorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var existingSetting = await appData.GetUserSettingByNameAsync(snapshot.Name, cancellationToken);

        if (!snapshot.Exists)
        {
            if (existingSetting is null)
                return;

            appData.RemoveUserSetting(existingSetting);
            return;
        }

        if (existingSetting is null)
        {
            await appData.AddUserSettingAsync(new UserSettings
            {
                Name = snapshot.Name,
                Value = snapshot.Value
            }, cancellationToken);
        }
        else
        {
            existingSetting.Value = snapshot.Value;
            appData.UpdateUserSetting(existingSetting);
        }
    }
}
