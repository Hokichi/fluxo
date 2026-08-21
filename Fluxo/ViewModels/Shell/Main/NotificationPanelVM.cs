using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Notifications;
using Fluxo.Services.Updates;
using Fluxo.ViewModels.Entities;

namespace Fluxo.ViewModels.Shell.Main;

public partial class NotificationPanelVM : ObservableRecipient,
    IRecipient<DashboardDataInvalidatedMessage>, IRecipient<NotificationEntityCreatedMessage>
{
    private readonly IAppDataService _appData;
    private readonly INotificationGroupingService _grouping;
    private readonly StartupNotificationEvaluator _evaluator;

    public NotificationPanelVM(
        ITransactionService transactionService,
        IAccountService accountService,
        IAppDataService appData,
        IMapper mapper,
        INotificationGroupingService? notificationGroupingService = null,
        IDialogService? dialogService = null,
        IMessenger? messenger = null,
        IAppUpdateInteractionService? appUpdateInteractionService = null,
        StartupNotificationEvaluator? evaluator = null)
        : base(messenger ?? WeakReferenceMessenger.Default)
    {
        _ = transactionService;
        _ = accountService;
        _ = mapper;
        _ = dialogService;
        _ = appUpdateInteractionService;
        _appData = appData;
        _grouping = notificationGroupingService ?? new NotificationGroupingService();
        _evaluator = evaluator ?? new StartupNotificationEvaluator(appData);
        Notifications.CollectionChanged += OnNotificationsChanged;
        IsActive = true;
    }

    [ObservableProperty] private int _notificationCount;
    [ObservableProperty] private bool _hasNotifications;
    [ObservableProperty] private bool _hasMultipleNotifications;
    [ObservableProperty] private int _currentNotificationIndex = -1;
    [ObservableProperty] private NotificationVM? _currentNotification;
    [ObservableProperty] private NotificationItemVM? _currentNotificationItem;
    [ObservableProperty] private int _navigationDirection;

    public ObservableCollection<NotificationVM> Notifications { get; } = [];
    public ObservableCollection<NotificationItemVM> NotificationItems { get; } = [];
    public int NotificationStepCount => NotificationItems.Count;
    public int CurrentStepNumber => HasNotifications && CurrentNotificationIndex >= 0 ? CurrentNotificationIndex + 1 : 0;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (await IsSnoozedAsync(cancellationToken))
        {
            ReplaceNotifications([]);
            return;
        }

        ApplyEvaluation(await _evaluator.EvaluateAsync(cancellationToken));
    }

    [RelayCommand] private Task ClearAllNotificationsAsync()
    {
        ReplaceNotifications([]);
        return Task.CompletedTask;
    }

    [RelayCommand] private async Task SnoozeAllNotificationsAsync()
    {
        var settings = await _appData.GetUserSettingByNameAsync(
            UserSettingNames.NotificationsSnoozeEndDate);
        var value = DateTime.Now.AddHours(24).ToString("O", CultureInfo.InvariantCulture);
        if (settings is null)
        {
            await _appData.AddUserSettingAsync(new()
            {
                Name = UserSettingNames.NotificationsSnoozeEndDate,
                Value = value
            });
        }
        else
        {
            settings.Value = value;
            _appData.UpdateUserSetting(settings);
        }
        await _appData.SaveChangesAsync();
        ReplaceNotifications([]);
    }

    [RelayCommand] private Task ClearNotificationGroupAsync(NotificationItemVM? card)
    {
        if (card is not null)
            ReplaceNotifications(Notifications.Except(card.Notifications).ToList());
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenNotificationActionAsync(NotificationItemVM? card)
    {
        if (card is null || !card.HasActionCta)
            return Task.CompletedTask;

        var ids = card.Notifications
            .Select(notification => notification.Type.Split('-').LastOrDefault())
            .Select(token => int.TryParse(token, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToList();
        if (ids.Count > 0)
            Messenger.Send(new NotificationProcessingRequestedMessage(card.Category.ToString(), ids));
        return Task.CompletedTask;
    }
    [RelayCommand] private void NavigatePrevious() => Navigate(-1, 1);
    [RelayCommand] private void NavigateNext() => Navigate(1, -1);

    public void Receive(DashboardDataInvalidatedMessage message)
    {
        if (message.Value.HasFlag(DashboardDataInvalidationScope.Notifications))
            _ = LoadAsync();
    }

    public async void Receive(NotificationEntityCreatedMessage message)
    {
        var evaluation = await _evaluator.EvaluateEntityAsync(message.Value.Kind, message.Value.EntityId);
        ReplaceNotifications(Notifications.Concat(evaluation.Notifications).GroupBy(item => item.Type).Select(group => group.Last()).ToList());
        Messenger.Send(new StartupNotificationStateChangedMessage(evaluation));
    }

    public static DateTime? ResolveRecurringTransactionDueDate(RecurringTransactionVM transaction, DateTime today) => transaction.RecurringPeriod switch
    {
        Core.Enums.RecurringPeriod.Weekly or Core.Enums.RecurringPeriod.Biweekly when transaction.RecurringTime is >= 1 and <= 7 =>
            today.Date.AddDays((transaction.RecurringTime - (today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)today.DayOfWeek) + 7) % 7),
        Core.Enums.RecurringPeriod.Monthly when transaction.RecurringTime > 0 => new DateTime(today.Year, today.Month,
            Math.Min(transaction.RecurringTime, DateTime.DaysInMonth(today.Year, today.Month))),
        _ => null
    };

    private async Task<bool> IsSnoozedAsync(CancellationToken cancellationToken)
    {
        var setting = await _appData.GetUserSettingByNameAsync(
            UserSettingNames.NotificationsSnoozeEndDate,
            cancellationToken).ConfigureAwait(false);
        return setting is not null && DateTime.TryParseExact(setting.Value, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var endDate) && endDate > DateTime.Now;
    }

    private void ApplyEvaluation(StartupNotificationEvaluation evaluation)
    {
        ReplaceNotifications(evaluation.Notifications);
        Messenger.Send(new StartupNotificationStateChangedMessage(evaluation));
    }

    private void ReplaceNotifications(IReadOnlyList<NotificationVM> notifications)
    {
        Notifications.Clear();
        foreach (var notification in notifications) Notifications.Add(notification);
    }

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotificationItems.Clear();
        foreach (var card in _grouping.Group(Notifications.ToList())) NotificationItems.Add(card);
        NotificationCount = Notifications.Count;
        HasNotifications = NotificationCount > 0;
        HasMultipleNotifications = NotificationCount > 1;
        CurrentNotificationIndex = HasNotifications ? 0 : -1;
        CurrentNotificationItem = HasNotifications ? NotificationItems[0] : null;
        CurrentNotification = CurrentNotificationItem?.Notifications.FirstOrDefault();
        OnPropertyChanged(nameof(NotificationStepCount));
        OnPropertyChanged(nameof(CurrentStepNumber));
    }

    private void Navigate(int offset, int direction)
    {
        if (NotificationItems.Count == 0) return;
        NavigationDirection = direction;
        CurrentNotificationIndex = (CurrentNotificationIndex + offset + NotificationItems.Count) % NotificationItems.Count;
        CurrentNotificationItem = NotificationItems[CurrentNotificationIndex];
        CurrentNotification = CurrentNotificationItem.Notifications.FirstOrDefault();
        OnPropertyChanged(nameof(CurrentStepNumber));
    }
}
