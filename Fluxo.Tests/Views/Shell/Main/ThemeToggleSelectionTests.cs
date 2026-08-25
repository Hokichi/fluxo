using Fluxo.Resources.Theming;
using Fluxo.Views.Shell.Main;
using Xunit;

namespace Fluxo.Tests.Views.Shell.Main;

public sealed class ThemeToggleSelectionTests
{
    [Theory]
    [InlineData(false, ApplicationTheme.Light)]
    [InlineData(true, ApplicationTheme.Dark)]
    public void ResolveThemeFromToggle_MapsThumbSideToAdjacentIcon(
        bool isChecked,
        ApplicationTheme expectedTheme)
    {
        Assert.Equal(expectedTheme, MainWindow.ResolveThemeFromToggle(isChecked));
    }
}
