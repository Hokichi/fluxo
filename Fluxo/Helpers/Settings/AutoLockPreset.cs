namespace Fluxo.Helpers.Settings;

public static class AutoLockPreset
{
    public const int DefaultIntervalSeconds = 30;
    public const string Seconds30 = "30";
    public const string Seconds60 = "60";
    public const string Seconds180 = "180";
    public const string Seconds300 = "300";
    public const string Seconds600 = "600";
    public const string Custom = "Custom";

    public static string FromIntervalSeconds(int seconds)
    {
        return seconds switch
        {
            30 => Seconds30,
            60 => Seconds60,
            180 => Seconds180,
            300 => Seconds300,
            600 => Seconds600,
            _ => Custom
        };
    }

    public static bool IsCustom(string? preset)
    {
        return string.Equals(preset, Custom, StringComparison.Ordinal);
    }

    public static bool TryGetSeconds(string? preset, out int seconds)
    {
        seconds = preset switch
        {
            Seconds30 => 30,
            Seconds60 => 60,
            Seconds180 => 180,
            Seconds300 => 300,
            Seconds600 => 600,
            _ => 0
        };

        return seconds > 0;
    }
}
