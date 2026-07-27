using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.AddNewTransactionHistoryBuilder;

public sealed record IncomeKey(
        string Name,
        decimal Amount,
        int AccountId,
        string Note);
