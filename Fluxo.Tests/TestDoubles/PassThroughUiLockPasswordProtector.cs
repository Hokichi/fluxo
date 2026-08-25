using Fluxo.Core.Interfaces.Services;

namespace Fluxo.Tests.TestDoubles;

internal sealed class PassThroughUiLockPasswordProtector : IUiLockPasswordProtector
{
    public string Protect(string? password) => password ?? string.Empty;

    public string Unprotect(string? protectedPassword) => protectedPassword ?? string.Empty;
}
