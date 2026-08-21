using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Fluxo.Data.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Fluxo.Tests.Services.Caching;

internal static class CacheTestServiceProviderFactory
{
    internal static ServiceProvider Create(
        SqliteConnection connection,
        CountingDbCommandInterceptor interceptor,
        params IInterceptor[] additionalInterceptors)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILogService>());
        services.AddFluxoData();
        services.AddDbContext<FluxoDbContext>(options =>
            options.UseSqlite(connection).AddInterceptors([interceptor, .. additionalInterceptors]));
        return services.BuildServiceProvider();
    }
}
