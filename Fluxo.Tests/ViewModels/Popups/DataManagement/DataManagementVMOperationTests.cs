using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups.DataManagement;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.DataManagement;

public sealed class DataManagementVMOperationTests
{
    [Fact]
    public async Task DataManagementVMOperation_StartAsync_BackupSuccess_ShowsBackupReady()
    {
        var service = Substitute.For<IUserBackupService>();
        service.BuildDefaultBackupPath(Arg.Any<DateTime>()).Returns("backup.json");
        service.BackupAsync(Arg.Any<UserBackupSelection>(), "backup.json", Arg.Any<CancellationToken>())
            .Returns(UserBackupOperationResult.Success());
        var vm = new DataManagementVM(service);

        await vm.StartAsync();

        Assert.Equal("Backup is ready", vm.ResultMessage);
        Assert.True(vm.IsResultSuccess);
        Assert.Equal(3, vm.PageIndex);
    }

    [Fact]
    public async Task DataManagementVMOperation_StartAsync_AppendWithConflicts_ShowsConflictPageBeforeApply()
    {
        var service = Substitute.For<IUserBackupService>();
        service.BuildDefaultBackupPath(Arg.Any<DateTime>()).Returns("backup.json");
        service.FindAppendConflictsAsync("backup.json", Arg.Any<UserBackupSelection>(), Arg.Any<CancellationToken>())
            .Returns([
                new UserBackupConflict("source:1", DataManagementEntityKind.Accounts, "Wallet")
            ]);

        var vm = new DataManagementVM(service)
        {
            Mode = DataManagementMode.Append
        };
        vm.FilePath = "backup.json";

        await vm.StartAsync();

        Assert.Equal(2, vm.PageIndex);
        Assert.Single(vm.Conflicts);
        await service.DidNotReceive().AppendAsync(
            Arg.Any<string>(),
            Arg.Any<UserBackupSelection>(),
            Arg.Any<IReadOnlyDictionary<string, DataManagementConflictDecision>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DataManagementVMOperation_StartAsync_OverwriteFailure_ShowsOverwriteFailed()
    {
        var service = Substitute.For<IUserBackupService>();
        service.BuildDefaultBackupPath(Arg.Any<DateTime>()).Returns("backup.json");
        service.OverwriteAsync("backup.json", Arg.Any<UserBackupSelection>(), Arg.Any<CancellationToken>())
            .Returns(UserBackupOperationResult.Failure("failed"));
        var vm = new DataManagementVM(service) { Mode = DataManagementMode.Overwrite };
        vm.FilePath = "backup.json";

        await vm.StartAsync();

        Assert.StartsWith("Data overwrite failed.", vm.ResultMessage);
        Assert.False(vm.IsResultSuccess);
        Assert.Equal(3, vm.PageIndex);
    }
}
