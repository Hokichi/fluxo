using System.Runtime.ExceptionServices;
using System.Windows;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Shell.Main;
using Fluxo.Views.Shell.Main.Pages;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class CalendarXamlResourceTests
{
    [Fact]
    public void Calendar_RendersInitialWeek_WhenLocalTemplateResourcesResolve()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var calendar = new Calendar(new CalendarVM(Substitute.For<ICalendarService>()));

            calendar.Measure(new Size(1440, 810));
            calendar.Arrange(new Rect(0, 0, 1440, 810));
            calendar.UpdateLayout();

            Assert.NotNull(calendar.Content);
        });
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Theme.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml",
                     "Styles/SettingsStyle.xaml", "Styles/StepNavigatorStyle.xaml", "Styles/QuickSetupWizardStyle.xaml"
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
