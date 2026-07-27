using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Resources.CustomControls;
using Fluxo.Services.Notifications;
using Fluxo.Services.Updates;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Views.Popups.Settings;

namespace Fluxo.Helpers.Settings;

public static class SettingsUpdateCheckFlow
{
    public static async Task CheckForUpdatesAsync(Window? owner, SettingsPersonalizationTabVM? viewModel)
    {
        if (viewModel is null)
            return;

        var ownerPopup = owner as SettingsPopup;
        var messenger = ownerPopup?.Messenger ?? WeakReferenceMessenger.Default;
        var progressId = FloatingNotificationPublisher.Publish(
            messenger,
            "Checking for updates",
            "Looking for the latest fluxo release.",
            [],
            NotificationSeverity.Info);

        AppUpdateCheckResult update;
        try
        {
            update = await viewModel.CheckForUpdatesAsync();
        }
        finally
        {
            FloatingNotificationPublisher.Dismiss(messenger, progressId);
        }

        switch (update.Status)
        {
            case AppUpdateCheckStatus.UpToDate:
                FloatingNotificationPublisher.Publish(
                    messenger,
                    "fluxo is up to date",
                    $"Version {viewModel.CurrentVersion} is the latest available version.",
                    [],
                    NotificationSeverity.Success);
                return;

            case AppUpdateCheckStatus.Error:
                FluxoMessageBox.Show(
                    owner,
                    update.ErrorMessage ?? "Unable to check for updates.",
                    "Check for Updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;

            case AppUpdateCheckStatus.UpdateAvailable:
                await viewModel.HandleAvailableUpdateAsync(update, owner);
                return;
        }
    }
}
