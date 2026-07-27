using System.Windows;
using Fluxo.Resources.CustomControls;
using Fluxo.ViewModels.Popups.Settings;

namespace Fluxo.Helpers.Settings;

public static class SettingsSetupWizardFlow
{
    public static async Task RunAsync(Window? owner, SettingsPersonalizationTabVM? viewModel)
    {
        if (viewModel is null || Application.Current is not App app)
            return;

        if (FluxoMessageBox.Show(
                owner,
                "This will close the current window and open the setup wizard. Continue?",
                "Run Setup Wizard",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        viewModel.RequestClosePopup();
        await app.RunSetupWizardAsync();
    }
}
