using WixToolset.BootstrapperApplicationApi;

namespace Fluxo.Installer.Models;

public static class InstallerLaunchActionResolver
{
    public static LaunchAction Resolve(InstallerRequestedOperation operation) => operation switch
    {
        InstallerRequestedOperation.Uninstall => LaunchAction.Uninstall,
        InstallerRequestedOperation.Repair => LaunchAction.Repair,
        _ => LaunchAction.Install,
    };
}
