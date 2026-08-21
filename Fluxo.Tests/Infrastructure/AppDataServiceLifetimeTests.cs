using Fluxo.Core.Interfaces.Caching;
using Fluxo.Core.Interfaces.Operations;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Extensions;
using Fluxo.Services.Caching;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class AppDataServiceLifetimeTests
{
    [Fact]
    public void AddFluxoPresentation_UsesSingletonCacheAndCoordinatorWithScopedAppData()
    {
        using var services = new ServiceCollection()
            .AddSingleton(Substitute.For<IDataOperationRunner>())
            .AddSingleton(Substitute.For<ILogService>())
            .AddFluxoPresentation()
            .BuildServiceProvider();
        using var firstScope = services.CreateScope();
        using var secondScope = services.CreateScope();

        Assert.Same(
            services.GetRequiredService<IAppDataCache>(),
            firstScope.ServiceProvider.GetRequiredService<IAppDataCache>());
        Assert.Same(
            services.GetRequiredService<AppDataCommitCoordinator>(),
            secondScope.ServiceProvider.GetRequiredService<AppDataCommitCoordinator>());
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<IAppDataService>(),
            firstScope.ServiceProvider.GetRequiredService<IAppDataService>());
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<IAppDataService>(),
            secondScope.ServiceProvider.GetRequiredService<IAppDataService>());
    }
}
