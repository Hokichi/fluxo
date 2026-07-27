using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.AddNewTransactionHistoryBuilder;

public sealed record RepeatingExpenseKey(
        string Name,
        decimal Amount,
        int AccountId,
        int TagId);
