using Fluxo.Services.Backups;
using Xunit;

namespace Fluxo.Tests.Services.Backups;

public sealed class UserBackupServicePathTests
{
    [Fact]
    public void UserBackupServicePath_BuildDefaultBackupFileName_UsesRequestedTimestamp()
    {
        var timestamp = new DateTime(2026, 5, 26, 7, 8, 9);

        var fileName = UserBackupService.BuildDefaultBackupFileName(timestamp);

        Assert.Equal("fluxo_user-backup_260526070809.json", fileName);
    }

    [Fact]
    public void UserBackupServicePath_BuildDefaultBackupDirectory_UsesLocalAppDataFluxoUserBackups()
    {
        var directory = UserBackupService.BuildDefaultBackupDirectory();

        Assert.EndsWith(Path.Combine("fluxo", "user_backups"), directory);
    }
}
