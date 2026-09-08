namespace Fluxo.Core.DTO;

public sealed record AnalyticsDto(
    decimal TotalIncome,
    decimal TotalExpense,
    IReadOnlyList<AnalyticsTimeSeriesPoint> TimeSeries,
    IReadOnlyList<AnalyticsCategorySlice> CategoryRatio,
    IReadOnlyList<AnalyticsTagTotal> TopSpendingTags,
    IReadOnlyList<AnalyticsGoalItem> GoalsCreatedInPeriod);
