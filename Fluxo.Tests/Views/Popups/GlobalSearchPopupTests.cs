using System.Threading;
using System.Windows;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Views.Popups;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class GlobalSearchPopupTests
{
    [Fact]
    public void RefreshResults_SelectsFirstAvailableTypeAndShowsOnlySelectedTypeRows()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var candidates = new GlobalSearchResult[]
            {
                new(GlobalSearchResultType.Settings, "Username"),
                new(GlobalSearchResultType.Tags, "Food")
            };
            var popup = new GlobalSearchPopup(Task.FromResult<IReadOnlyList<GlobalSearchResult>>(candidates));
            popup.SetCandidatesForTest(candidates);

            popup.RefreshResults("food");

            Assert.Equal(GlobalSearchResultType.Tags, popup.SelectedType);
            Assert.Equal(["Food"], popup.VisibleResults.Select(item => item.Name));
        });
    }

    [Fact]
    public void SelectType_KeepsTheOnlyAvailableTypeSelected()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var candidates = new[] { new GlobalSearchResult(GlobalSearchResultType.Accounts, "Cash Account") };
            var popup = new GlobalSearchPopup(Task.FromResult<IReadOnlyList<GlobalSearchResult>>(candidates));
            popup.SetCandidatesForTest(candidates);
            popup.RefreshResults("cash");

            popup.SelectType(GlobalSearchResultType.Accounts);

            Assert.Equal(GlobalSearchResultType.Accounts, popup.SelectedType);
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
