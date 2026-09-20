using System.Reflection;
using System.Runtime.Loader;

namespace Fluxo.Tests.Infrastructure;

internal sealed class PublishedAssemblyLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        foreach (var folder in new[] { directory, Path.Combine(directory, "libs"), Path.Combine(directory, "vendor") })
        {
            var path = Path.Combine(folder, $"{assemblyName.Name}.dll");
            if (File.Exists(path))
                return LoadFromAssemblyPath(path);
        }

        if (assemblyName.Name?.StartsWith("Fluxo", StringComparison.OrdinalIgnoreCase) == true)
            throw new FileNotFoundException($"Missing deployed assembly: {assemblyName.Name}");

        return null; // Framework assemblies come from the test host's shared runtime.
    }
}
