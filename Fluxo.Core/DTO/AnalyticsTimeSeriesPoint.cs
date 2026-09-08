namespace Fluxo.Core.DTO;

public sealed record AnalyticsTimeSeriesPoint(
    DateOnly Period,
    decimal Income,
    decimal Expense);
