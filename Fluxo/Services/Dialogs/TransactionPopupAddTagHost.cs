using System.Windows;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
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
    private readonly Func<Guid, Window?> _ownerProvider;
    private readonly Registration _registration;
    private bool _disposed;

    public TransactionPopupAddTagHost(
        IServiceScopeFactory scopeFactory,
        IDialogService dialogService,
        IMessenger messenger,
        Func<Guid, Window?> ownerProvider)
    {
        _scopeFactory = scopeFactory;
        _dialogService = dialogService;
        _messenger = messenger;
        _ownerProvider = ownerProvider;
        lock (Registrations)
        {
            if (Registrations.TryGetValue(messenger, out var registration))
                _registration = registration;
            else
            {
                _registration = new Registration(messenger);
                Registrations.Add(messenger, _registration);
            }
            _registration.Push(this);
        }
    }

    private void ShowAddTag(TransactionPopupAddTagRequestedMessage message)
    {
        if (_disposed)
            return;

        var owner = _ownerProvider(message.OwnerToken);
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
        lock (Registrations)
        {
            _registration.Remove(this);
            if (_registration.IsEmpty)
                Registrations.Remove(_messenger);
        }
    }

    private sealed class Registration(IMessenger messenger)
    {
        private readonly IMessenger _messenger = messenger;
        private readonly List<TransactionPopupAddTagHost> _hosts = [];
        public bool IsEmpty => _hosts.Count == 0;

        public void Push(TransactionPopupAddTagHost host)
        {
            if (_hosts.Count > 0)
                _hosts[^1].Deactivate();
            _hosts.Add(host);
            host.Activate();
        }

        public void Remove(TransactionPopupAddTagHost host)
        {
            var wasCurrent = _hosts.Count > 0 && ReferenceEquals(_hosts[^1], host);
            _hosts.Remove(host);
            host.Deactivate();
            if (wasCurrent && _hosts.Count > 0)
                _hosts[^1].Activate();
        }

        public void Activate(TransactionPopupAddTagHost host)
        {
            _messenger.Register<TransactionPopupAddTagHost, TransactionPopupAddTagRequestedMessage>(
                host,
                static (recipient, message) => recipient.ShowAddTag(message));
        }
    }

    private void Activate() => _registration.Activate(this);

    private void Deactivate() => _messenger.Unregister<TransactionPopupAddTagRequestedMessage>(this);
}
