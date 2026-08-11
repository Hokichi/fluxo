using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fluxo.Core.Entities;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;

namespace Fluxo.Services.Backups;

public sealed class UserBackupService(IAppDataService appData) : IUserBackupService
{
    internal const int CurrentSchemaVersion = 3;
    private const string DataRestorationTagName = "Data Restoration";
    private const string DataRestorationTagHex = "#e9c178";
    private static readonly HashSet<string> LegacyBudgetAllocationUserSettingNames = new(StringComparer.OrdinalIgnoreCase)
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

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string GetDefaultBackupDirectory() => BuildDefaultBackupDirectory();

    public string BuildDefaultBackupPath(DateTime timestamp)
    {
        return Path.Combine(BuildDefaultBackupDirectory(), BuildDefaultBackupFileName(timestamp));
    }

    public static string BuildDefaultBackupDirectory()
    {
        return Path.Combine(FluxoDbContextFactory.GetDatabaseDirectoryPath(), "user_backups");
    }

    public static string BuildDefaultBackupFileName(DateTime timestamp)
    {
        return $"fluxo_user-backup_{timestamp.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)}.json";
    }

    public async Task<UserBackupManifest> ReadManifestAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var document = await DeserializeDocumentAsync(stream, cancellationToken);

        if (document is null)
            throw new InvalidDataException("Backup file is empty or invalid.");

        if (document.IncludedEntities is null)
            throw new InvalidDataException("Backup file is missing includedEntities.");

        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported backup schema version {document.SchemaVersion}.");

        return new UserBackupManifest(
            document.SchemaVersion,
            document.CreatedAt,
            document.IncludedEntities.ToHashSet());
    }

    public async Task<UserBackupOperationResult> BackupAsync(UserBackupSelection selection, string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
                Directory.CreateDirectory(directoryPath);

            var document = new FluxoUserBackupDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                CreatedAt = DateTime.UtcNow,
                IncludedEntities = selection.Entities.ToList()
            };

            var sourceBackupIds = new Dictionary<int, int>();
            var tagBackupIds = new Dictionary<int, int>();
            var goalBackupIds = new Dictionary<int, int>();

            if (selection.Includes(DataManagementEntityKind.Accounts))
            {
                var sources = await appData.GetAccountsAsync(cancellationToken);

                var nextBackupId = document.Entities.Accounts.Count + 1;
                foreach (var source in sources)
                {
                    sourceBackupIds[source.Id] = nextBackupId;
                    nextBackupId++;
                }

                foreach (var source in sources)
                {
                    var backupId = sourceBackupIds[source.Id];
                    int? deductBackupId = source.DeductSource is null ||
                                          !sourceBackupIds.TryGetValue(source.DeductSource.Value, out var mappedDeductBackupId)
                        ? null
                        : mappedDeductBackupId;

                    document.Entities.Accounts.Add(new BackupAccount(
                        backupId,
                        source.Name,
                        source.AccountType.ToString(),
                        source.AccountLimit,
                        source.MaximumSpending,
                        source.MinimumPayment,
                        source.SpentAmount,
                        source.Balance,
                        source.MonthlyDueDate,
                        deductBackupId,
                        source.InterestRate,
                        source.PinnedOnUI,
                        source.IsEnabled,
                        source.IsForDeletion,
                        source.IsDefault));
                }
            }

            if (selection.Includes(DataManagementEntityKind.Tags))
                await AddSelectedTagsAsync(document, tagBackupIds, cancellationToken);
            else if (selection.Includes(DataManagementEntityKind.Expenses) ||
                     selection.Includes(DataManagementEntityKind.RecurringTransactions))
                AddDataRestorationTag(document, tagBackupIds);

            if (selection.Includes(DataManagementEntityKind.Goals))
                await AddSelectedGoalsAsync(document, goalBackupIds, cancellationToken);

            if (selection.Includes(DataManagementEntityKind.Expenses) || selection.Includes(DataManagementEntityKind.Incomes))
                await AddSelectedTransactionsAsync(document, selection, sourceBackupIds, tagBackupIds, cancellationToken);

            if (selection.Includes(DataManagementEntityKind.RecurringTransactions))
            {
                await AddSelectedRecurringTransactionsAsync(
                    document,
                    sourceBackupIds,
                    tagBackupIds,
                    goalBackupIds,
                    selection,
                    cancellationToken);
            }

            if (selection.Includes(DataManagementEntityKind.UserSettings))
                await AddSelectedUserSettingsAsync(document, cancellationToken);

            await using var stream = File.Create(filePath);
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
            return UserBackupOperationResult.Success();
        }
        catch (Exception exception)
        {
            return UserBackupOperationResult.Failure(exception.Message);
        }
    }

    public async Task<IReadOnlyList<UserBackupConflict>> FindAppendConflictsAsync(string filePath,
        UserBackupSelection selection, CancellationToken cancellationToken = default)
    {
        var document = await LoadDocumentAsync(filePath, cancellationToken);
        var conflicts = new List<UserBackupConflict>();

        if (selection.Includes(DataManagementEntityKind.Accounts))
        {
            var existing = (await appData.GetAccountsAsync(cancellationToken))
                .Select(source => source.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            conflicts.AddRange(document.Entities.Accounts
                .Where(source => existing.Contains(source.Name))
                .Select(source => new UserBackupConflict(
                    $"source:{source.BackupId}", DataManagementEntityKind.Accounts, source.Name)));
        }

        if (selection.Includes(DataManagementEntityKind.Goals))
        {
            var existing = (await appData.GetSavingGoalsAsync(cancellationToken))
                .Select(goal => goal.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            conflicts.AddRange(document.Entities.Goals
                .Where(goal => existing.Contains(goal.Name))
                .Select(goal => new UserBackupConflict(
                    $"goal:{goal.BackupId}", DataManagementEntityKind.Goals, goal.Name)));
        }

        if (selection.Includes(DataManagementEntityKind.Tags))
        {
            var existing = (await appData.GetTagsAsync(cancellationToken))
                .Select(tag => tag.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            conflicts.AddRange(document.Entities.Tags
                .Where(tag => existing.Contains(tag.Name))
                .Select(tag => new UserBackupConflict(
                    $"tag:{tag.BackupId}", DataManagementEntityKind.Tags, tag.Name)));
        }

        return conflicts;
    }

    public async Task<UserBackupOperationResult> AppendAsync(string filePath, UserBackupSelection selection,
        IReadOnlyDictionary<string, DataManagementConflictDecision> conflictDecisions,
        CancellationToken cancellationToken = default)
    {
        string? safetyBackupPath = null;
        try
        {
            var document = await LoadDocumentAsync(filePath, cancellationToken);
            safetyBackupPath = await CreateDatabaseSafetyBackupAsync(cancellationToken);

            var sourceIdMap = new Dictionary<int, int>();
            var tagIdMap = new Dictionary<int, int>();
            var goalIdMap = new Dictionary<int, int>();

            await AppendTagsAsync(document, selection, conflictDecisions, tagIdMap, cancellationToken);
            await AppendAccountsAsync(document, selection, conflictDecisions, sourceIdMap, cancellationToken);
            await AppendGoalsAsync(document, selection, conflictDecisions, goalIdMap, cancellationToken);
            await AppendTransactionsAsync(document, selection, sourceIdMap, tagIdMap, cancellationToken);
            await AppendRecurringTransactionsAsync(document, selection, sourceIdMap, tagIdMap, goalIdMap, cancellationToken);
            await AppendUserSettingsAsync(document, selection, cancellationToken);

            await appData.SaveChangesAsync(cancellationToken);
            return UserBackupOperationResult.Success(safetyBackupPath);
        }
        catch (Exception exception)
        {
            if (safetyBackupPath is not null)
            {
                try
                {
                    await RestoreDatabaseBackupAsync(FluxoDbContextFactory.GetDatabasePath(), safetyBackupPath);
                }
                catch (Exception restoreException)
                {
                    return UserBackupOperationResult.Failure(
                        $"{exception.Message} Rollback failed: {restoreException.Message}",
                        safetyBackupPath);
                }
            }

            return UserBackupOperationResult.Failure(exception.Message, safetyBackupPath);
        }
    }

    public async Task<UserBackupOperationResult> OverwriteAsync(string filePath, UserBackupSelection selection,
        CancellationToken cancellationToken = default)
    {
        string? safetyBackupPath = null;
        try
        {
            var document = await LoadDocumentAsync(filePath, cancellationToken);
            safetyBackupPath = await CreateDatabaseSafetyBackupAsync(cancellationToken);

            await RemoveSelectedDataAsync(selection, cancellationToken);

            var sourceIdMap = new Dictionary<int, int>();
            var tagIdMap = new Dictionary<int, int>();
            var goalIdMap = new Dictionary<int, int>();
            var overwriteConflictDecisions = new Dictionary<string, DataManagementConflictDecision>();

            await AppendTagsAsync(document, selection, overwriteConflictDecisions, tagIdMap, cancellationToken);
            await AppendAccountsAsync(document, selection, overwriteConflictDecisions, sourceIdMap, cancellationToken);
            await AppendGoalsAsync(document, selection, overwriteConflictDecisions, goalIdMap, cancellationToken);
            await AppendTransactionsAsync(document, selection, sourceIdMap, tagIdMap, cancellationToken);
            await AppendRecurringTransactionsAsync(document, selection, sourceIdMap, tagIdMap, goalIdMap, cancellationToken);
            await AppendUserSettingsAsync(document, selection, cancellationToken);

            await appData.SaveChangesAsync(cancellationToken);
            return UserBackupOperationResult.Success(safetyBackupPath);
        }
        catch (Exception exception)
        {
            if (safetyBackupPath is not null)
            {
                try
                {
                    await RestoreDatabaseBackupAsync(FluxoDbContextFactory.GetDatabasePath(), safetyBackupPath);
                }
                catch (Exception restoreException)
                {
                    return UserBackupOperationResult.Failure(
                        $"{exception.Message} Rollback failed: {restoreException.Message}",
                        safetyBackupPath);
                }
            }

            return UserBackupOperationResult.Failure(exception.Message, safetyBackupPath);
        }
    }

    public static List<string> BuildOverwriteRemovalLabels(UserBackupSelection selection)
    {
        var labels = new List<string>();
        var includeRecurringTransactions =
            selection.Includes(DataManagementEntityKind.RecurringTransactions) ||
            selection.Includes(DataManagementEntityKind.Tags) ||
            selection.Includes(DataManagementEntityKind.Goals) ||
            selection.Includes(DataManagementEntityKind.Accounts);

        // Recurring transactions can depend on tags/goals/sources.
        // When any principal is selected for overwrite, clear recurring first to avoid FK conflicts.
        if (includeRecurringTransactions)
            labels.Add("RecurringTransactions");

        var includeExpensesOrLogs =
            selection.Includes(DataManagementEntityKind.Expenses) ||
            selection.Includes(DataManagementEntityKind.Accounts);

        // Spending source overwrite requires clearing dependent expenses/logs first.
        if (includeExpensesOrLogs)
        {
            labels.Add("ExpenseLogs");
            labels.Add("Expenses");
        }

        if (selection.Includes(DataManagementEntityKind.Incomes) ||
            selection.Includes(DataManagementEntityKind.Accounts))
            labels.Add("IncomeLogs");

        if (selection.Includes(DataManagementEntityKind.Accounts))
            labels.Add("Accounts");

        if (selection.Includes(DataManagementEntityKind.Goals))
            labels.Add("SavingGoals");

        if (selection.Includes(DataManagementEntityKind.Tags))
            labels.Add("Tags");

        if (selection.Includes(DataManagementEntityKind.UserSettings))
            labels.Add("UserSettings");

        return labels;
    }

    public static string BuildSafetyBackupPath(DateTime timestamp)
    {
        var backupDirectory = Path.Combine(FluxoDbContextFactory.GetDatabaseDirectoryPath(), "backup");
        Directory.CreateDirectory(backupDirectory);
        return Path.Combine(backupDirectory,
            $"data-management_{timestamp.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)}.db");
    }

    public static async Task<string?> CreateDatabaseSafetyBackupAsync(CancellationToken cancellationToken)
    {
        var databasePath = FluxoDbContextFactory.GetDatabasePath();
        if (!File.Exists(databasePath))
            return null;

        var safetyPath = BuildSafetyBackupPath(DateTime.Now);
        await using var source = File.Open(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        await using var destination = File.Create(safetyPath);
        await source.CopyToAsync(destination, cancellationToken);
        return safetyPath;
    }

    public static async Task RestoreDatabaseBackupAsync(string databasePath, string safetyBackupPath)
    {
        var databaseDirectory = Path.GetDirectoryName(databasePath);
        if (string.IsNullOrWhiteSpace(databaseDirectory))
            throw new InvalidOperationException("Database path must include a directory.");

        var tempRestorePath = Path.Combine(
            databaseDirectory,
            $"{Path.GetFileName(databasePath)}.{Guid.NewGuid():N}.restore.tmp");

        try
        {
            await using (var source = File.Open(safetyBackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var tempDestination = File.Open(tempRestorePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(tempDestination);
            }

            if (File.Exists(databasePath))
                File.Replace(tempRestorePath, databasePath, null);
            else
                File.Move(tempRestorePath, databasePath);
        }
        finally
        {
            if (File.Exists(tempRestorePath))
                File.Delete(tempRestorePath);
        }
    }

    private static DataManagementConflictDecision ResolveConflictDecision(
        IReadOnlyDictionary<string, DataManagementConflictDecision> conflictDecisions,
        string conflictKey,
        DataManagementEntityKind entityKind)
    {
        if (conflictDecisions.TryGetValue(conflictKey, out var decision))
            return decision;

        return entityKind == DataManagementEntityKind.Tags
            ? DataManagementConflictDecision.Ignore
            : DataManagementConflictDecision.Replace;
    }

    private async Task RemoveSelectedDataAsync(
        UserBackupSelection selection,
        CancellationToken cancellationToken)
    {
        var removalLabels = BuildOverwriteRemovalLabels(selection);
        foreach (var label in removalLabels)
        {
            switch (label)
            {
                case "RecurringTransactions":
                    await RemoveRecurringTransactionsAsync(cancellationToken);
                    break;
                case "ExpenseLogs":
                    await RemoveTransactionsAsync(TransactionType.Expense, cancellationToken);
                    break;
                case "Expenses":
                    break;
                case "IncomeLogs":
                    await RemoveTransactionsAsync(TransactionType.Income, cancellationToken);
                    break;
                case "Accounts":
                    await RemoveAccountsAsync(cancellationToken);
                    break;
                case "SavingGoals":
                    await RemoveSavingGoalsAsync(cancellationToken);
                    break;
                case "Tags":
                    await RemoveTagsAsync(cancellationToken);
                    break;
                case "UserSettings":
                    await RemoveUserSettingsAsync(cancellationToken);
                    break;
            }
        }
    }

    private async Task RemoveRecurringTransactionsAsync(CancellationToken cancellationToken)
    {
        var recurringTransactions = await appData.GetRecurringTransactionsAsync(cancellationToken);
        foreach (var recurringTransaction in recurringTransactions)
            appData.RemoveRecurringTransaction(recurringTransaction);
    }

    private async Task RemoveTransactionsAsync(TransactionType type, CancellationToken cancellationToken)
    {
        var transactions = await appData.GetTransactionsAsync(cancellationToken);
        foreach (var transaction in transactions.Where(item => item.Type == type))
            appData.RemoveTransaction(transaction);
    }

    private async Task RemoveAccountsAsync(CancellationToken cancellationToken)
    {
        var sources = await appData.GetAccountsAsync(cancellationToken);
        foreach (var source in sources)
            appData.RemoveAccount(source);
    }

    private async Task RemoveSavingGoalsAsync(CancellationToken cancellationToken)
    {
        var goals = await appData.GetSavingGoalsAsync(cancellationToken);
        foreach (var goal in goals)
            appData.RemoveSavingGoal(goal);
    }

    private async Task RemoveTagsAsync(CancellationToken cancellationToken)
    {
        var tags = await appData.GetTagsAsync(cancellationToken);
        foreach (var tag in tags)
            appData.RemoveTag(tag);
    }

    private async Task RemoveUserSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await appData.GetUserSettingsAsync(cancellationToken);
        foreach (var setting in settings)
            appData.RemoveUserSetting(setting);
    }

    private async Task<FluxoUserBackupDocument> LoadDocumentAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var document = await DeserializeDocumentAsync(stream, cancellationToken);

        if (document is null)
            throw new InvalidDataException("Backup file is empty or invalid.");

        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported backup schema version {document.SchemaVersion}.");

        if (document.IncludedEntities is null)
            throw new InvalidDataException("Backup file is missing includedEntities.");

        if (document.Entities is null)
            throw new InvalidDataException("Backup file is missing entities.");

        return document;
    }

    private static async Task<FluxoUserBackupDocument?> DeserializeDocumentAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var root = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject;
        if (root is null)
            return null;

        if (root["schemaVersion"]?.GetValue<int>() == 2)
            UpgradeVersionTwoBackup(root);

        return root.Deserialize<FluxoUserBackupDocument>(JsonOptions);
    }

    private static void UpgradeVersionTwoBackup(JsonObject root)
    {
        var entities = root["entities"] as JsonObject;
        var transactions = entities?["transactions"] as JsonArray;
        var parentIds = transactions?
            .OfType<JsonObject>()
            .Select(transaction => transaction["parentTransactionBackupId"]?.GetValue<int?>())
            .Where(parentId => parentId.HasValue)
            .Select(parentId => parentId!.Value)
            .ToHashSet() ?? [];

        foreach (var transaction in transactions?.OfType<JsonObject>() ?? [])
        {
            var backupId = transaction["backupId"]?.GetValue<int>() ?? 0;
            var isLeaf = !parentIds.Contains(backupId);
            var isExcluded = transaction["isExcludedFromBudget"]?.GetValue<bool>() == true;
            var isIncome = string.Equals(transaction["type"]?.GetValue<string>(), nameof(TransactionType.Income),
                StringComparison.OrdinalIgnoreCase);
            var isIoU = transaction["isIoU"]?.GetValue<bool>() == true;

            if (!isLeaf)
                transaction["expenseCategory"] = null;
            else if (isExcluded || isIncome || isIoU)
                transaction["expenseCategory"] = nameof(ExpenseCategory.Excluded);

            transaction.Remove("isExcludedFromBudget");
        }

        foreach (var recurring in (entities?["recurringTransactions"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var isExcluded = recurring["isExcludedFromBudget"]?.GetValue<bool>() == true;
            var isIncome = string.Equals(recurring["type"]?.GetValue<string>(),
                nameof(RecurringTransactionType.Income), StringComparison.OrdinalIgnoreCase);
            var isGoal = recurring["goalBackupId"] is not null;
            recurring["category"] = isExcluded || isIncome || isGoal
                ? nameof(ExpenseCategory.Excluded)
                : nameof(ExpenseCategory.Needs);
            recurring.Remove("isExcludedFromBudget");
        }

        root["schemaVersion"] = CurrentSchemaVersion;
    }

    private async Task AppendTagsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        IReadOnlyDictionary<string, DataManagementConflictDecision> conflictDecisions,
        Dictionary<int, int> tagIdMap,
        CancellationToken cancellationToken)
    {
        if (!selection.Includes(DataManagementEntityKind.Tags))
        {
            if (selection.Includes(DataManagementEntityKind.Expenses) ||
                selection.Includes(DataManagementEntityKind.RecurringTransactions))
            {
                var availableTags = await appData.GetTagsAsync(cancellationToken);
                var availableTagsByName = availableTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);
                var restorationTagId = await EnsureDataRestorationTagIdAsync(availableTagsByName, cancellationToken);
                foreach (var backupTag in document.Entities.Tags)
                    tagIdMap[backupTag.BackupId] = restorationTagId;
            }

            return;
        }

        var existingTags = await appData.GetTagsAsync(cancellationToken);
        var existingByName = existingTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var backupTag in document.Entities.Tags)
        {
            if (existingByName.TryGetValue(backupTag.Name, out var existingTag))
            {
                var decision = ResolveConflictDecision(
                    conflictDecisions,
                    $"tag:{backupTag.BackupId}",
                    DataManagementEntityKind.Tags);

                if (decision == DataManagementConflictDecision.Append)
                {
                    var newTag = new Tag
                    {
                        Name = backupTag.Name,
                        HexCode = backupTag.HexCode,
                        IsSystemTag = backupTag.IsSystemTag,
                        SpendingLimit = backupTag.SpendingLimit
                    };

                    await appData.AddTagAsync(newTag, cancellationToken);
                    tagIdMap[backupTag.BackupId] = newTag.Id;
                    existingByName[newTag.Name] = newTag;
                    continue;
                }

                if (decision == DataManagementConflictDecision.Replace)
                {
                    existingTag.HexCode = backupTag.HexCode;
                    existingTag.IsSystemTag = backupTag.IsSystemTag;
                    existingTag.SpendingLimit = backupTag.SpendingLimit;
                    appData.UpdateTag(existingTag);
                }

                tagIdMap[backupTag.BackupId] = existingTag.Id;
                continue;
            }

            var insertedTag = new Tag
            {
                Name = backupTag.Name,
                HexCode = backupTag.HexCode,
                IsSystemTag = backupTag.IsSystemTag,
                SpendingLimit = backupTag.SpendingLimit
            };

            await appData.AddTagAsync(insertedTag, cancellationToken);
            tagIdMap[backupTag.BackupId] = insertedTag.Id;
            existingByName[insertedTag.Name] = insertedTag;
        }
    }

    private async Task AppendAccountsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        IReadOnlyDictionary<string, DataManagementConflictDecision> conflictDecisions,
        Dictionary<int, int> sourceIdMap,
        CancellationToken cancellationToken)
    {
        if (!selection.Includes(DataManagementEntityKind.Accounts))
            return;

        var existingSources = await appData.GetAccountsAsync(cancellationToken);
        var existingByName = existingSources.ToDictionary(source => source.Name, StringComparer.OrdinalIgnoreCase);
        var pendingDeductMappings = new List<(Account Source, int? DeductBackupId)>();

        void SetDefault(Account target, bool isDefault)
        {
            if (isDefault)
            {
                foreach (var other in existingByName.Values.Where(source => source.IsDefault && !ReferenceEquals(source, target)))
                {
                    other.IsDefault = false;
                    appData.UpdateAccount(other);
                }
            }

            target.IsDefault = isDefault;
        }

        foreach (var backupSource in document.Entities.Accounts)
        {
            if (existingByName.TryGetValue(backupSource.Name, out var existingSource))
            {
                var decision = ResolveConflictDecision(
                    conflictDecisions,
                    $"source:{backupSource.BackupId}",
                    DataManagementEntityKind.Accounts);

                if (decision == DataManagementConflictDecision.Append)
                {
                    existingSource.SpentAmount += backupSource.SpentAmount;
                    existingSource.Balance += backupSource.Balance;
                    appData.UpdateAccount(existingSource);
                    sourceIdMap[backupSource.BackupId] = existingSource.Id;
                    continue;
                }

                if (decision == DataManagementConflictDecision.Replace)
                {
                    existingSource.AccountType = ParseEnumValue<AccountType>(backupSource.AccountType, "accountType");
                    existingSource.AccountLimit = backupSource.AccountLimit;
                    existingSource.MaximumSpending = backupSource.MaximumSpending;
                    existingSource.MinimumPayment = backupSource.MinimumPayment;
                    existingSource.SpentAmount = backupSource.SpentAmount;
                    existingSource.Balance = backupSource.Balance;
                    existingSource.MonthlyDueDate = backupSource.MonthlyDueDate;
                    existingSource.DeductSource = null;
                    existingSource.InterestRate = backupSource.InterestRate;
                    existingSource.PinnedOnUI = backupSource.RestoredPinnedOnUI;
                    existingSource.IsEnabled = backupSource.IsEnabled;
                    existingSource.IsForDeletion = backupSource.IsForDeletion;
                    SetDefault(existingSource, backupSource.IsDefault);
                    appData.UpdateAccount(existingSource);
                    pendingDeductMappings.Add((existingSource, backupSource.DeductSourceBackupId));
                }

                sourceIdMap[backupSource.BackupId] = existingSource.Id;
                continue;
            }

            var insertedSource = new Account
            {
                Name = backupSource.Name,
                AccountType = ParseEnumValue<AccountType>(backupSource.AccountType, "accountType"),
                AccountLimit = backupSource.AccountLimit,
                MaximumSpending = backupSource.MaximumSpending,
                MinimumPayment = backupSource.MinimumPayment,
                SpentAmount = backupSource.SpentAmount,
                Balance = backupSource.Balance,
                MonthlyDueDate = backupSource.MonthlyDueDate,
                DeductSource = null,
                InterestRate = backupSource.InterestRate,
                PinnedOnUI = backupSource.RestoredPinnedOnUI,
                IsEnabled = backupSource.IsEnabled,
                IsForDeletion = backupSource.IsForDeletion
            };

            SetDefault(insertedSource, backupSource.IsDefault);

            await appData.AddAccountAsync(insertedSource, cancellationToken);
            sourceIdMap[backupSource.BackupId] = insertedSource.Id;
            existingByName[insertedSource.Name] = insertedSource;
            pendingDeductMappings.Add((insertedSource, backupSource.DeductSourceBackupId));
        }

        foreach (var pending in pendingDeductMappings)
        {
            pending.Source.DeductSource = pending.DeductBackupId is not null &&
                                          sourceIdMap.TryGetValue(pending.DeductBackupId.Value, out var mappedDeductSourceId)
                ? mappedDeductSourceId
                : null;
            appData.UpdateAccount(pending.Source);
        }
    }

    private async Task AppendGoalsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        IReadOnlyDictionary<string, DataManagementConflictDecision> conflictDecisions,
        Dictionary<int, int> goalIdMap,
        CancellationToken cancellationToken)
    {
        if (!selection.Includes(DataManagementEntityKind.Goals))
            return;

        var existingGoals = await appData.GetSavingGoalsAsync(cancellationToken);
        var existingByName = existingGoals.ToDictionary(goal => goal.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var backupGoal in document.Entities.Goals)
        {
            if (existingByName.TryGetValue(backupGoal.Name, out var existingGoal))
            {
                var decision = ResolveConflictDecision(
                    conflictDecisions,
                    $"goal:{backupGoal.BackupId}",
                    DataManagementEntityKind.Goals);

                if (decision == DataManagementConflictDecision.Append)
                {
                    existingGoal.CurrentAmount += backupGoal.CurrentAmount;
                    appData.UpdateSavingGoal(existingGoal);
                    goalIdMap[backupGoal.BackupId] = existingGoal.Id;
                    continue;
                }

                if (decision == DataManagementConflictDecision.Replace)
                {
                    existingGoal.TargetAmount = backupGoal.TargetAmount;
                    existingGoal.CurrentAmount = backupGoal.CurrentAmount;
                    existingGoal.SavingEndDate = backupGoal.SavingEndDate;
                    existingGoal.CreatedOn = backupGoal.CreatedOn;
                    appData.UpdateSavingGoal(existingGoal);
                }

                goalIdMap[backupGoal.BackupId] = existingGoal.Id;
                continue;
            }

            var insertedGoal = new SavingGoal
            {
                Name = backupGoal.Name,
                TargetAmount = backupGoal.TargetAmount,
                CurrentAmount = backupGoal.CurrentAmount,
                SavingEndDate = backupGoal.SavingEndDate,
                CreatedOn = backupGoal.CreatedOn
            };

            await appData.AddSavingGoalAsync(insertedGoal, cancellationToken);
            goalIdMap[backupGoal.BackupId] = insertedGoal.Id;
            existingByName[insertedGoal.Name] = insertedGoal;
        }
    }

    private async Task AppendRecurringTransactionsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        Dictionary<int, int> sourceIdMap,
        Dictionary<int, int> tagIdMap,
        Dictionary<int, int> goalIdMap,
        CancellationToken cancellationToken)
    {
        if (!selection.Includes(DataManagementEntityKind.RecurringTransactions))
            return;

        var backupTagsById = document.Entities.Tags.ToDictionary(tag => tag.BackupId);
        var existingTags = await appData.GetTagsAsync(cancellationToken);
        var existingTagsByName = existingTags.ToDictionary(tag => tag.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var backupRecurring in document.Entities.RecurringTransactions)
        {
            if (!sourceIdMap.TryGetValue(backupRecurring.SourceBackupId, out var sourceId))
                continue;

            int? tagId = null;
            if (backupRecurring.TagBackupId is not null)
            {
                var backupTagId = backupRecurring.TagBackupId.Value;
                if (tagIdMap.TryGetValue(backupTagId, out var mappedTagId) ||
                    TryMapTagFromBackup(backupTagId, backupTagsById, existingTagsByName, tagIdMap, out mappedTagId))
                {
                    tagId = mappedTagId;
                }
                else
                {
                    tagId = await EnsureDataRestorationTagIdAsync(existingTagsByName, cancellationToken);
                    tagIdMap[backupTagId] = tagId.Value;
                }
            }

            int? goalId = null;
            if (backupRecurring.GoalBackupId is not null)
            {
                if (!goalIdMap.TryGetValue(backupRecurring.GoalBackupId.Value, out var mappedGoalId))
                    continue;
                goalId = mappedGoalId;
            }

            var recurringType = ParseEnumValue<RecurringTransactionType>(backupRecurring.Type, "type");
            var category = ParseOptionalExpenseCategory(backupRecurring.Category);

            await appData.AddRecurringTransactionAsync(new RecurringTransaction
            {
                Name = backupRecurring.Name,
                Amount = backupRecurring.Amount,
                RecurringPeriod = ParseEnumValue<RecurringPeriod>(backupRecurring.RecurringPeriod, "recurringPeriod"),
                RecurringTime = backupRecurring.RecurringTime,
                Type = recurringType,
                Category = recurringType is RecurringTransactionType.Income or RecurringTransactionType.GoalUpdate ||
                           goalId is not null
                    ? ExpenseCategory.Excluded
                    : category ?? ExpenseCategory.Needs,
                SourceId = sourceId,
                TagId = tagId,
                GoalId = goalId,
                IsEnabled = backupRecurring.IsEnabled,
                EndDate = backupRecurring.EndDate
            }, cancellationToken);
        }
    }

    private async Task AppendUserSettingsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        CancellationToken cancellationToken)
    {
        if (!selection.Includes(DataManagementEntityKind.UserSettings))
            return;

        var existingSettings = await appData.GetUserSettingsAsync(cancellationToken);
        var existingByName = existingSettings.ToDictionary(setting => setting.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var backupSetting in document.Entities.UserSettings)
        {
            if (IsLegacyBudgetAllocationUserSetting(backupSetting.Name))
                continue;

            if (existingByName.TryGetValue(backupSetting.Name, out var existingSetting))
            {
                existingSetting.Value = backupSetting.Value;
                appData.UpdateUserSetting(existingSetting);
                continue;
            }

            await appData.AddUserSettingAsync(new UserSettings
            {
                Name = backupSetting.Name,
                Value = backupSetting.Value
            }, cancellationToken);
        }
    }

    private static bool TryMapTagFromBackup(
        int backupTagId,
        IReadOnlyDictionary<int, BackupTag> backupTagsById,
        IReadOnlyDictionary<string, Tag> existingTagsByName,
        IDictionary<int, int> tagIdMap,
        out int tagId)
    {
        if (!backupTagsById.TryGetValue(backupTagId, out var backupTag))
        {
            tagId = default;
            return false;
        }

        if (!existingTagsByName.TryGetValue(backupTag.Name, out var existingTag))
        {
            tagId = default;
            return false;
        }

        tagId = existingTag.Id;
        tagIdMap[backupTagId] = tagId;
        return true;
    }

    private async Task<int> EnsureDataRestorationTagIdAsync(
        IDictionary<string, Tag> existingTagsByName,
        CancellationToken cancellationToken)
    {
        if (existingTagsByName.TryGetValue(DataRestorationTagName, out var existingTag))
            return existingTag.Id;

        var dataRestorationTag = new Tag
        {
            Name = DataRestorationTagName,
            HexCode = DataRestorationTagHex,
            IsSystemTag = true
        };

        await appData.AddTagAsync(dataRestorationTag, cancellationToken);
        existingTagsByName[dataRestorationTag.Name] = dataRestorationTag;
        return dataRestorationTag.Id;
    }

    private static TEnum ParseEnumValue<TEnum>(string value, string fieldName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;

        throw new InvalidDataException($"Invalid {fieldName} value '{value}'.");
    }

    private static ExpenseCategory? ParseOptionalExpenseCategory(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParseEnumValue<ExpenseCategory>(value, "expenseCategory");
    }

    private async Task AddSelectedTagsAsync(
        FluxoUserBackupDocument document,
        Dictionary<int, int> tagBackupIds,
        CancellationToken cancellationToken)
    {
        var tags = await appData.GetTagsAsync(cancellationToken);
        foreach (var tag in tags)
        {
            var backupId = document.Entities.Tags.Count + 1;
            tagBackupIds[tag.Id] = backupId;
            document.Entities.Tags.Add(new BackupTag(
                backupId,
                tag.Name,
                tag.HexCode,
                tag.IsSystemTag,
                tag.SpendingLimit));
        }
    }

    private static void AddDataRestorationTag(
        FluxoUserBackupDocument document,
        Dictionary<int, int> tagBackupIds)
    {
        var backupId = document.Entities.Tags.Count + 1;
        document.Entities.Tags.Add(new BackupTag(
            backupId,
            DataRestorationTagName,
            DataRestorationTagHex,
            true));
        tagBackupIds[-1] = backupId;
    }

    private async Task AddSelectedGoalsAsync(
        FluxoUserBackupDocument document,
        Dictionary<int, int> goalBackupIds,
        CancellationToken cancellationToken)
    {
        var goals = await appData.GetSavingGoalsAsync(cancellationToken);
        foreach (var goal in goals)
        {
            var backupId = document.Entities.Goals.Count + 1;
            goalBackupIds[goal.Id] = backupId;
            document.Entities.Goals.Add(new BackupSavingGoal(
                backupId,
                goal.Name,
                goal.TargetAmount,
                goal.CurrentAmount,
                goal.SavingEndDate,
                goal.CreatedOn));
        }
    }

    private async Task AddSelectedTransactionsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        Dictionary<int, int> sourceBackupIds,
        Dictionary<int, int> tagBackupIds,
        CancellationToken cancellationToken)
    {
        var transactions = (await appData.GetTransactionsAsync(cancellationToken))
            .Where(transaction => transaction.Type == TransactionType.Expense
                ? selection.Includes(DataManagementEntityKind.Expenses)
                : selection.Includes(DataManagementEntityKind.Incomes))
            .ToList();
        var backupIds = transactions.Select((transaction, index) => (transaction.Id, BackupId: index + 1))
            .ToDictionary(item => item.Id, item => item.BackupId);

        foreach (var transaction in transactions)
        {
            if (!sourceBackupIds.TryGetValue(transaction.SourceAccountId, out var accountBackupId))
                continue;

            int? tagBackupId = transaction.TagId is { } tagId && tagBackupIds.TryGetValue(tagId, out var mappedTagId)
                ? mappedTagId
                : transaction.Type == TransactionType.Expense && tagBackupIds.TryGetValue(-1, out var restorationTagId)
                    ? restorationTagId
                    : null;
            int? parentBackupId = transaction.ParentTransactionId is { } parentId && backupIds.TryGetValue(parentId, out var mappedParentId)
                ? mappedParentId
                : null;

            document.Entities.Transactions.Add(new BackupTransaction(
                backupIds[transaction.Id],
                transaction.Type.ToString(),
                accountBackupId,
                transaction.Name,
                transaction.Amount,
                transaction.OccurredOn,
                transaction.Notes,
                transaction.ExpenseCategory?.ToString(),
                tagBackupId,
                parentBackupId,
                transaction.IsPinned,
                transaction.IsForDeletion,
                transaction.IsIoU,
                transaction.ShouldAffectBalance));
        }
    }

    private async Task AppendTransactionsAsync(
        FluxoUserBackupDocument document,
        UserBackupSelection selection,
        Dictionary<int, int> sourceIdMap,
        Dictionary<int, int> tagIdMap,
        CancellationToken cancellationToken)
    {
        var restored = new Dictionary<int, Transaction>();
        var parentBackupIds = document.Entities.Transactions
            .Where(transaction => transaction.ParentTransactionBackupId.HasValue)
            .Select(transaction => transaction.ParentTransactionBackupId!.Value)
            .ToHashSet();
        foreach (var backup in document.Entities.Transactions)
        {
            var type = ParseEnumValue<TransactionType>(backup.Type, "type");
            if ((type == TransactionType.Expense && !selection.Includes(DataManagementEntityKind.Expenses)) ||
                (type == TransactionType.Income && !selection.Includes(DataManagementEntityKind.Incomes)) ||
                !sourceIdMap.TryGetValue(backup.AccountBackupId, out var accountId))
                continue;

            var category = ParseOptionalExpenseCategory(backup.ExpenseCategory);
            ExpenseCategory? normalizedCategory = parentBackupIds.Contains(backup.BackupId)
                ? null
                : type == TransactionType.Income || backup.IsIoU
                    ? ExpenseCategory.Excluded
                    : category ?? ExpenseCategory.Needs;

            var transaction = new Transaction
            {
                Type = type,
                SourceAccountId = accountId,
                Name = backup.Name,
                Amount = backup.Amount,
                OccurredOn = backup.OccurredOn,
                Notes = backup.Notes,
                ExpenseCategory = normalizedCategory,
                TagId = backup.TagBackupId is { } tagBackupId && tagIdMap.TryGetValue(tagBackupId, out var tagId) ? tagId : null,
                IsPinned = backup.IsPinned,
                IsForDeletion = backup.IsForDeletion,
                IsIoU = backup.IsIoU,
                ShouldAffectBalance = backup.RestoredShouldAffectBalance
            };
            await appData.AddTransactionAsync(transaction, cancellationToken);
            restored[backup.BackupId] = transaction;
        }

        await appData.SaveChangesAsync(cancellationToken);
        foreach (var backup in document.Entities.Transactions)
        {
            if (backup.ParentTransactionBackupId is not { } parentBackupId ||
                !restored.TryGetValue(backup.BackupId, out var transaction) ||
                !restored.TryGetValue(parentBackupId, out var parent))
                continue;

            transaction.ParentTransactionId = parent.Id;
            appData.UpdateTransaction(transaction);
        }
    }

    private async Task AddSelectedRecurringTransactionsAsync(
        FluxoUserBackupDocument document,
        Dictionary<int, int> sourceBackupIds,
        Dictionary<int, int> tagBackupIds,
        Dictionary<int, int> goalBackupIds,
        UserBackupSelection selection,
        CancellationToken cancellationToken)
    {
        tagBackupIds.TryGetValue(-1, out var dataRestorationBackupId);

        var transactions = await appData.GetRecurringTransactionsAsync(cancellationToken);
        foreach (var transaction in transactions)
        {
            if (!sourceBackupIds.TryGetValue(transaction.SourceId, out var sourceBackupId))
                continue;

            int? tagBackupId = null;
            if (transaction.TagId is not null)
            {
                var hasTag = tagBackupIds.TryGetValue(transaction.TagId.Value, out var selectedTagBackupId);
                if (hasTag)
                    tagBackupId = selectedTagBackupId;
                else if (dataRestorationBackupId > 0)
                    tagBackupId = dataRestorationBackupId;
            }

            int? goalBackupId = null;
            if (transaction.GoalId is not null)
            {
                if (!selection.Includes(DataManagementEntityKind.Goals) ||
                    !goalBackupIds.TryGetValue(transaction.GoalId.Value, out var selectedGoalBackupId))
                {
                    continue;
                }

                goalBackupId = selectedGoalBackupId;
            }

            document.Entities.RecurringTransactions.Add(new BackupRecurringTransaction(
                document.Entities.RecurringTransactions.Count + 1,
                transaction.Name,
                transaction.Amount,
                transaction.RecurringPeriod.ToString(),
                transaction.RecurringTime,
                transaction.Type.ToString(),
                transaction.Category?.ToString(),
                sourceBackupId,
                tagBackupId,
                goalBackupId,
                transaction.IsEnabled,
                transaction.EndDate));
        }
    }

    private async Task AddSelectedUserSettingsAsync(
        FluxoUserBackupDocument document,
        CancellationToken cancellationToken)
    {
        var settings = await appData.GetUserSettingsAsync(cancellationToken);
        foreach (var setting in settings)
        {
            if (IsLegacyBudgetAllocationUserSetting(setting.Name))
                continue;

            document.Entities.UserSettings.Add(new BackupUserSetting(setting.Name, setting.Value));
        }
    }

    private static bool IsLegacyBudgetAllocationUserSetting(string name)
    {
        return LegacyBudgetAllocationUserSettingNames.Contains(name);
    }
}
