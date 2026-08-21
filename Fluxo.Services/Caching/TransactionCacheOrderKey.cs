namespace Fluxo.Services.Caching;

internal readonly record struct TransactionCacheOrderKey(
    DateTime OccurredOn,
    DateTime LoggedOn,
    int Id) : IComparable<TransactionCacheOrderKey>
{
    public int CompareTo(TransactionCacheOrderKey other)
    {
        var occurredComparison = other.OccurredOn.CompareTo(OccurredOn);
        if (occurredComparison != 0)
            return occurredComparison;

        var loggedComparison = other.LoggedOn.CompareTo(LoggedOn);
        return loggedComparison != 0 ? loggedComparison : other.Id.CompareTo(Id);
    }
}
