using Microsoft.Win32;

namespace Fluxo.Installer.Services;

public static class InstalledVersionRegistryReader
{
    public const string InstalledVersionSubKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\fluxo";
    public const string InstalledVersionValueName = "InstalledVersion";
    public const string InstallLocationValueName = "InstallLocation";

    public static string? ReadInstalledVersion()
    {
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
                var version = ReadInstalledVersion(baseKey);
                if (!string.IsNullOrWhiteSpace(version))
                {
                    return version;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static string? ReadInstalledVersion(RegistryKey baseKey, string subKeyPath = InstalledVersionSubKeyPath)
    {
        try
        {
            using var fluxoKey = baseKey.OpenSubKey(subKeyPath);
            var installedVersion = fluxoKey?.GetValue(InstalledVersionValueName) as string;
            return string.IsNullOrWhiteSpace(installedVersion) ? null : installedVersion;
        }
        catch
        {
            return null;
        }
    }

    public static string? ReadInstallLocation()
        => ReadInstallLocations().FirstOrDefault();

    public static IReadOnlyList<string> ReadInstallLocations()
    {
        var locations = new List<string>();
        foreach (var registryView in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView);
                var installLocation = ReadInstallLocation(baseKey);
                if (!string.IsNullOrWhiteSpace(installLocation))
                {
                    locations.Add(installLocation);
                }
            }
            catch
            {
            }
        }

        return locations;
    }

    public static string? ReadInstallLocation(RegistryKey baseKey, string subKeyPath = InstalledVersionSubKeyPath)
    {
        try
        {
            using var fluxoKey = baseKey.OpenSubKey(subKeyPath);
            var installLocation = fluxoKey?.GetValue(InstallLocationValueName) as string;
            return string.IsNullOrWhiteSpace(installLocation) ? null : installLocation;
        }
        catch
        {
            return null;
        }
    }
}
