namespace Fluxo.Core.DTO;

public sealed record CalendarExpenseItem(
    int Id,
    string Name,
    decimal Amount,
    string AccountName,
    string? TagName);
