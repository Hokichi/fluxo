using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using Fluxo.Resources.CustomControls;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class FluxoMessageBoxOwnerTests
{
    [Fact]
    public void FluxoMessageBoxOwner_ResolveOwnerForDialog_ReturnsNullWhenResolvedOwnerIsDialogItself()
    {
        RunInSta(() =>
        {
            var dialog = new Window();

            var owner = FluxoMessageBox.ResolveOwnerForDialog(dialog, dialog, () => null);

            Assert.Null(owner);
        });
    }

    [Fact]
    public void FluxoMessageBoxOwner_ResolveOwnerForDialog_UsesFallbackOwnerWhenRequestedOwnerIsNull()
    {
        RunInSta(() =>
        {
            var dialog = new Window();
            var fallback = new Window();

            var owner = FluxoMessageBox.ResolveOwnerForDialog(dialog, null, () => fallback);

            Assert.Same(fallback, owner);
        });
    }

    private static void RunInSta(Action action)
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
}
