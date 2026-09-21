namespace Fluxo.Installer.Models;

public static class InstallerUpToDateDecision
{
    public static bool ShouldSkipInstall(
        InstallerOperationMode operationMode,
        int detectStatus,
        string? currentBundleVersion,
        string? highestDetectedInstalledVersion,
        Func<string, string, int> compareVersions)
    {
        return ShouldSkipInstall(
            operationMode,
            detectStatus,
            currentBundleVersion,
            highestDetectedInstalledVersion,
            registryInstalledVersion: null,
            installedExecutableVersion: null,
            compareVersions);
    }

    public static bool ShouldSkipInstall(
        InstallerOperationMode operationMode,
        int detectStatus,
        string? currentBundleVersion,
        string? highestDetectedInstalledVersion,
        string? installedExecutableVersion,
        Func<string, string, int> compareVersions)
    {
        return ShouldSkipInstall(
            operationMode,
            detectStatus,
            currentBundleVersion,
            highestDetectedInstalledVersion,
            registryInstalledVersion: null,
            installedExecutableVersion,
            compareVersions);
    }

    public static bool ShouldSkipInstall(
        InstallerOperationMode operationMode,
        int detectStatus,
        string? currentBundleVersion,
        string? highestDetectedInstalledVersion,
        string? registryInstalledVersion,
        string? installedExecutableVersion,
        Func<string, string, int> compareVersions)
    {
        return Evaluate(
            operationMode,
            detectStatus,
            currentBundleVersion,
            highestDetectedInstalledVersion,
            registryInstalledVersion,
            installedExecutableVersion,
            compareVersions).ShouldSkipInstall;
    }

    public static InstallerUpToDateDecisionResult Evaluate(
        InstallerOperationMode operationMode,
        int detectStatus,
        string? currentBundleVersion,
        string? highestDetectedInstalledVersion,
        string? registryInstalledVersion,
        string? installedExecutableVersion,
        Func<string, string, int> compareVersions,
        bool hasOlderRelatedBundle = false)
    {
        if (operationMode != InstallerOperationMode.Install || detectStatus != 0)
        {
            return new InstallerUpToDateDecisionResult(false, null, false);
        }

        if (string.IsNullOrWhiteSpace(currentBundleVersion))
        {
            return new InstallerUpToDateDecisionResult(false, null, false);
        }

        int? highestDetectedComparison = null;
        string? highestDetectedVersion = null;

        ConsiderDetectedVersion(highestDetectedInstalledVersion);
        ConsiderDetectedVersion(registryInstalledVersion);
        ConsiderDetectedVersion(installedExecutableVersion);

        return highestDetectedComparison >= 0
            ? new InstallerUpToDateDecisionResult(
                true,
                highestDetectedVersion,
                highestDetectedComparison > 0,
                highestDetectedComparison == 0)
            : new InstallerUpToDateDecisionResult(false, highestDetectedVersion, false);

        void ConsiderDetectedVersion(string? detectedVersion)
        {
            if (string.IsNullOrWhiteSpace(detectedVersion))
            {
                return;
            }

            int comparisonToCurrent;
            try
            {
                comparisonToCurrent = compareVersions(detectedVersion, currentBundleVersion);
            }
            catch
            {
                return;
            }

            if (highestDetectedVersion is null)
            {
                highestDetectedVersion = detectedVersion;
                highestDetectedComparison = comparisonToCurrent;
                return;
            }

            try
            {
                if (compareVersions(detectedVersion, highestDetectedVersion) <= 0)
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            highestDetectedVersion = detectedVersion;
            highestDetectedComparison = comparisonToCurrent;
        }
    }
}
