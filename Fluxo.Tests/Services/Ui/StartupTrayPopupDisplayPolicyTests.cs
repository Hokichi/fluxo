using Fluxo.Services.Ui;
using Xunit;

namespace Fluxo.Tests.Services.Ui;

public sealed class StartupTrayPopupDisplayPolicyTests
{
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    public void StartupTrayPopupDisplayPolicy_ShouldShow_ReturnsExpectedDecision(
        bool launchInTrayMode,
        bool alreadyShownThisProcess,
        bool hasSummary,
        bool expected)
    {
        var sut = new StartupTrayPopupDisplayPolicy();

        var result = sut.ShouldShow(launchInTrayMode, alreadyShownThisProcess, hasSummary);

        Assert.Equal(expected, result);
    }
}
