using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.AddNewTransactionHistoryBuilder;

public sealed record RepeatingIncomeKey(
        string Name,
        decimal Amount,
        int AccountId);
