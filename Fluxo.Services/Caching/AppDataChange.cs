namespace Fluxo.Services.Caching;

internal sealed class AppDataChange(AppDataChangeKind kind, object entity)
{
    internal AppDataChangeKind Kind { get; } = kind;
    internal object Entity { get; } = entity;
}
