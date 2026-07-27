using Fluxo.Core.Entities;
using Fluxo.Core.Enums;

namespace Fluxo.DataModels.Popups.AddNewTransactionHistoryBuilder;

public sealed record ExpenseKey(
        string Name,
        decimal Amount,
        int AccountId,
        string Note,
        ExpenseCategory Category,
        int TagId);
