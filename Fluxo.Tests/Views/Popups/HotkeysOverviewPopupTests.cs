using System.Threading;
using System.Windows;
using Fluxo.Views.Popups;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class HotkeysOverviewPopupTests
{
    [Fact]
    public void GlobalHotkeys_DisplaysLockForCtrlShiftL()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var popup = new HotkeysOverviewPopup();

            var hotkey = popup.HotkeyGroups
                .Single(group => group.Name == "Global")
                .Hotkeys
                .Single(item => item.Parts.Select(part => part.Text).SequenceEqual(["Ctrl", "Shift", "L"]));

            Assert.Equal("Lock", hotkey.Functionality);
        });
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Themes/Dark.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml"
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
            try { action(); }
            catch (Exception error) { exception = error; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw new Xunit.Sdk.XunitException(exception.ToString());
    }
}
