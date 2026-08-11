using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Services.Backups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Services.Backups;

public sealed class UserBackupServiceAppendTests
{
    [Fact]
    public void UserBackupServiceAppend_LegacyIoUWithoutBalanceFlag_RestoresAsPosted()
    {
        var backup = new BackupTransaction(
            1, "Expense", 2, "Loan", 10m, DateTime.Today, string.Empty,
            nameof(ExpenseCategory.Needs), 3, null, false, false, true, false);

        Assert.True(backup.RestoredShouldAffectBalance);
    }

    [Fact]
    public async Task UserBackupServiceAppend_BackupAsync_WithFileNameOnlyPath_WritesBackupWithoutDirectoryCreationFailure()
    {
        var appData = Substitute.For<IAppDataService>();
        var service = new UserBackupService(appData);
        var backupFileName = $"{Guid.NewGuid():N}.json";
        var tempWorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkingDirectory);
        var originalWorkingDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(tempWorkingDirectory);

            var result = await service.BackupAsync(
                new UserBackupSelection(new HashSet<DataManagementEntityKind>()),
                backupFileName);

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(tempWorkingDirectory, backupFileName)));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
            Directory.Delete(tempWorkingDirectory, true);
        }
    }

    [Fact]
    public async Task UserBackupServiceAppend_FindAppendConflictsAsync_DetectsSourceGoalAndTagNameConflicts()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns([new Account { Id = 1, Name = "Wallet" }]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>())
            .Returns([new SavingGoal { Id = 2, Name = "Trip" }]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns([new Tag { Id = 3, Name = "Food", HexCode = "#fff" }]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 2,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "accounts", "goals", "tags" ],
          "entities": {
            "accounts": [ { "backupId": 1, "name": "Wallet", "accountType": "Cash" } ],
            "goals": [ { "backupId": 1, "name": "Trip", "targetAmount": 100, "currentAmount": 1, "createdOn": "2026-05-26T00:00:00Z" } ],
            "tags": [ { "backupId": 1, "name": "Food", "hexCode": "#fff", "isSystemTag": false } ]
          }
        }
        """);

        try
        {
            var service = new UserBackupService(appData);
            var conflicts = await service.FindAppendConflictsAsync(tempFile,
                new UserBackupSelection(new HashSet<DataManagementEntityKind>
                {
                    DataManagementEntityKind.Accounts,
                    DataManagementEntityKind.Goals,
                    DataManagementEntityKind.Tags
                }));

            Assert.Contains(conflicts, conflict => conflict.EntityKind == DataManagementEntityKind.Accounts);
            Assert.Contains(conflicts, conflict => conflict.EntityKind == DataManagementEntityKind.Goals);
            Assert.Contains(conflicts, conflict => conflict.EntityKind == DataManagementEntityKind.Tags);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceAppend_AppendAsync_WhenTagsNotSelected_MapsUnresolvedExpenseAndRecurringTagsToDataRestorationTag()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Tag>)[]);
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Account>)[]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<SavingGoal>)[]);

        appData.AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<Tag>().Id = 77;
                return Task.CompletedTask;
            });

        var appendedExpenses = new List<Transaction>();
        appData.AddTransactionAsync(Arg.Any<Transaction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var expense = call.Arg<Transaction>();
                expense.Id = 100;
                appendedExpenses.Add(expense);
                return Task.CompletedTask;
            });

        var appendedRecurring = new List<RecurringTransaction>();
        appData.AddRecurringTransactionAsync(Arg.Any<RecurringTransaction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                appendedRecurring.Add(call.Arg<RecurringTransaction>());
                return Task.CompletedTask;
            });

        appData.AddAccountAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<Account>().Id = 10;
                return Task.CompletedTask;
            });

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 2,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "accounts", "expenses", "recurringTransactions", "tags" ],
          "entities": {
            "accounts": [
              { "backupId": 1, "name": "Wallet", "accountType": "Cash" }
            ],
            "tags": [
              { "backupId": 2, "name": "Missing Tag", "hexCode": "#123456", "isSystemTag": false }
            ],
            "transactions": [
              { "backupId": 3, "type": "Expense", "accountBackupId": 1, "tagBackupId": 2, "name": "Lunch", "amount": 10, "occurredOn": "2026-05-26T00:00:00Z", "notes": "", "expenseCategory": "Needs", "isPinned": false, "isForDeletion": false, "isIoU": false, "isExcludedFromBudget": false }
            ],
            "recurringTransactions": [
              { "backupId": 4, "name": "Gym", "amount": 40, "recurringPeriod": "Monthly", "recurringTime": 1, "type": "Expense", "sourceBackupId": 1, "tagBackupId": 2, "goalBackupId": null, "isEnabled": true }
            ]
          }
        }
        """);

        try
        {
            var service = new UserBackupService(appData);
            var result = await service.AppendAsync(
                tempFile,
                new UserBackupSelection(new HashSet<DataManagementEntityKind>
                {
                    DataManagementEntityKind.Accounts,
                    DataManagementEntityKind.Expenses,
                    DataManagementEntityKind.RecurringTransactions
                }),
                new Dictionary<string, DataManagementConflictDecision>());

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var appendedExpense = Assert.Single(appendedExpenses);
            Assert.Equal(77, appendedExpense.TagId);

            var recurring = Assert.Single(appendedRecurring);
            Assert.Equal(77, recurring.TagId);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceAppend_AppendAsync_WhenTagsSelected_RestoresSpendingLimit()
    {
        var appendedTags = new List<Tag>();
        var appData = Substitute.For<IAppDataService>();
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => appendedTags.ToList());
        appData.AddTagAsync(Arg.Any<Tag>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var tag = call.Arg<Tag>();
                tag.Id = 12;
                appendedTags.Add(tag);
                return Task.CompletedTask;
            });

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 2,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "tags" ],
          "entities": {
            "tags": [
              { "backupId": 1, "name": "Food", "hexCode": "#ffffff", "isSystemTag": false, "spendingLimit": 250 }
            ]
          }
        }
        """);

        try
        {
            var service = new UserBackupService(appData);
            var result = await service.AppendAsync(
                tempFile,
                new UserBackupSelection(new HashSet<DataManagementEntityKind>
                {
                    DataManagementEntityKind.Tags
                }),
                new Dictionary<string, DataManagementConflictDecision>());

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var tag = Assert.Single(appendedTags);
            Assert.Equal(250m, tag.SpendingLimit);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceAppend_AppendAsync_WithLegacySchema_ReturnsFailure()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Account>)[]);

        var appendedSources = new List<Account>();
        appData.AddAccountAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                appendedSources.Add(call.Arg<Account>());
                return Task.CompletedTask;
            });

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 1,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "accounts" ],
          "entities": {
            "accounts": [
              {
                "backupId": 1,
                "name": "Wallet",
                "accountType": "Cash",
                "accountLimit": 0,
                "maximumSpending": 0,
                "minimumPayment": null,
                "spentAmount": 0,
                "balance": 40,
                "monthlyDueDate": null,
                "deductSourceBackupId": null,
                "interestRate": null,
                "showOnUI": true,
                "isEnabled": true,
                "isForDeletion": false
              }
            ]
          }
        }
        """);

        try
        {
            var service = new UserBackupService(appData);
            var result = await service.AppendAsync(
                tempFile,
                new UserBackupSelection(new HashSet<DataManagementEntityKind>
                {
                    DataManagementEntityKind.Accounts
                }),
                new Dictionary<string, DataManagementConflictDecision>());

            Assert.False(result.IsSuccess);
            Assert.Contains("Unsupported backup schema version 1", result.ErrorMessage);
            Assert.Empty(appendedSources);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceAppend_AppendAsync_WhenUserSettingsSelected_SkipsLegacyBudgetAllocationSettings()
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetUserSettingsAsync(Arg.Any<CancellationToken>())
            .Returns([new UserSettings { Name = "PreferredDisplayName", Value = "Existing" }]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 2,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "userSettings" ],
          "entities": {
            "userSettings": [
              { "name": "NeedsThreshold", "value": "40" },
              { "name": "WantsThreshold", "value": "40" },
              { "name": "InvestThreshold", "value": "20" },
              { "name": "AllocationPeriod", "value": "Yearly" },
              { "name": "AllocationLimit", "value": "1000" },
              { "name": "RolloverPolicy", "value": "Pooled" },
              { "name": "OverspendPolicy", "value": "SoftDebt" },
              { "name": "NeedsDebt", "value": "5" },
              { "name": "WantsDebt", "value": "6" },
              { "name": "InvestDebt", "value": "7" },
              { "name": "PreferredDisplayName", "value": "Alex" }
            ]
          }
        }
        """);

        try
        {
            var service = new UserBackupService(appData);
            var result = await service.AppendAsync(
                tempFile,
                new UserBackupSelection(new HashSet<DataManagementEntityKind>
                {
                    DataManagementEntityKind.UserSettings
                }),
                new Dictionary<string, DataManagementConflictDecision>());

            Assert.True(result.IsSuccess, result.ErrorMessage);
            var legacyBudgetSettingNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "NeedsThreshold",
                "WantsThreshold",
                "InvestThreshold",
                "AllocationPeriod",
                "AllocationLimit",
                "RolloverPolicy",
                "OverspendPolicy",
                "NeedsDebt",
                "WantsDebt",
                "InvestDebt"
            };
            await appData.DidNotReceive().AddUserSettingAsync(
                Arg.Is<UserSettings>(setting => legacyBudgetSettingNames.Contains(setting.Name)),
                Arg.Any<CancellationToken>());
            appData.Received(1).UpdateUserSetting(Arg.Is<UserSettings>(setting =>
                setting.Name == "PreferredDisplayName" &&
                setting.Value == "Alex"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task UserBackupServiceAppend_AppendAsync_WithAppendDecisionOnSourceAndGoal_AddsAmountsToExistingEntities()
    {
        var existingSource = new Account
        {
            Id = 10,
            Name = "Wallet",
            SpentAmount = 25m,
            Balance = 100m
        };
        var existingGoal = new SavingGoal
        {
            Id = 20,
            Name = "Trip",
            TargetAmount = 500m,
            CurrentAmount = 120m
        };

        var appData = Substitute.For<IAppDataService>();
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns([existingSource]);
        appData.GetSavingGoalsAsync(Arg.Any<CancellationToken>())
            .Returns([existingGoal]);
        appData.GetTagsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => (IReadOnlyList<Tag>)[]);

        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, """
        {
          "schemaVersion": 2,
          "createdAt": "2026-05-26T00:00:00Z",
          "includedEntities": [ "accounts", "goals" ],
          "entities": {
            "accounts": [
              {
                "backupId": 1,
                "name": "Wallet",
                "accountType": "Cash",
                "accountLimit": 0,
                "maximumSpending": 0,
                "minimumPayment": null,
                "spentAmount": 5,
                "balance": 40,
                "monthlyDueDate": null,
                "deductSourceBackupId": null,
                "interestRate": null,
                "pinnedOnUI": true,
                "isEnabled": true,
                "isForDeletion": false
              }
            ],
            "goals": [
              {
                "backupId": 1,
                "name": "Trip",
                "targetAmount": 999,
                "currentAmount": 30,
                "savingEndDate": null,
                "createdOn": "2026-05-26T00:00:00Z"
              }
            ]
          }
        }
        """);

        try
        {
            var service = new UserBackupService(appData);
            var result = await service.AppendAsync(
                tempFile,
                new UserBackupSelection(new HashSet<DataManagementEntityKind>
                {
                    DataManagementEntityKind.Accounts,
                    DataManagementEntityKind.Goals
                }),
                new Dictionary<string, DataManagementConflictDecision>
                {
                    ["source:1"] = DataManagementConflictDecision.Append,
                    ["goal:1"] = DataManagementConflictDecision.Append
                });

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal(30m, existingSource.SpentAmount);
            Assert.Equal(140m, existingSource.Balance);
            Assert.Equal(150m, existingGoal.CurrentAmount);
            Assert.Equal(500m, existingGoal.TargetAmount);

            _ = appData.DidNotReceive().AddAccountAsync(Arg.Any<Account>(), Arg.Any<CancellationToken>());
            _ = appData.DidNotReceive().AddSavingGoalAsync(Arg.Any<SavingGoal>(), Arg.Any<CancellationToken>());
            appData.Received(1).UpdateAccount(existingSource);
            appData.Received(1).UpdateSavingGoal(existingGoal);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
