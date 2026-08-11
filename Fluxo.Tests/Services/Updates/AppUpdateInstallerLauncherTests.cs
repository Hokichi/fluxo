using Fluxo.Services.Updates;
using Xunit;

namespace Fluxo.Tests.Services.Updates;

public sealed class AppUpdateInstallerLauncherTests
{
    [Fact]
    public void AppUpdateInstallerLauncher_CreateStartInfo_PassesExistingInstallDirectory()
    {
        var startInfo = AppUpdateInstallerLauncher.CreateStartInfo(
            installerPath: @"X:\Downloads\fluxo-1.2.0-Installer.exe",
            installFolder: @"C:\Program Files\fluxo");

        Assert.Equal(@"X:\Downloads\fluxo-1.2.0-Installer.exe", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal("--install-folder \"C:\\Program Files\\fluxo\"", startInfo.Arguments);
    }
}
