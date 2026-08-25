using Fluxo.Data.Enums;

namespace Fluxo.DataModels.Popups.GlobalSearch;

public sealed record SettingsSearchTarget(
    SettingsSearchSection Section,
    string AutomationId,
    SettingsBudgetManagementPage? BudgetPage = null);
