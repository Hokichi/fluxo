using Fluxo.Resources.Components;
using System.Windows;
using Xunit;

namespace Fluxo.Tests.Views.Components;

public sealed class DateSelectorFutureSelectionTests
{
    [Fact]
    public void DateSelectorFutureSelection_DateSelector_DisablesDayItemsAfterMaxSelectableDate()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();

            var selector = new DateSelector
            {
                SelectedDate = new DateTime(2026, 6, 4),
                MaxSelectableDate = new DateTime(2026, 6, 4)
            };

            selector.RebuildViewForTests();

            Assert.Contains(selector.Days, day => day.Date == new DateTime(2026, 6, 5) && !day.IsEnabled);
            Assert.Contains(selector.Days, day => day.Date == new DateTime(2026, 6, 4) && day.IsEnabled);
        });
    }

    [Fact]
    public void DateSelectorFutureSelection_DateSelector_CoercesSelectedDateToMaxSelectableDate()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();

            var selector = new DateSelector
            {
                MaxSelectableDate = new DateTime(2026, 6, 4),
                SelectedDate = new DateTime(2026, 6, 5)
            };

            Assert.Equal(new DateTime(2026, 6, 4), selector.SelectedDate);
        });
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
            throw exception;
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Theme.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml"
                 })
        {
            if (application.Resources.MergedDictionaries.Any(dictionary =>
                    dictionary.Source?.OriginalString.EndsWith($"Resources/{resource}", StringComparison.OrdinalIgnoreCase) == true))
                continue;

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Fluxo.Resources;component/Resources/{resource}", UriKind.Relative)
            });
        }
    }
}
