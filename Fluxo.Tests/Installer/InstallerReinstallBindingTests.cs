using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using Fluxo.Installer.Models;
using Fluxo.Installer.ViewModels;
using Fluxo.Installer.Views.Pages;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class InstallerReinstallBindingTests
{
    [Fact]
    public void Confirmation_BindsPromptAndCommands_AndDeclinesWithoutShowingAWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new InstallerViewModel(requestDetect: () => { });
                vm.Begin();
                vm.OnStartupDetectionComplete(0, new(true, "1.0.6", false, true), @"D:\My Apps\fluxo");
                var page = new InstallerReinstallPage { DataContext = vm };
                page.Measure(new Size(700, 400));
                page.Arrange(new Rect(0, 0, 700, 400));
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    () => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var decline = Assert.IsType<Button>(page.FindName("DeclineButton"));
                Assert.True(decline.IsDefault);
                Assert.Same(vm.DeclineReinstallCommand, decline.Command);
                Assert.Equal(BindingMode.OneWay, BindingOperations.GetBinding(decline, Button.CommandProperty)!.Mode);
                var escape = Assert.Single(page.InputBindings.Cast<KeyBinding>());
                Assert.Equal(Key.Escape, escape.Key);
                Assert.Same(vm.DeclineReinstallCommand, escape.Command);
                Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text.Contains("1.0.6"));
                Assert.Contains(Descendants(page).OfType<TextBlock>(), text => text.Text == vm.InstallFolder);
                decline.Command.Execute(null);
                Assert.Equal(InstallerScreen.Finished, vm.Screen);
                Assert.False(decline.Command.CanExecute(null));
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "In-memory binding test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(owner).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
