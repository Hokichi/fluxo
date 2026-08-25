using System.Windows;

namespace Fluxo.Resources.Theming;

public static class ThemeManager
{
    private const string ThemeResourcePrefix = "/Fluxo.Resources;component/Resources/Themes/";

    public static ApplicationTheme CurrentTheme { get; private set; } = ApplicationTheme.Dark;

    public static void SwitchTheme(ApplicationTheme theme)
    {
        var application = Application.Current
                          ?? throw new InvalidOperationException("No WPF application is active.");
        application.Dispatcher.VerifyAccess();
        SwitchTheme(application.Resources, theme);
    }

    public static void SwitchTheme(ResourceDictionary applicationResources, ApplicationTheme theme)
    {
        ArgumentNullException.ThrowIfNull(applicationResources);

        var activeTheme = FindActiveTheme(applicationResources)
                          ?? throw new InvalidOperationException("No Fluxo theme dictionary is loaded.");
        var targetTheme = LoadTheme(theme);
        EnsureMatchingKeys(activeTheme, targetTheme);

        var themeIndex = applicationResources.MergedDictionaries.IndexOf(activeTheme);
        applicationResources.MergedDictionaries[themeIndex] = targetTheme;

        CurrentTheme = theme;
    }

    private static ResourceDictionary? FindActiveTheme(ResourceDictionary resources)
    {
        return resources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.StartsWith(ThemeResourcePrefix, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static ResourceDictionary LoadTheme(ApplicationTheme theme) => new()
    {
        Source = new Uri($"{ThemeResourcePrefix}{theme}.xaml", UriKind.Relative)
    };

    private static void EnsureMatchingKeys(ResourceDictionary activeTheme, ResourceDictionary targetTheme)
    {
        var activeKeys = activeTheme.Keys.Cast<object>().ToHashSet();
        var targetKeys = targetTheme.Keys.Cast<object>().ToHashSet();

        if (!activeKeys.SetEquals(targetKeys))
            throw new InvalidOperationException("Dark and Light theme resource keys must match exactly.");
    }
}
