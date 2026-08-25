using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Fluxo.Views.Popups.Settings;
using Xunit;

namespace Fluxo.Tests.Views.Popups.Settings;

public sealed class SettingsSearchNavigationTests
{
    [Fact]
    public void FindSearchTarget_ReturnsDescendantWithMatchingAutomationId()
    {
        RunOnStaThread(() =>
        {
            var expected = new TextBox();
            AutomationProperties.SetAutomationId(expected, "Settings.Personalization.Username");
            var root = new Grid();
            root.Children.Add(new Border { Child = expected });

            var actual = SettingsPopup.FindSearchTarget(root, "Settings.Personalization.Username");

            Assert.Same(expected, actual);
        });
    }

    [Fact]
    public void FindSearchTarget_ReturnsNullWhenAutomationIdIsNotPresent()
    {
        RunOnStaThread(() =>
        {
            var root = new Grid();
            root.Children.Add(new TextBox());

            Assert.Null(SettingsPopup.FindSearchTarget(root, "Settings.Configuration.CheckUpdates"));
        });
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
