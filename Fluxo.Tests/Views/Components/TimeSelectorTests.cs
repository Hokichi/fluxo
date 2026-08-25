using Fluxo.Resources.Components;
using System.Windows;
using System.Windows.Input;
using Xunit;

namespace Fluxo.Tests.Views.Components;

public sealed class TimeSelectorTests
{
    [Fact]
    public void TimeSelector_SelectedTime_FormatsAs24HourHoursAndMinutesWithHours_Active()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var selector = new TimeSelector { SelectedTime = new TimeSpan(14, 5, 0) };

            Assert.Equal("14:05", selector.FormattedSelectedTime);
            Assert.Equal(TimeSelectorSegment.Hours, selector.ActiveSegment);
        });
    }

    [Fact]
    public void TimeSelector_Typing_TwoHourDigitsUpdatesHoursAndMovesTo_Minutes()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var selector = new TimeSelector();

            selector.ProcessTextForTests("1");
            selector.ProcessTextForTests("4");

            Assert.Equal(new TimeSpan(14, 0, 0), selector.SelectedTime);
            Assert.Equal(TimeSelectorSegment.Minutes, selector.ActiveSegment);
        });
    }

    [Fact]
    public void TimeSelector_Arrow_KeysSelectSegmentsAndWrapTime_Boundaries()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var selector = new TimeSelector { SelectedTime = new TimeSpan(23, 59, 0) };

            selector.ProcessKeyForTests(Key.Right);
            selector.ProcessKeyForTests(Key.Up);

            Assert.Equal(new TimeSpan(0, 0, 0), selector.SelectedTime);
            selector.ProcessKeyForTests(Key.Down);
            Assert.Equal(new TimeSpan(23, 59, 0), selector.SelectedTime);
        });
    }

    [Fact]
    public void TimeSelector_Typed_MinutesCarryIntoHourWithoutExceeding_2359()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var selector = new TimeSelector { SelectedTime = new TimeSpan(23, 0, 0) };

            selector.ProcessKeyForTests(Key.Right);
            selector.ProcessTextForTests("7");
            selector.ProcessTextForTests("5");

            Assert.Equal(new TimeSpan(0, 15, 0), selector.SelectedTime);
            Assert.Equal("00:15", selector.FormattedSelectedTime);
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
                     "Themes/Dark.xaml", "Fonts.xaml", "Icons.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/TextBoxStyles.xaml"
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
