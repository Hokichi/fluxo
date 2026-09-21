namespace Fluxo.Installer.Models;

public enum InstallerState
{
    Welcome,
    Detecting,
    AwaitingReinstallConfirmation,
    Installing,
    Verifying,
    FinishedSuccess,
    FinishedUninstalled,
    FinishedUpToDate,
    FinishedFailed,
    FinishedCancelled,
}
