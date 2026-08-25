using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Entities;
using Fluxo.Tests.ViewModels.Shell.Main;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups;

public sealed class TransferFundsVMPersistenceTests
{
    [Fact]
    public void SaveAsync_WhenInvalidationFailsAfterSave_ReturnsSuccess()
    {
        RunInSta(() =>
        {
            var sourceVm = new AccountVM
            {
                Id = 1,
                Name = "Checking",
                AccountType = AccountType.Checking,
                IsEnabled = true
            };
            var targetVm = new AccountVM
            {
                Id = 2,
                Name = "Savings",
                AccountType = AccountType.Saving,
                IsEnabled = true
            };
            var main = MainVMUserSettingsTests.CreateMainViewModel(Substitute.For<IUnitOfWork>());
            main.BudgetPanel.Accounts.Add(sourceVm);
            main.BudgetPanel.Accounts.Add(targetVm);

            var source = new Account
            {
                Id = sourceVm.Id,
                Name = sourceVm.Name,
                AccountType = sourceVm.AccountType,
                Balance = 100m,
                IsEnabled = true
            };
            var target = new Account
            {
                Id = targetVm.Id,
                Name = targetVm.Name,
                AccountType = targetVm.AccountType,
                Balance = 50m,
                IsEnabled = true
            };
            var appData = Substitute.For<IAppDataService>();
            appData.GetAccountByIdAsync(source.Id, Arg.Any<CancellationToken>()).Returns(source);
            appData.GetAccountByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(target);
            appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns(
                Task.FromResult<IReadOnlyList<Tag>>([new Tag { Id = 3, Name = "Transfer", HexCode = "#22C55E" }]));
            appData.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var vm = new TransferFundsVM(main, sourceVm, appData)
            {
                AmountText = 25m,
                SelectedTarget = targetVm
            };
            var recipient = new object();
            WeakReferenceMessenger.Default.Register<DashboardDataInvalidatedMessage>(
                recipient,
                (_, _) => throw new InvalidOperationException("invalidation failed"));

            try
            {
                var result = vm.SaveAsync().GetAwaiter().GetResult();

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(75m, source.Balance);
                Assert.Equal(75m, target.Balance);
            }
            finally
            {
                WeakReferenceMessenger.Default.UnregisterAll(recipient);
            }
        });
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }
}
