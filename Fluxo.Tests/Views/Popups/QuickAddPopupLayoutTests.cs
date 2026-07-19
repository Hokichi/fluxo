using System.Windows;
using System.Windows.Controls;
using Fluxo.Resources.CustomControls;
using Fluxo.Views.Popups;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class QuickAddPopupLayoutTests
{
    [Fact]
    public void QuickAccess_ShowsSetupUpdateAndSettingsInSecondRow()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var popup = new QuickAddPopup(null!);
            var grid = Assert.IsType<SpacedUniformGrid>(popup.Content);
            var secondRow = grid.Children.Cast<Button>().Skip(3).Take(3)
                .Select(button => GetTileLabel(button)).ToArray();

            Assert.Equal(["Open Settings", "Run Quick Setup", "Check For Updates"], secondRow);
            Assert.Null(popup.FindName("NewRecurringTransactionQuickAddButton"));
        });
    }

    private static string GetTileLabel(Button button)
    {
        var content = Assert.IsType<StackPanel>(button.Content);
        return Assert.IsType<TextBlock>(content.Children[1]).Text;
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception caught) { exception = caught; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null) throw exception;
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Theme.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml",
                     "Styles/StepNavigatorStyle.xaml", "Styles/QuickSetupWizardStyle.xaml"
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
