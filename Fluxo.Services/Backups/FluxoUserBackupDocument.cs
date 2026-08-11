using Fluxo.Core.Enums;
using System.Text.Json.Serialization;

namespace Fluxo.Services.Backups;

public sealed class FluxoUserBackupDocument
{
    public int SchemaVersion { get; set; } = 3;
    public DateTime CreatedAt { get; set; }
    public List<DataManagementEntityKind> IncludedEntities { get; set; } = [];
    public FluxoUserBackupEntities Entities { get; set; } = new();
}

public sealed class FluxoUserBackupEntities
{
    public List<BackupAccount> Accounts { get; set; } = [];
    public List<BackupTag> Tags { get; set; } = [];
    public List<BackupSavingGoal> Goals { get; set; } = [];
    public List<BackupTransaction> Transactions { get; set; } = [];
    [JsonIgnore] public List<BackupExpense> Expenses { get; set; } = [];
    [JsonIgnore] public List<BackupExpenseLog> ExpenseLogs { get; set; } = [];
    [JsonIgnore] public List<BackupIncomeLog> IncomeLogs { get; set; } = [];
    public List<BackupRecurringTransaction> RecurringTransactions { get; set; } = [];
    public List<BackupUserSetting> UserSettings { get; set; } = [];
}

public sealed record BackupAccount(
    int BackupId,
    string Name,
    string AccountType,
    decimal AccountLimit,
    decimal MaximumSpending,
    decimal? MinimumPayment,
    decimal SpentAmount,
    decimal Balance,
    int? MonthlyDueDate,
    int? DeductSourceBackupId,
    decimal? InterestRate,
    bool PinnedOnUI,
    bool IsEnabled,
    bool IsForDeletion,
    bool IsDefault = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("showOnUI")]
    public bool? LegacyShowOnUI { get; init; }

    [JsonIgnore]
    public bool RestoredPinnedOnUI => LegacyShowOnUI ?? PinnedOnUI;
}

public sealed record BackupTag(
    int BackupId,
    string Name,
    string HexCode,
    bool IsSystemTag,
    decimal? SpendingLimit = null);

public sealed record BackupSavingGoal(
    int BackupId,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateTime? SavingEndDate,
    DateTime CreatedOn);

public sealed record BackupExpense(
    int BackupId,
    int AccountBackupId,
    int TagBackupId,
    string Name,
    decimal Amount,
    string ExpenseCategory,
    bool IsIoU = false);

public sealed record BackupExpenseLog(
    int BackupId,
    int ExpenseBackupId,
    int AccountBackupId,
    decimal Amount,
    DateTime DeductedOn,
    string Notes,
    bool IsForDeletion,
    bool IsPinned = false,
    int? ParentLogBackupId = null,
    bool IsIoU = false);

public sealed record BackupIncomeLog(
    int BackupId,
    int AccountBackupId,
    string Name,
    decimal Amount,
    DateTime AddedOn,
    string Notes,
    bool IsPinned = false,
    bool IsIoU = false);

public sealed record BackupTransaction(
    int BackupId,
    string Type,
    int AccountBackupId,
    string Name,
    decimal Amount,
    DateTime OccurredOn,
    string Notes,
    string? ExpenseCategory,
    int? TagBackupId,
    int? ParentTransactionBackupId,
    bool IsPinned,
    bool IsForDeletion,
    bool IsIoU,
    bool? ShouldAffectBalance = null)
{
    [JsonIgnore]
    public bool RestoredShouldAffectBalance => ShouldAffectBalance ?? IsIoU;
}

public sealed record BackupRecurringTransaction(
    int BackupId,
    string Name,
    decimal Amount,
    string RecurringPeriod,
    int RecurringTime,
    string Type,
    string? Category,
    int SourceBackupId,
    int? TagBackupId,
    int? GoalBackupId,
    bool IsEnabled,
    DateTime? EndDate = null);

public sealed record BackupUserSetting(string Name, string Value);
