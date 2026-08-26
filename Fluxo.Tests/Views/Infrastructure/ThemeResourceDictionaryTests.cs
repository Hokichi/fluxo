using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Fluxo.Resources.Converters;
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

            Assert.Equal(Color.FromRgb(0x09, 0x0B, 0x0E), GetBackgroundColor(consumer));

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Light);

            Assert.Equal(Color.FromRgb(0xF2, 0xF8, 0xF5), GetBackgroundColor(consumer));
            Assert.Equal(ApplicationTheme.Light, ThemeManager.CurrentTheme);

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Dark);

            Assert.Equal(Color.FromRgb(0x09, 0x0B, 0x0E), GetBackgroundColor(consumer));
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

            Assert.Equal(Color.FromRgb(0xF2, 0xF8, 0xF5), GetBackgroundColor(consumer));

            ThemeManager.SwitchTheme(resources, ApplicationTheme.Dark);
        });
    }

    [Fact]
    public void SwitchTheme_UpdatesDifferenceToBrushConverterForeground()
    {
        RunOnStaThread(() =>
        {
            var application = Application.Current ?? new Application();
            var originalResources = application.Resources;
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(LoadTheme("Dark"));
            application.Resources = resources;

            try
            {
                var foreground = Assert.IsType<SolidColorBrush>(new DifferenceToBrushConverter()
                    .Convert(0m, typeof(Brush), null, CultureInfo.InvariantCulture));
                var consumer = new TextBlock { Foreground = foreground };

                ThemeManager.SwitchTheme(resources, ApplicationTheme.Light);

                Assert.Equal(Color.FromRgb(0x1F, 0x24, 0x22), foreground.Color);
                Assert.Same(foreground, consumer.Foreground);
            }
            finally
            {
                ThemeManager.SwitchTheme(resources, ApplicationTheme.Dark);
                application.Resources = originalResources;
            }
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
