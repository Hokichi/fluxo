using Fluxo.Core.Enums;

namespace Fluxo.ViewModels.Popups.Settings;

public sealed class IoUItemVM
{
    public IoUKind Kind { get; init; }
    public int TransactionId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime Date { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public bool ShouldAffectBalance { get; init; }
    public string TypeLabel => Kind == IoUKind.Lend ? "Lend" : "Debt";
    public string AmountSign => Kind == IoUKind.Debt ? "+" : "-";
}
