namespace Fluxo.Installer.Models;

public readonly record struct InstallerUpToDateDecisionResult(
    bool ShouldSkipInstall,
    string? InstalledVersion,
    bool IsNewerVersion,
    bool RequiresReinstallConfirmation = false);
