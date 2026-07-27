
namespace Fluxo.DataModels.Popups.Settings.SettingsOperationResult;

public readonly record struct SettingsOperationResult(bool IsSuccess, string? ErrorMessage)
{
    public static SettingsOperationResult Success()
    {
        return new SettingsOperationResult(true, null);
    }

    public static SettingsOperationResult Failure(string? errorMessage)
    {
        return new SettingsOperationResult(false, errorMessage);
    }
}
