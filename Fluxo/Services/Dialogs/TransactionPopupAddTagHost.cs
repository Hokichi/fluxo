using System.Windows;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Popups.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Fluxo.Services.Dialogs;

public sealed class TransactionPopupAddTagHost : IDisposable
{
    private static readonly ConditionalWeakTable<IMessenger, Registration> Registrations = [];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDialogService _dialogService;
    private readonly IMessenger _messenger;
    private readonly Func<TransactionPopupVM?, Window?> _ownerProvider;
    private bool _disposed;

    public TransactionPopupAddTagHost(
        IServiceScopeFactory scopeFactory,
        IDialogService dialogService,
        IMessenger messenger,
        Func<TransactionPopupVM?, Window?> ownerProvider)
    {
        _scopeFactory = scopeFactory;
        _dialogService = dialogService;
        _messenger = messenger;
        _ownerProvider = ownerProvider;
        lock (Registrations)
        {
            if (Registrations.TryGetValue(messenger, out var previous))
                previous.Host.Dispose();
            Registrations.Remove(messenger);
            Registrations.Add(messenger, new Registration(this));
        }
        _messenger.Register<TransactionPopupAddTagHost, TransactionPopupAddTagRequestedMessage>(
            this,
            static (recipient, message) => recipient.ShowAddTag(message));
    }

    private void ShowAddTag(TransactionPopupAddTagRequestedMessage message)
    {
        if (_disposed)
            return;

        var owner = _ownerProvider(message.Requester);
        if (owner is null)
            return;

        using var scope = _scopeFactory.CreateScope();
        _dialogService.ShowAddTag(scope.ServiceProvider.GetRequiredService<SettingsTagsTabVM>(), owner);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _messenger.Unregister<TransactionPopupAddTagRequestedMessage>(this);
        lock (Registrations)
        {
            if (Registrations.TryGetValue(_messenger, out var registration) && ReferenceEquals(registration.Host, this))
                Registrations.Remove(_messenger);
        }
    }

    private sealed class Registration(TransactionPopupAddTagHost host)
    {
        public TransactionPopupAddTagHost Host { get; } = host;
    }
}
