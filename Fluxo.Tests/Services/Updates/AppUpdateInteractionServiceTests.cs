using System.Windows;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Updates;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Updates;

public sealed class AppUpdateInteractionServiceTests
{
    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenFirstPromptDeclined_DoesNotDownloadLaunchOrDelete()
    {
        var update = CreateAvailableUpdate();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.No);

        var appUpdateService = Substitute.For<IAppUpdateService>();
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        await appUpdateService
            .DidNotReceive()
            .DownloadInstallerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await dialogService
            .DidNotReceive()
            .ShowDownloadUpdateAsync(
                Arg.Any<AppUpdateCheckResult>(),
                Arg.Any<Func<IProgress<double>, CancellationToken, Task<string>>>(),
                Arg.Any<Window?>());
        appUpdateService.DidNotReceive().DeleteInstaller(Arg.Any<string>());
        lifecycleService.DidNotReceive().LaunchUpdateInstallerAndShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenInstallPromptDeclined_DeletesDownloadedInstaller()
    {
        const string installerPath = "C:\\temp\\fluxo-installer.exe";
        var update = CreateAvailableUpdate();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.Yes, MessageBoxResult.No);
        ConfigureDownloadPopupToReturn(dialogService, installerPath);

        var appUpdateService = Substitute.For<IAppUpdateService>();
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        await dialogService.Received(1).ShowDownloadUpdateAsync(
            update,
            Arg.Any<Func<IProgress<double>, CancellationToken, Task<string>>>(),
            null);
        appUpdateService.Received(1).DeleteInstaller(installerPath);
        lifecycleService.DidNotReceive().LaunchUpdateInstallerAndShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenDownloadFails_ShowsErrorAndDoesNotLaunch()
    {
        var update = CreateAvailableUpdate();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.Yes);
        dialogService
            .ShowDownloadUpdateAsync(
                Arg.Any<AppUpdateCheckResult>(),
                Arg.Any<Func<IProgress<double>, CancellationToken, Task<string>>>(),
                Arg.Any<Window?>())
            .Returns(Task.FromException<string?>(new InvalidOperationException("download failed")));

        var appUpdateService = Substitute.For<IAppUpdateService>();
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        dialogService.Received(1).ShowError(
            Arg.Any<string>(),
            "Check for Updates",
            null,
            MessageBoxButton.OK);
        lifecycleService.DidNotReceive().LaunchUpdateInstallerAndShutdown(Arg.Any<string>());
    }

    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenLaunchFails_ShowsError()
    {
        const string installerPath = "C:\\temp\\fluxo-installer.exe";
        var update = CreateAvailableUpdate();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.Yes, MessageBoxResult.Yes);
        ConfigureDownloadPopupToReturn(dialogService, installerPath);

        var appUpdateService = Substitute.For<IAppUpdateService>();
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        lifecycleService
            .When(service => service.LaunchUpdateInstallerAndShutdown(installerPath))
            .Do(_ => throw new InvalidOperationException("launch failed"));
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        dialogService.Received(1).ShowError(
            Arg.Any<string>(),
            "Install Update",
            null,
            MessageBoxButton.OK);
    }

    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenBothPromptsAcceptedLaunchesInstallerOnce_AndDoesNotDeleteInstaller()
    {
        const string installerPath = "C:\\temp\\fluxo-installer.exe";
        var update = CreateAvailableUpdate();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.Yes, MessageBoxResult.Yes);
        ConfigureDownloadPopupToReturn(dialogService, installerPath);

        var appUpdateService = Substitute.For<IAppUpdateService>();
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        lifecycleService.Received(1).LaunchUpdateInstallerAndShutdown(installerPath);
        appUpdateService.DidNotReceive().DeleteInstaller(Arg.Any<string>());
    }

    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenMetadataMissing_HydratesAndLaunchesInstaller()
    {
        const string installerPath = "C:\\temp\\fluxo-installer.exe";
        var update = AppUpdateCheckResult.UpdateAvailable("2.5.0", string.Empty, string.Empty);
        var hydratedUpdate = AppUpdateCheckResult.UpdateAvailable(
            "2.5.1",
            "fluxo-2.5.1-Installer.exe",
            "https://example.test/fluxo-2.5.1-Installer.exe");
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.Yes, MessageBoxResult.Yes);
        ConfigureDownloadPopupToReturn(dialogService, installerPath);

        var appUpdateService = Substitute.For<IAppUpdateService>();
        appUpdateService
            .CheckForUpdatesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(hydratedUpdate);
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        await appUpdateService.Received(1).CheckForUpdatesAsync(
            AppVersionResolver.ResolveCurrentVersion(),
            Arg.Any<CancellationToken>());
        await dialogService.Received(1).ShowDownloadUpdateAsync(
            Arg.Is<AppUpdateCheckResult>(candidate =>
                candidate.LatestVersion == "2.5.0"
                && candidate.InstallerAssetName == hydratedUpdate.InstallerAssetName
                && candidate.InstallerDownloadUrl == hydratedUpdate.InstallerDownloadUrl),
            Arg.Any<Func<IProgress<double>, CancellationToken, Task<string>>>(),
            null);
        lifecycleService.Received(1).LaunchUpdateInstallerAndShutdown(installerPath);
        appUpdateService.DidNotReceive().DeleteInstaller(Arg.Any<string>());
    }

    [Fact]
    public async Task AppUpdateInteractionService_HandleAvailableUpdateAsync_WhenDownloadPopupRunsDelegate_DownloadsWithProgressAndCancellationToken()
    {
        const string installerPath = "C:\\temp\\fluxo-installer.exe";
        var update = CreateAvailableUpdate();
        using var cancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<double>();
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Window?>(), Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.Yes, MessageBoxResult.Yes);
        dialogService
            .ShowDownloadUpdateAsync(
                update,
                Arg.Any<Func<IProgress<double>, CancellationToken, Task<string>>>(),
                Arg.Any<Window?>())
            .Returns(callInfo =>
            {
                var download = callInfo.ArgAt<Func<IProgress<double>, CancellationToken, Task<string>>>(1);
                return AwaitNullableAsync(download(progress, cancellationTokenSource.Token));
            });

        var appUpdateService = Substitute.For<IAppUpdateService>();
        appUpdateService
            .DownloadInstallerAsync(
                update.InstallerDownloadUrl!,
                update.InstallerAssetName!,
                progress,
                cancellationTokenSource.Token)
            .Returns(installerPath);
        var lifecycleService = Substitute.For<IAppUpdateLifecycleService>();
        var sut = new AppUpdateInteractionService(dialogService, appUpdateService, lifecycleService);

        await sut.HandleAvailableUpdateAsync(update, owner: null);

        await appUpdateService.Received(1).DownloadInstallerAsync(
            update.InstallerDownloadUrl!,
            update.InstallerAssetName!,
            progress,
            cancellationTokenSource.Token);
        lifecycleService.Received(1).LaunchUpdateInstallerAndShutdown(installerPath);
    }

    [Fact]
    public void AppUpdateInteractionService_BuildAvailableUpdatePrompt_UsesLatestVersion()
    {
        var update = AppUpdateCheckResult.UpdateAvailable(
            "2.5.0",
            "fluxo-2.5.0-Installer.exe",
            "https://example.test/fluxo-2.5.0-Installer.exe");

        var prompt = AppUpdateInteractionService.BuildAvailableUpdatePrompt(update);

        Assert.Equal("Fluxo 2.5.0 is available. Download and install it?", prompt);
    }

    [Fact]
    public void AppUpdateInteractionService_BuildAvailableUpdatePrompt_FallsBackToUnknown_WhenLatestVersionMissing()
    {
        var update = AppUpdateCheckResult.UpdateAvailable(
            " ",
            "fluxo-2.5.0-Installer.exe",
            "https://example.test/fluxo-2.5.0-Installer.exe");

        var prompt = AppUpdateInteractionService.BuildAvailableUpdatePrompt(update);

        Assert.Equal("Fluxo Unknown is available. Download and install it?", prompt);
    }

    private static void ConfigureDownloadPopupToReturn(IDialogService dialogService, string installerPath)
    {
        dialogService
            .ShowDownloadUpdateAsync(
                Arg.Any<AppUpdateCheckResult>(),
                Arg.Any<Func<IProgress<double>, CancellationToken, Task<string>>>(),
                Arg.Any<Window?>())
            .Returns(installerPath);
    }

    private static async Task<string?> AwaitNullableAsync(Task<string> task)
    {
        return await task;
    }

    private static AppUpdateCheckResult CreateAvailableUpdate()
    {
        return AppUpdateCheckResult.UpdateAvailable(
            "2.5.0",
            "fluxo-2.5.0-Installer.exe",
            "https://example.test/fluxo-2.5.0-Installer.exe");
    }
}
