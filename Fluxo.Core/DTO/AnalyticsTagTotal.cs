namespace Fluxo.Core.DTO;

public sealed record AnalyticsTagTotal(
    string TagName,
    string HexCode,
    decimal Total);
