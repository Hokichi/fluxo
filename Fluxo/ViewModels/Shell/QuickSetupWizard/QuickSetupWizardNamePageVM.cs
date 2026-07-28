using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.Helpers.Settings;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.ViewModels.Popups.Settings;

namespace Fluxo.ViewModels.Shell.QuickSetupWizard;

public partial class QuickSetupWizardNamePageVM : ObservableObject
{
    private readonly IAppDataService _appData;
    private readonly IMessenger _messenger;

    [ObservableProperty] private string _usernameText = "User";

    public QuickSetupWizardNamePageVM(IAppDataService appData, IMessenger? messenger = null)
    {
        _appData = appData;
        _messenger = messenger ?? WeakReferenceMessenger.Default;
    }

    public string ResolvedUsername => QuickSetupWizardShared.ResolveUsername(UsernameText);
    public bool HasUsernameInput => !string.IsNullOrWhiteSpace(UsernameText);

    public async Task LoadAsync()
    {
        var settings = await _appData.GetUserSettingsAsync();
        var settingsByName = settings.ToDictionary(setting => setting.Name, setting => setting.Value, StringComparer.Ordinal);

        UsernameText = QuickSetupWizardShared.ParseString(settingsByName, UserSettingNames.PreferredDisplayName, "User");

        PublishIdentitySnapshot();
    }

    public async Task<SettingsOperationResult> SaveAsync()
    {
        await ApplyAsync(_appData);
        await _appData.SaveChangesAsync();

        _messenger.Send(new DashboardDataInvalidatedMessage(DashboardDataInvalidationScope.All));
        PublishIdentitySnapshot();
        return SettingsOperationResult.Success();
    }

    public Task ApplyAsync(IAppDataService appData)
    {
        return QuickSetupWizardShared.UpsertUserSettingAsync(
            appData,
            UserSettingNames.PreferredDisplayName,
            ResolvedUsername);
    }

    partial void OnUsernameTextChanged(string value)
    {
        OnPropertyChanged(nameof(ResolvedUsername));
        OnPropertyChanged(nameof(HasUsernameInput));
        PublishIdentitySnapshot();
    }

    public string CurrentStepTitle => "What should fluxo call you?";

    public string CurrentStepDescription => "Pick the name you'd like fluxo to use throughout the app.";

    private void PublishIdentitySnapshot()
    {
        _messenger.Send(new QuickSetupWizardIdentityChangedMessage(
            new QuickSetupWizardIdentityChanged(ResolvedUsername)));
    }
}

