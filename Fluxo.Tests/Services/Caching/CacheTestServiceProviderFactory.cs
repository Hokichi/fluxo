using Fluxo.Core.Interfaces.Services;
using Fluxo.Data.Context;
using Fluxo.Data.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Fluxo.Tests.Services.Caching;

internal static class CacheTestServiceProviderFactory
{
    internal static ServiceProvider Create(
        SqliteConnection connection,
        CountingDbCommandInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILogService>());
        services.AddFluxoData();
        services.AddDbContext<FluxoDbContext>(options =>
            options.UseSqlite(connection).AddInterceptors(interceptor));
        return services.BuildServiceProvider();
    }
}
