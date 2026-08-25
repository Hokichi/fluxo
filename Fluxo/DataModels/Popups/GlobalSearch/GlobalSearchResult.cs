namespace Fluxo.DataModels.Popups.GlobalSearch;

public sealed record GlobalSearchResult(
    GlobalSearchResultType Type,
    string Name,
    string? SecondaryText = null,
    int? EntityId = null,
    DateTime? LoggedOn = null,
    GlobalSearchFeatureTarget? FeatureTarget = null,
    SettingsSearchTarget? SettingsTarget = null);
