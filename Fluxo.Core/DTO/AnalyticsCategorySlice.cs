using Fluxo.Core.Enums;

namespace Fluxo.Core.DTO;

public sealed record AnalyticsCategorySlice(
    ExpenseCategory Category,
    decimal Total);
