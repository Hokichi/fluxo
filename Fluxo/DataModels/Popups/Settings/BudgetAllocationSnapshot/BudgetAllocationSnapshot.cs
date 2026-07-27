using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.Settings.BudgetAllocationSnapshot;

public readonly record struct BudgetAllocationSnapshot(
    int Needs,
    int Wants,
    int Invest,
    decimal AllocationLimit,
    AllocationPeriod AllocationPeriod,
    int PeriodStart,
    RolloverPolicy RolloverPolicy,
    OverspendPolicy OverspendPolicy);
