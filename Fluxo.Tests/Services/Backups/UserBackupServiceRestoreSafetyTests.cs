using Fluxo.Services.Backups;
using Xunit;

namespace Fluxo.Tests.Services.Backups;

public sealed class UserBackupServiceRestoreSafetyTests
{
    [Fact]
    public async Task UserBackupServiceRestoreSafety_RestoreDatabaseBackupAsync_CopiesSafetyBackupOverDatabase()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "fluxo.db");
        var backupPath = Path.Combine(tempDirectory, "safety.db");

        await File.WriteAllTextAsync(databasePath, "broken");
        await File.WriteAllTextAsync(backupPath, "safe");

        await UserBackupService.RestoreDatabaseBackupAsync(databasePath, backupPath);

        Assert.Equal("safe", await File.ReadAllTextAsync(databasePath));
    }

    [Fact]
    public async Task UserBackupServiceRestoreSafety_RestoreDatabaseBackupAsync_MissingBackup_ThrowsAndKeepsDestinationUnchanged()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "fluxo-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var databasePath = Path.Combine(tempDirectory, "fluxo.db");
        var missingBackupPath = Path.Combine(tempDirectory, "missing-safety.db");

        await File.WriteAllTextAsync(databasePath, "original");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            UserBackupService.RestoreDatabaseBackupAsync(databasePath, missingBackupPath));

        Assert.Equal("original", await File.ReadAllTextAsync(databasePath));
    }
}
