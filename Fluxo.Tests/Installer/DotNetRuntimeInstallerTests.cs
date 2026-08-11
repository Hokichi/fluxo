using Fluxo.Installer.Services;
using Fluxo.Tests.TestSupport;
using Xunit;

namespace Fluxo.Tests.Installer;

public sealed class DotNetRuntimeInstallerTests
{
    [Fact]
    public async Task DotNetRuntimeInstaller_EnsureInstalledAsync_ReturnsInstalled_WhenRuntimeAlreadyPresent()
    {
        var service = CreateService(isRuntimeInstalled: () => true);

        var result = await service.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(DotNetRuntimeInstallStatus.AlreadyInstalled, result.Status);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_EnsureInstalledAsync_DownloadsInstallsVerifiesAndSavesMarker_WhenRuntimeMissing()
    {
        var runtimeInstalled = false;
        var downloads = new List<string>();
        var processStarts = new List<(string FileName, string Arguments)>();
        DotNetRuntimeOwnershipMarker? savedMarker = null;
        var service = CreateService(
            isRuntimeInstalled: () => runtimeInstalled,
            downloadFileAsync: (url, path, _) =>
            {
                downloads.Add($"{url}|{path}");
                return Task.CompletedTask;
            },
            runProcessAsync: (fileName, arguments, _) =>
            {
                processStarts.Add((fileName, arguments));
                runtimeInstalled = true;
                return Task.FromResult(0);
            },
            saveMarker: marker => savedMarker = marker);

        var result = await service.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(DotNetRuntimeInstallStatus.InstalledByFluxo, result.Status);
        Assert.Single(downloads);
        Assert.Contains("runtime.exe", downloads[0], StringComparison.OrdinalIgnoreCase);
        Assert.Single(processStarts);
        Assert.Equal("/install /quiet /norestart", processStarts[0].Arguments);
        Assert.NotNull(savedMarker);
        Assert.Equal("10.0.8", savedMarker!.Version);
        Assert.True(savedMarker.InstalledByFluxo);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_EnsureInstalledAsync_DeletesTempInstaller_WhenInstallVerificationFails()
    {
        var deletedFiles = new List<string>();
        var service = CreateService(
            isRuntimeInstalled: () => false,
            deleteFile: path => deletedFiles.Add(path));

        var result = await service.EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(DotNetRuntimeInstallStatus.Failed, result.Status);
        Assert.Contains(WindowsPathFixtures.TempFile("runtime.exe"), deletedFiles);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_RollbackAsync_UninstallsRuntimeInstalledByFluxo()
    {
        var processStarts = new List<(string FileName, string Arguments)>();
        var markerCleared = false;
        var service = CreateService(
            readMarker: () => new DotNetRuntimeOwnershipMarker("10.0.8", "win-x64", "https://example.test/runtime.exe", true),
            runProcessAsync: (fileName, arguments, _) =>
            {
                processStarts.Add((fileName, arguments));
                return Task.FromResult(0);
            },
            clearMarker: () => markerCleared = true);

        await service.RollbackRuntimeInstalledByFluxoAsync(CancellationToken.None);

        Assert.Single(processStarts);
        Assert.Equal("/uninstall /quiet /norestart", processStarts[0].Arguments);
        Assert.True(markerCleared);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_UninstallOwnedRuntimeAsync_Skips_WhenMarkerMissing()
    {
        var processCalls = 0;
        var service = CreateService(
            readMarker: () => null,
            runProcessAsync: (_, _, _) =>
            {
                processCalls++;
                return Task.FromResult(0);
            });

        var result = await service.UninstallOwnedRuntimeAsync(CancellationToken.None);

        Assert.Equal(DotNetRuntimeUninstallStatus.Skipped, result.Status);
        Assert.Equal(0, processCalls);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_UninstallOwnedRuntimeAsync_DownloadsAndRunsSilentUninstall_WhenMarkerExists()
    {
        var processStarts = new List<(string FileName, string Arguments)>();
        var markerCleared = false;
        var service = CreateService(
            readMarker: () => new DotNetRuntimeOwnershipMarker("10.0.8", "win-x64", "https://example.test/runtime.exe", true),
            runProcessAsync: (fileName, arguments, _) =>
            {
                processStarts.Add((fileName, arguments));
                return Task.FromResult(0);
            },
            clearMarker: () => markerCleared = true);

        var result = await service.UninstallOwnedRuntimeAsync(CancellationToken.None);

        Assert.Equal(DotNetRuntimeUninstallStatus.Uninstalled, result.Status);
        Assert.Single(processStarts);
        Assert.Equal("/uninstall /quiet /norestart", processStarts[0].Arguments);
        Assert.True(markerCleared);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_UninstallOwnedRuntimeAsync_DoesNotClearMarker_WhenSilentUninstallFails()
    {
        var markerCleared = false;
        var service = CreateService(
            readMarker: () => new DotNetRuntimeOwnershipMarker("10.0.8", "win-x64", "https://example.test/runtime.exe", true),
            runProcessAsync: (_, _, _) => Task.FromResult(123),
            clearMarker: () => markerCleared = true);

        var result = await service.UninstallOwnedRuntimeAsync(CancellationToken.None);

        Assert.Equal(DotNetRuntimeUninstallStatus.Failed, result.Status);
        Assert.False(markerCleared);
    }

    [Fact]
    public async Task DotNetRuntimeInstaller_RollbackRuntimeInstalledByFluxoAsync_DoesNotClearMarker_WhenSilentUninstallFails()
    {
        var markerCleared = false;
        var service = CreateService(
            readMarker: () => new DotNetRuntimeOwnershipMarker("10.0.8", "win-x64", "https://example.test/runtime.exe", true),
            runProcessAsync: (_, _, _) => Task.FromResult(123),
            clearMarker: () => markerCleared = true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RollbackRuntimeInstalledByFluxoAsync(CancellationToken.None));

        Assert.False(markerCleared);
    }

    private static DotNetRuntimeInstaller CreateService(
        Func<bool>? isRuntimeInstalled = null,
        Func<string, string, CancellationToken, Task>? downloadFileAsync = null,
        Func<string, string, CancellationToken, Task<int>>? runProcessAsync = null,
        Action<string>? deleteFile = null,
        Action<DotNetRuntimeOwnershipMarker>? saveMarker = null,
        Func<DotNetRuntimeOwnershipMarker?>? readMarker = null,
        Action? clearMarker = null)
    {
        var detector = new DelegateRuntimeDetector(isRuntimeInstalled ?? (() => false));
        var ownershipStore = new DelegateOwnershipStore(
            readMarker ?? (() => null),
            saveMarker ?? (_ => { }),
            clearMarker ?? (() => { }));

        return new DotNetRuntimeInstaller(
            runtimeDetector: detector,
            ownershipStore: ownershipStore,
            downloadFileAsync: downloadFileAsync ?? ((_, _, _) => Task.CompletedTask),
            runProcessAsync: runProcessAsync ?? ((_, _, _) => Task.FromResult(0)),
            deleteFile: deleteFile ?? (_ => { }),
            tempPathFactory: static fileName => WindowsPathFixtures.TempFile(fileName),
            releaseInfoProvider: static _ => Task.FromResult(new DotNetRuntimeInstallerInfo(
                "10.0.8",
                "win-x64",
                "runtime.exe",
                "https://example.test/runtime.exe",
                "abc123",
                "https://example.test/releases.json")));
    }

    private sealed class DelegateRuntimeDetector(Func<bool> isInstalled) : IDotNetRuntimeDetector
    {
        public bool IsRequiredRuntimeInstalled() => isInstalled();
    }

    private sealed class DelegateOwnershipStore(
        Func<DotNetRuntimeOwnershipMarker?> read,
        Action<DotNetRuntimeOwnershipMarker> save,
        Action clear) : IDotNetRuntimeOwnershipStore
    {
        public DotNetRuntimeOwnershipMarker? Read() => read();
        public void Save(DotNetRuntimeOwnershipMarker marker) => save(marker);
        public void Clear() => clear();
    }
}
