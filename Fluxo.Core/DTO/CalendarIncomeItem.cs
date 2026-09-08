namespace Fluxo.Core.DTO;

public sealed record CalendarIncomeItem(
    int Id,
    string Name,
    decimal Amount,
    string AccountName);
