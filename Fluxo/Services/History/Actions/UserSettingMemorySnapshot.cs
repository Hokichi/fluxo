using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.History;
using Fluxo.Core.Interfaces.Services;
using System.Globalization;

namespace Fluxo.Services.History;

public sealed record UserSettingMemorySnapshot(
    string Name,
    string Value,
    bool Exists)
{
    public static UserSettingMemorySnapshot Create(UserSettings userSettings)
    {
        ArgumentNullException.ThrowIfNull(userSettings);

        return new UserSettingMemorySnapshot(
            userSettings.Name,
            userSettings.Value,
            true);
    }

    public static UserSettingMemorySnapshot Missing(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new UserSettingMemorySnapshot(name, string.Empty, false);
    }
}
