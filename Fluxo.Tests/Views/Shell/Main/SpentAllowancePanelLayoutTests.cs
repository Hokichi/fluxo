using System.Windows;
using Fluxo.Resources.CustomControls;
using Fluxo.Views.Shell.Main.Sections;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class SpentAllowancePanelLayoutTests
{
    [Fact]
    public void SpentAllowancePanel_DisplaysFourMetricsInOneRow()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var panel = new SpentAllowancePanel();

            var metricGrid = Assert.IsType<SpacedUniformGrid>(panel.Content);

            Assert.Equal(4, metricGrid.Columns);
            Assert.Equal(1, metricGrid.Rows);
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
                     "Themes/Dark.xaml", "Fonts.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml"
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
