using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;

namespace Fluxo.Services.Dialogs;

public sealed class TransactionPopupAddTagHost : IDisposable
{
    private readonly Func<SettingsTagsTabVM> _settingsTagsProvider;
    private readonly IDialogService _dialogService;
    private readonly IMessenger _messenger;
    private readonly Func<Window?> _ownerProvider;
    private bool _disposed;

    public TransactionPopupAddTagHost(
        Func<SettingsTagsTabVM> settingsTagsProvider,
        IDialogService dialogService,
        IMessenger messenger,
        Func<Window?> ownerProvider)
    {
        _settingsTagsProvider = settingsTagsProvider;
        _dialogService = dialogService;
        _messenger = messenger;
        _ownerProvider = ownerProvider;
        _messenger.Register<TransactionPopupAddTagHost, TransactionPopupAddTagRequestedMessage>(
            this,
            static (recipient, _) => recipient.ShowAddTag());
    }

    private void ShowAddTag()
    {
        if (_disposed)
            return;

        var owner = _ownerProvider();
        _dialogService.ShowAddTag(_settingsTagsProvider(), owner);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _messenger.Unregister<TransactionPopupAddTagRequestedMessage>(this);
    }
}
