namespace Fluxo.Core.Exceptions;

public sealed class AppDataCacheNotInitializedException()
    : InvalidOperationException("Application data cache has not been initialized.");
