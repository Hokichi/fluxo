using Fluxo.Core.Enums;

namespace Fluxo.Core.DTO;

public sealed record CalendarRecurringTransactionItem(
    int Id,
    string Name,
    decimal Amount,
    RecurringTransactionType Type,
    RecurringPeriod RecurringPeriod,
    int RecurringTime,
    string SourceName);
