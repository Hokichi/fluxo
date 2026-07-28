using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.History;

namespace Fluxo.Helpers.Settings;

internal static class SettingsShared
{
    public static async Task UpdateUserSettingAsync(IAppDataService appData, string name, string? value,
        List<ILogMemoryAction> actions)
    {
        var existingSetting = await appData.GetUserSettingByNameAsync(name);
        var beforeSnapshot = existingSetting is null
            ? UserSettingMemorySnapshot.Missing(name)
            : UserSettingMemorySnapshot.Create(existingSetting);

        if (value is null)
        {
            if (existingSetting is null)
                return;

            appData.RemoveUserSetting(existingSetting);
            actions.Add(new SetUserSettingMemoryAction(beforeSnapshot, UserSettingMemorySnapshot.Missing(name)));
            return;
        }

        if (existingSetting is null)
        {
            await appData.AddUserSettingAsync(new UserSettings { Name = name, Value = value });
        }
        else
        {
            if (string.Equals(existingSetting.Value, value, StringComparison.Ordinal))
                return;

            existingSetting.Value = value;
            appData.UpdateUserSetting(existingSetting);
        }

        actions.Add(new SetUserSettingMemoryAction(beforeSnapshot, new UserSettingMemorySnapshot(name, value, true)));
    }

    public static async Task UpdateIdSetSettingAsync(IAppDataService appData, string name, HashSet<int> ids,
        List<ILogMemoryAction> actions)
    {
        var value = ids.Count == 0
            ? null
            : string.Join(",", ids.OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture)));

        await UpdateUserSettingAsync(appData, name, value, actions);
    }

    public static int[] NormalizeSelectionIds(IReadOnlyCollection<int>? selectedIdsOverride,
        IEnumerable<int> validIds,
        IEnumerable<int> fallbackIds)
    {
        var validIdSet = validIds.ToHashSet();
        var source = selectedIdsOverride is { Count: > 0 } ? selectedIdsOverride : fallbackIds;

        return source
            .Where(id => id > 0 && validIdSet.Contains(id))
            .Distinct()
            .ToArray();
    }

    public static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    public static string ParseString(IReadOnlyDictionary<string, string> settings, string name, string defaultValue)
    {
        if (!settings.TryGetValue(name, out var value))
            return defaultValue;

        return value?.Trim() ?? defaultValue;
    }

    public static int ParsePercentage(IReadOnlyDictionary<string, string> settings, string name, decimal defaultValue)
    {
        if (!settings.TryGetValue(name, out var value) ||
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedValue))
            return (int)defaultValue;

        return (int)Math.Round(parsedValue, MidpointRounding.AwayFromZero);
    }

    public static int ParsePositiveInt(IReadOnlyDictionary<string, string> settings, string name, int defaultValue)
    {
        if (!settings.TryGetValue(name, out var value) ||
            !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) ||
            parsedValue <= 0)
        {
            return defaultValue;
        }

        return parsedValue;
    }

    public static bool ParseBool(IReadOnlyDictionary<string, string> settings, string name, bool defaultValue)
    {
        return settings.TryGetValue(name, out var value)
            ? UserSettingValueParser.ParseBool(value, defaultValue)
            : defaultValue;
    }

    public static AppCloseBehavior ParseCloseBehavior(IReadOnlyDictionary<string, string> settings,
        string name,
        AppCloseBehavior defaultValue)
    {
        return settings.TryGetValue(name, out var value)
            ? UserSettingValueParser.ParseCloseBehavior(value, defaultValue)
            : defaultValue;
    }

    public static HashSet<int> ParseIdSet(IReadOnlyDictionary<string, string> settings, string name)
    {
        if (!settings.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
                int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : -1)
            .Where(id => id > 0)
            .ToHashSet();
    }

    public static async Task<Dictionary<string, string>> GetSettingsDictionaryAsync(IAppDataService appData)
    {
        var settings = await appData.GetUserSettingsAsync();
        return settings.ToDictionary(setting => setting.Name, setting => setting.Value, StringComparer.Ordinal);
    }

}
