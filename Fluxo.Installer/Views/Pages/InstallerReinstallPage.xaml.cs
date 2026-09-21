using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Fluxo.Installer.Views.Pages;

public partial class InstallerReinstallPage : UserControl
{
    public InstallerReinstallPage() => InitializeComponent();

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsVisible) DeclineButton.Focus();
        });
    }
}
