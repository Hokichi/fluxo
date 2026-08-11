using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Helpers.Popups;
using Xunit;

namespace Fluxo.Tests.ViewModels.Popups.Helpers;

public sealed class AccountDeletionConfirmationHelperTests
{
    [Fact]
    public void AccountDeletionConfirmationHelper_IsOnlyFunctioningSource_WhenSingleEnabledPositiveSource_ReturnsTrue()
    {
        var sources = new[]
        {
            CreateSource(1, "Main", AccountType.Checking, isEnabled: true, balance: 100m),
            CreateSource(2, "Backup", AccountType.Checking, isEnabled: true, balance: 0m)
        };

        var isOnlyFunctioning = AccountDeletionConfirmationHelper.IsOnlyFunctioningSource(sources, 1);

        Assert.True(isOnlyFunctioning);
    }

    [Fact]
    public void AccountDeletionConfirmationHelper_IsOnlyFunctioningSource_WhenMultipleEnabledPositiveSources_ReturnsFalse()
    {
        var sources = new[]
        {
            CreateSource(1, "Main", AccountType.Checking, isEnabled: true, balance: 100m),
            CreateSource(2, "Backup", AccountType.Cash, isEnabled: true, balance: 100m)
        };

        var isOnlyFunctioning = AccountDeletionConfirmationHelper.IsOnlyFunctioningSource(sources, 1);

        Assert.False(isOnlyFunctioning);
    }

    private static Account CreateSource(
        int id,
        string name,
        AccountType sourceType,
        bool isEnabled,
        decimal balance)
    {
        return new Account
        {
            Id = id,
            Name = name,
            AccountType = sourceType,
            Balance = balance,
            IsEnabled = isEnabled
        };
    }
}
