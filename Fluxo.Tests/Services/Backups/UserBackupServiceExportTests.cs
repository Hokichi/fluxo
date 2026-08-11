using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Backups;
using NSubstitute;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Fluxo.Tests.Services.Backups;

public sealed class UserBackupServiceExportTests
{
    private static readonly JsonSerializerOptions BackupJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task UserBackupServiceExport_BackupAsync_WhenTagsAreNotSelected_MapsExpensesToDataRestorationTag()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns([new Account { Id = 7, Name = "Wallet", AccountType = AccountType.Cash }]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns([new Tag { Id = 3, Name = "Food", HexCode = "#ffffff" }]);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>())
            .Returns([new Transaction { Id = 9, Type = TransactionType.Expense, SourceAccountId = 7, TagId = 3, Name = "Lunch" }]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var service = new UserBackupService(appData);

        try
        {
            var result = await service.BackupAsync(new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Expenses,
                DataManagementEntityKind.Accounts
            }), tempFile);

            Assert.True(result.IsSuccess);
            var json = await File.ReadAllTextAsync(tempFile);
            var document = JsonSerializer.Deserialize<FluxoUserBackupDocument>(json, BackupJsonOptions);
            Assert.NotNull(document);

            var dataRestorationTag = Assert.Single(document.Entities.Tags, tag => tag.Name == "Data Restoration");
            Assert.Equal("#e9c178", dataRestorationTag.HexCode);

            var expense = Assert.Single(document.Entities.Transactions);
            Assert.Equal(dataRestorationTag.BackupId, expense.TagBackupId);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_BackupAsync_WhenTagsSelected_IncludesSpendingLimit()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns([new Tag { Id = 3, Name = "Food", HexCode = "#ffffff", SpendingLimit = 250m }]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var service = new UserBackupService(appData);

        try
        {
            var result = await service.BackupAsync(new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Tags
            }), tempFile);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var json = await File.ReadAllTextAsync(tempFile);
            var document = JsonSerializer.Deserialize<FluxoUserBackupDocument>(json, BackupJsonOptions);
            Assert.NotNull(document);

            var tag = Assert.Single(document.Entities.Tags);
            Assert.Equal(250m, tag.SpendingLimit);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_BackupAsync_WhenExpenseLogsHaveParent_StoresParentLogBackupId()
    {
        var appData = Substitute.For<IAppDataService>();
        var account = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking };
        var tag = new Tag { Id = 3, Name = "Food", HexCode = "#ffffff" };
        var parentExpense = new Transaction
        {
            Id = 20,
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            TagId = tag.Id,
            Name = "Dinner",
            Amount = 100m,
            Tag = tag,
            Account = account
        };
        var childExpense = new Transaction
        {
            Id = 21,
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            TagId = tag.Id,
            Name = "Tip",
            Amount = 20m,
            Tag = tag,
            Account = account
        };
        childExpense.ParentTransactionId = parentExpense.Id;

        appData.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns([account]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns([tag]);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([parentExpense, childExpense]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var service = new UserBackupService(appData);

        try
        {
            var result = await service.BackupAsync(new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Accounts,
                DataManagementEntityKind.Tags,
                DataManagementEntityKind.Expenses
            }), tempFile);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var json = await File.ReadAllTextAsync(tempFile);
            var document = JsonSerializer.Deserialize<FluxoUserBackupDocument>(json, BackupJsonOptions);
            Assert.NotNull(document);

            var exportedChild = Assert.Single(document.Entities.Transactions, transaction => transaction.BackupId == 2);
            Assert.Equal(1, exportedChild.ParentTransactionBackupId);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_BackupAsync_WhenIoUFlagsSet_ExportsFlags()
    {
        var appData = Substitute.For<IAppDataService>();
        var account = new Account { Id = 1, Name = "Checking", AccountType = AccountType.Checking };
        var tag = new Tag { Id = 3, Name = "Food", HexCode = "#ffffff" };
        var expense = new Transaction
        {
            Id = 20,
            Type = TransactionType.Expense,
            SourceAccountId = account.Id,
            TagId = tag.Id,
            Name = "Loan",
            Amount = 100m,
            Tag = tag,
            Account = account,
            IsIoU = true,
            ShouldAffectBalance = true
        };
        var incomeLog = new Transaction
        {
            Id = 11,
            Type = TransactionType.Income,
            SourceAccountId = account.Id,
            Account = account,
            Name = "Borrowed cash",
            Amount = 50m,
            Notes = string.Empty,
            IsIoU = true
        };

        appData.GetAccountsAsync(Arg.Any<CancellationToken>()).Returns([account]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>()).Returns([tag]);
        appData.GetTransactionsAsync(Arg.Any<CancellationToken>()).Returns([expense, incomeLog]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var service = new UserBackupService(appData);

        try
        {
            var result = await service.BackupAsync(new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Accounts,
                DataManagementEntityKind.Tags,
                DataManagementEntityKind.Expenses,
                DataManagementEntityKind.Incomes
            }), tempFile);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var json = await File.ReadAllTextAsync(tempFile);
            var document = JsonSerializer.Deserialize<FluxoUserBackupDocument>(json, BackupJsonOptions);
            Assert.NotNull(document);

            Assert.All(document.Entities.Transactions, transaction => Assert.True(transaction.IsIoU));
            Assert.True(document.Entities.Transactions.Single(transaction => transaction.BackupId == 1).ShouldAffectBalance);
            Assert.False(document.Entities.Transactions.Single(transaction => transaction.BackupId == 2).ShouldAffectBalance);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_BackupAsync_WhenDeductSourceAppearsAfterDependent_MapsDeductSourceByBackupId()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns([
                new Account
                {
                    Id = 11,
                    Name = "Card A",
                    AccountType = AccountType.Credit,
                    DeductSource = 12
                },
                new Account
                {
                    Id = 12,
                    Name = "Wallet",
                    AccountType = AccountType.Cash,
                    IsDefault = true
                }
            ]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var service = new UserBackupService(appData);

        try
        {
            var result = await service.BackupAsync(new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.Accounts
            }), tempFile);

            Assert.True(result.IsSuccess);
            var json = await File.ReadAllTextAsync(tempFile);
            var document = JsonSerializer.Deserialize<FluxoUserBackupDocument>(json, BackupJsonOptions);
            Assert.NotNull(document);

            var dependentSource = Assert.Single(document.Entities.Accounts, source => source.Name == "Card A");
            var deductSource = Assert.Single(document.Entities.Accounts, source => source.Name == "Wallet");

            Assert.Equal(deductSource.BackupId, dependentSource.DeductSourceBackupId);
            Assert.True(deductSource.IsDefault);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_BackupAsync_WhenUserSettingsSelected_ExcludesLegacyBudgetAllocationSettings()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingsAsync(Arg.Any<CancellationToken>())
            .Returns([
                new UserSettings { Name = "NeedsThreshold", Value = "40" },
                new UserSettings { Name = "WantsThreshold", Value = "40" },
                new UserSettings { Name = "InvestThreshold", Value = "20" },
                new UserSettings { Name = "AllocationPeriod", Value = "Yearly" },
                new UserSettings { Name = "AllocationLimit", Value = "1000" },
                new UserSettings { Name = "RolloverPolicy", Value = "Pooled" },
                new UserSettings { Name = "OverspendPolicy", Value = "SoftDebt" },
                new UserSettings { Name = "NeedsDebt", Value = "5" },
                new UserSettings { Name = "WantsDebt", Value = "6" },
                new UserSettings { Name = "InvestDebt", Value = "7" },
                new UserSettings { Name = "PreferredDisplayName", Value = "Alex" }
            ]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var service = new UserBackupService(appData);

        try
        {
            var result = await service.BackupAsync(new UserBackupSelection(new HashSet<DataManagementEntityKind>
            {
                DataManagementEntityKind.UserSettings
            }), tempFile);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var json = await File.ReadAllTextAsync(tempFile);
            var document = JsonSerializer.Deserialize<FluxoUserBackupDocument>(json, BackupJsonOptions);
            Assert.NotNull(document);

            var setting = Assert.Single(document.Entities.UserSettings);
            Assert.Equal("PreferredDisplayName", setting.Name);
            Assert.Equal("Alex", setting.Value);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_ReadManifestAsync_ReturnsIncludedEntityKinds()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 2,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "expenses", "accounts" ],
          "entities": {}
        }
        """);

        try
        {
            var service = new UserBackupService(Substitute.For<IAppDataService>());
            var manifest = await service.ReadManifestAsync(tempFile);

            Assert.Contains(DataManagementEntityKind.Expenses, manifest.IncludedEntities);
            Assert.Contains(DataManagementEntityKind.Accounts, manifest.IncludedEntities);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceExport_ReadManifestAsync_WhenIncludedEntitiesIsNull_ThrowsInvalidDataException()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 1,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": null,
          "entities": {}
        }
        """);

        try
        {
            var service = new UserBackupService(Substitute.For<IAppDataService>());
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadManifestAsync(tempFile));
            Assert.Equal("Backup file is missing includedEntities.", exception.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
