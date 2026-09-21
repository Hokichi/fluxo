using System.IO;

namespace Fluxo.Installer.Models;

public static class InstallerInstallLocationResolver
{
    public static string Resolve(IEnumerable<string?> registeredLocations, string? requestedFolder,
        string defaultFolder, Func<string, bool> directoryExists)
    {
        foreach (var candidate in registeredLocations)
        {
            var normalized = Normalize(candidate);
            if (normalized is null) continue;
            try
            {
                if (directoryExists(normalized)) return normalized;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Continue to the other registry view or fresh-install destination.
            }
        }

        return Normalize(requestedFolder) ?? defaultFolder;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return string.Equals(normalized, Path.GetPathRoot(normalized), StringComparison.OrdinalIgnoreCase)
                ? null : normalized;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }
}
