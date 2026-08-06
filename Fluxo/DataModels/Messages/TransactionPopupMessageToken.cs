namespace Fluxo.DataModels.Messages;

public sealed record TransactionPopupMessageToken(Guid Id)
{
    public static TransactionPopupMessageToken Default { get; } = new(Guid.Empty);
}
