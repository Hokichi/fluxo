using System.Reflection;
using Xunit;

namespace Fluxo.Tests.Infrastructure;

public sealed class PublishedAssemblyLoadTests
{
    [Fact]
    public void PublishedAssemblies_ResolveCacheContractAndExportedTypesWithoutStartingApplication()
    {
        var directory = Path.GetFullPath(Environment.GetEnvironmentVariable("FLUXO_VERIFIED_PAYLOAD") ?? AppContext.BaseDirectory);
        var context = new PublishedAssemblyLoadContext(directory);
        try
        {
            var core = context.LoadFromAssemblyName(new AssemblyName("Fluxo.Core"));
            Assert.NotNull(core.GetType("Fluxo.Core.Interfaces.Caching.IAppDataCache", throwOnError: true));
            foreach (var name in new[] { "Fluxo.Core", "Fluxo.Data", "Fluxo.Services", "Fluxo.Resources", "fluxo" })
            {
                var assembly = context.LoadFromAssemblyName(new AssemblyName(name));
                Assert.NotEmpty(assembly.GetExportedTypes());
                Assert.StartsWith(directory, assembly.Location, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
