using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.Settings.PersonalizationSettingsSnapshot;

public sealed record PersonalizationSettingsSnapshot(
    string PreferredAppName,
    bool ShouldRunAtStartup,
    AppCloseBehavior CloseBehavior,
    bool IsAppAutoLocked,
    int AppAutoLockedInterval,
    string UiLockingPassword,
    IReadOnlyDictionary<string, bool> NotificationSettings);
