using Fluxo.Installer.Services;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class LegacySelfContainedCleanupServiceTests
{
    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_RemovesKnownLegacyRuntimeArtifacts()
    {
        var deleted = new List<string>();
        var service = CreateService(
            files:
            [
                @"C:\Fluxo\coreclr.dll",
                @"C:\Fluxo\hostfxr.dll",
                @"C:\Fluxo\PresentationFramework.dll",
                @"C:\Fluxo\System.Private.CoreLib.dll",
            ],
            deleted: deleted);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Equal(
            [
                @"C:\Fluxo\coreclr.dll",
                @"C:\Fluxo\hostfxr.dll",
                @"C:\Fluxo\PresentationFramework.dll",
                @"C:\Fluxo\System.Private.CoreLib.dll",
            ],
            deleted);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_PreservesCurrentApplicationFilesAndUserState()
    {
        var deleted = new List<string>();
        var service = CreateService(
            files:
            [
                @"C:\Fluxo\fluxo.exe",
                @"C:\Fluxo\fluxo.dll",
                @"C:\Fluxo\fluxo.deps.json",
                @"C:\Fluxo\fluxo.runtimeconfig.json",
                @"C:\Fluxo\fluxo.Repairer.exe",
                @"C:\Fluxo\fluxo.db",
                @"C:\Fluxo\installer.log",
            ],
            deleted: deleted);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Empty(deleted);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_RemovesRootDll_WhenSameDllExistsUnderLibs()
    {
        var deleted = new List<string>();
        var service = CreateService(
            files: [@"C:\Fluxo\FluentMigrator.dll"],
            existingFiles: [@"C:\Fluxo\libs\FluentMigrator.dll"],
            deleted: deleted);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Equal([@"C:\Fluxo\FluentMigrator.dll"], deleted);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_RemovesRootDll_WhenSameDllExistsUnderVendor()
    {
        var deleted = new List<string>();
        var service = CreateService(
            files: [@"C:\Fluxo\SQLitePCLRaw.core.dll"],
            existingFiles: [@"C:\Fluxo\vendor\SQLitePCLRaw.core.dll"],
            deleted: deleted);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Equal([@"C:\Fluxo\SQLitePCLRaw.core.dll"], deleted);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_PreservesUnknownRootDll_WhenNoNestedDuplicateExists()
    {
        var deleted = new List<string>();
        var service = CreateService(
            files: [@"C:\Fluxo\Plugin.Abstractions.dll"],
            deleted: deleted);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Empty(deleted);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_RemovesRootPdbFiles()
    {
        var deleted = new List<string>();
        var service = CreateService(
            files:
            [
                @"C:\Fluxo\fluxo.pdb",
                @"C:\Fluxo\Fluxo.Core.pdb",
            ],
            deleted: deleted);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Equal(
            [
                @"C:\Fluxo\fluxo.pdb",
                @"C:\Fluxo\Fluxo.Core.pdb",
            ],
            deleted);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_RemovesLegacyPublishFolder()
    {
        var deletedDirectories = new List<string>();
        var service = CreateService(
            files: [],
            existingDirectories: [@"C:\Fluxo\publish"],
            deletedDirectories: deletedDirectories);

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.True(result.Success);
        Assert.Equal([@"C:\Fluxo\publish"], deletedDirectories);
    }

    [Fact]
    public void LegacySelfContainedCleanupService_Cleanup_ReturnsFailure_WhenDeleteFails()
    {
        var service = new LegacySelfContainedCleanupService(
            directoryExists: _ => true,
            enumerateFiles: (_, _) => [@"C:\Fluxo\coreclr.dll"],
            deleteFile: _ => throw new UnauthorizedAccessException("access denied"));

        var result = service.Cleanup(@"C:\Fluxo");

        Assert.False(result.Success);
        Assert.Equal("access denied", result.Message);
    }

    private static LegacySelfContainedCleanupService CreateService(
        IReadOnlyCollection<string> files,
        IReadOnlyCollection<string>? existingFiles = null,
        IReadOnlyCollection<string>? existingDirectories = null,
        List<string>? deleted = null,
        List<string>? deletedDirectories = null)
    {
        var existingFileSet = new HashSet<string>(existingFiles ?? [], StringComparer.OrdinalIgnoreCase);
        var existingDirectorySet = new HashSet<string>(existingDirectories ?? [], StringComparer.OrdinalIgnoreCase);
        return new LegacySelfContainedCleanupService(
            directoryExists: path => string.Equals(path, @"C:\Fluxo", StringComparison.OrdinalIgnoreCase)
                                     || existingDirectorySet.Contains(path),
            fileExists: existingFileSet.Contains,
            enumerateFiles: (_, _) => files,
            deleteFile: file => deleted?.Add(file),
            deleteDirectory: directory => deletedDirectories?.Add(directory));
    }
}
