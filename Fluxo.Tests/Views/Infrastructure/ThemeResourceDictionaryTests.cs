using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fluxo.Resources.Theming;
using Xunit;

namespace Fluxo.Tests.Views.Infrastructure;

public sealed class ThemeResourceDictionaryTests
{
    [Fact]
    public void DarkAndLightThemes_ExposeIdenticalResourceKeys()
    {
        RunOnStaThread(() =>
        {
            _ = Application.Current ?? new Application();
            var darkTheme = LoadTheme("Dark");
            var lightTheme = LoadTheme("Light");
            var darkKeys = ResourceKeys(darkTheme);
            var lightKeys = ResourceKeys(lightTheme);

            Assert.Equal(62, darkKeys.Count(key => key.StartsWith("Color.", StringComparison.Ordinal)));
            Assert.Equal(63, darkKeys.Count(key => key.StartsWith("Brush.", StringComparison.Ordinal)));
            Assert.Equal(darkKeys, lightKeys);
        });
    }

    [Fact]
    public void SwitchTheme_UpdatesDynamicBrushConsumers()
    {
        RunOnStaThread(() =>
        {
            _ = Application.Current ?? new Application();
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(LoadTheme("Dark"));
            var consumer = CreateBrushConsumer(resources);

            Assert.Equal(Color.FromRgb(0x06, 0x0A, 0x08), GetBackgroundColor(consumer));

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Light);

            Assert.Equal(Color.FromRgb(0xF0, 0xF5, 0xF1), GetBackgroundColor(consumer));
            Assert.Equal(ApplicationTheme.Light, ThemeManager.CurrentTheme);

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Dark);

            Assert.Equal(Color.FromRgb(0x06, 0x0A, 0x08), GetBackgroundColor(consumer));
            Assert.Equal(ApplicationTheme.Dark, ThemeManager.CurrentTheme);
        });
    }

    [Fact]
    public void SwitchTheme_AfterSharedStylesLoad_UpdatesDynamicBrushConsumers()
    {
        RunOnStaThread(() =>
        {
            _ = Application.Current ?? new Application();
            var resources = new ResourceDictionary();
            foreach (var resource in new[]
                 {
                     "Themes/Dark.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ButtonStyles.xaml"
                 })
            {
                resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/Fluxo.Resources;component/Resources/{resource}", UriKind.Relative)
                });
            }

            var consumer = CreateBrushConsumer(resources);

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Light);

            Assert.Equal(Color.FromRgb(0xF0, 0xF5, 0xF1), GetBackgroundColor(consumer));

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Dark);
        });
    }

    private static Border CreateBrushConsumer(ResourceDictionary resources)
    {
        var root = new Grid { Resources = resources };
        var consumer = new Border();
        consumer.SetResourceReference(Panel.BackgroundProperty, "Brush.Background.Main");
        root.Children.Add(consumer);
        return consumer;
    }

    private static Color GetBackgroundColor(Border consumer) =>
        Assert.IsType<SolidColorBrush>(consumer.Background).Color;

    private static ResourceDictionary LoadTheme(string themeName) => new()
    {
        Source = new Uri(
            $"/Fluxo.Resources;component/Resources/Themes/{themeName}.xaml",
            UriKind.Relative)
    };

    private static IReadOnlyList<string> ResourceKeys(ResourceDictionary dictionary)
    {
        return dictionary.Keys
            .Cast<object>()
            .Select(key => key.ToString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caughtException)
            {
                exception = caughtException;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
