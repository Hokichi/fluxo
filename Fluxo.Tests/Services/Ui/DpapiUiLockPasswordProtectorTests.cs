using Fluxo.Services.Ui;
using Xunit;

namespace Fluxo.Tests.Services.Ui;

public sealed class DpapiUiLockPasswordProtectorTests
{
    [Fact]
    public void DpapiUiLockPasswordProtector_Protect_ReturnsEmpty_WhenPasswordIsBlank()
    {
        var protector = new DpapiUiLockPasswordProtector();

        Assert.Equal(string.Empty, protector.Protect("   "));
    }

    [Fact]
    public void DpapiUiLockPasswordProtector_Protect_DoesNotReturnPlainText()
    {
        var protector = new DpapiUiLockPasswordProtector();

        var protectedValue = protector.Protect("secret-pass");

        Assert.StartsWith("dpapi:", protectedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-pass", protectedValue, StringComparison.Ordinal);
    }

    [Fact]
    public void DpapiUiLockPasswordProtector_Unprotect_RoundTripsProtectedPassword()
    {
        var protector = new DpapiUiLockPasswordProtector();
        var protectedValue = protector.Protect("secret-pass");

        var plainText = protector.Unprotect(protectedValue);

        Assert.Equal("secret-pass", plainText);
    }

    [Fact]
    public void DpapiUiLockPasswordProtector_Unprotect_ReturnsEmpty_ForBlankValue()
    {
        var protector = new DpapiUiLockPasswordProtector();

        Assert.Equal(string.Empty, protector.Unprotect(""));
    }
}
