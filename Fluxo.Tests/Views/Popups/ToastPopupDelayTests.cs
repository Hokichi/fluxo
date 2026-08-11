using Fluxo.Views.Popups;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class ToastPopupDelayTests
{
    [Fact]
    public void ToastPopupDelay_CalculateCloseDelay_UsesFiveHundredMillisecondsForShortMessages()
    {
        var delay = ToastPopup.CalculateCloseDelay("Done");

        Assert.Equal(TimeSpan.FromMilliseconds(500), delay);
    }

    [Fact]
    public void ToastPopupDelay_CalculateCloseDelay_ScalesWithMessageLength()
    {
        var delay = ToastPopup.CalculateCloseDelay(new string('x', 20));

        Assert.Equal(TimeSpan.FromMilliseconds(1_000), delay);
    }
}
