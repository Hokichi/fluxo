using System.Windows;
using System.Windows.Controls;
using Fluxo.Views.Shell.Wizard;

namespace Fluxo.Views.Shell.Wizard.Pages.Steps;

public partial class Accounts : UserControl
{
    public Accounts()
    {
        InitializeComponent();
    }

    private static QuickSetupWizard? FindPopup(DependencyObject source) => Window.GetWindow(source) as QuickSetupWizard;

    private void OnEditAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject source)
            FindPopup(source)?.OnEditAccountClick(sender, e);
    }

    private void OnAddAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject source)
            FindPopup(source)?.OnAddAccountClick(sender, e);
    }

    private void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject source)
            FindPopup(source)?.OnDeleteAccountClick(sender, e);
    }
}
