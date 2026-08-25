using System.ComponentModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Fluxo.Resources.Components;
using Fluxo.Resources.CustomControls;
using Fluxo.Views.Popups;
using Xunit;

namespace Fluxo.Tests.Views.CustomControls;

public sealed class BasePopupApplicationMainWindowTests
{
    [Fact]
    public void BasePopupApplicationMainWindow_Popups_BubbleOnlyWhenTheyCanClose()
    {
        RunSta(() =>
        {
            if (Application.Current is { } existingApplication && !existingApplication.Dispatcher.CheckAccess())
                return;

            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            EnsureApplicationResources(application);
            var previousMainWindow = application.MainWindow;
            var root = new Window();
            application.MainWindow = root;

            try
            {
                var parent = new TestPopup();
                parent.RaiseLoaded();
                Assert.Same(parent, application.MainWindow);

                var child = new TestPopup();
                child.RaiseLoaded();
                Assert.Same(child, application.MainWindow);

                child.CompleteClose();
                Assert.Same(parent, application.MainWindow);

                parent.CompleteClose();
                Assert.Same(root, application.MainWindow);

                var messageBox = new MessageBoxPopup("Message", "Title");
                messageBox.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Same(root, application.MainWindow);

                var toast = new ToastPopup("Working", () => Task.CompletedTask);
                toast.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Assert.Same(root, application.MainWindow);

                Assert.False(messageBox.ShowCloseButton);
            }
            finally
            {
                application.MainWindow = previousMainWindow;
            }
        });
    }

    private static void EnsureApplicationResources(Application application)
    {
        foreach (var resource in new[]
                 {
                     "Themes/Dark.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml"
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

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class TestPopup : BasePopup
    {
        public void RaiseLoaded() => RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        public void CompleteClose()
        {
            var firstClose = new CancelEventArgs();
            OnClosing(firstClose);
            Assert.True(firstClose.Cancel);

            OnClosing(new CancelEventArgs());
        }
    }
}
