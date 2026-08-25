using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AutoMapper;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Resources.Messages;
using Fluxo.Services.Dialogs;
using Fluxo.Services.Ui;
using Fluxo.Tests.TestDoubles;
using Fluxo.ViewModels.Popups.Settings;
using Fluxo.Views.Popups.Settings;
using NSubstitute;
using Xunit;
using SettingsOperationResult = Fluxo.DataModels.Popups.Settings.SettingsOperationResult.SettingsOperationResult;

namespace Fluxo.Tests.Views.Popups;

public sealed class SettingsPopupPersistenceTests
{
    [Fact]
    public void ConcurrentConfigurationSaves_WaitForTheInFlightSave()
    {
        RunInSta(() =>
        {
            EnsureApplicationResources();
            var saveCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var (popup, _, appData) = CreatePopup(saveCompletion.Task);
            var saveMethod = typeof(SettingsPopup).GetMethod(
                "SaveConfigurationChangesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var first = Assert.IsAssignableFrom<Task>(saveMethod.Invoke(popup, null));
            var second = Assert.IsAssignableFrom<Task>(saveMethod.Invoke(popup, null));

            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            _ = appData.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void ConfigurationSave_WhenSettingChangesInFlight_PersistsLatestRevision()
    {
        RunInSta(() =>
        {
            EnsureApplicationResources();
            var firstSaveCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var (popup, settings, appData) = CreatePopup(
                firstSaveCompletion.Task,
                subsequentSaveTask: Task.CompletedTask);
            var saveMethod = typeof(SettingsPopup).GetMethod(
                "SaveConfigurationChangesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var saveTask = Assert.IsAssignableFrom<Task<SettingsOperationResult>>(saveMethod.Invoke(popup, null));
            settings.PersonalizationTab.ShouldRunAtStartup = false;
            firstSaveCompletion.SetResult();

            var result = saveTask.GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.False(settings.HasPendingConfigurationChanges);
            _ = appData.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void ConfigurationSave_WhenSuccessNotificationFails_StillReturnsSuccess()
    {
        RunInSta(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            messenger.Register<ShowFloatingNotificationMessage>(
                new object(),
                static (_, _) => throw new InvalidOperationException("notification failed"));
            var (popup, _, _) = CreatePopup(Task.CompletedTask, messenger: messenger);
            var saveMethod = typeof(SettingsPopup).GetMethod(
                "SaveConfigurationChangesAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            var saveTask = Assert.IsAssignableFrom<Task<SettingsOperationResult>>(saveMethod.Invoke(popup, null));
            var result = saveTask.GetAwaiter().GetResult();

            Assert.True(result.IsSuccess, result.ErrorMessage);
        });
    }

    [Fact]
    public void Close_WhenConfigurationSaveFails_RevertsAndAllowsClose()
    {
        RunInSta(() =>
        {
            EnsureApplicationResources();
            var dialogService = Substitute.For<IDialogService>();
            var (popup, settings, _) = CreatePopup(
                Task.FromException(new InvalidOperationException("save failed")),
                dialogService);
            var closing = typeof(SettingsPopup).GetMethod(
                "OnPopupClosing",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var allowClose = typeof(SettingsPopup).GetField(
                "_allowClose",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var args = new CancelEventArgs();
            var loaded = typeof(SettingsPopup).GetMethod(
                "OnLoadedAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            popup.Loaded -= (RoutedEventHandler)loaded.CreateDelegate(typeof(RoutedEventHandler), popup);
            popup.ShowInTaskbar = false;
            popup.Opacity = 0;
            var wasClosed = false;
            popup.Closed += (_, _) => wasClosed = true;
            popup.Show();

            closing.Invoke(popup, [null, args]);
            PumpDispatcherUntil(() => wasClosed);

            Assert.True(args.Cancel);
            Assert.True(wasClosed);
            Assert.True((bool)allowClose.GetValue(popup)!);
            Assert.False(settings.HasPendingConfigurationChanges);
            dialogService.Received(1).ShowInformation(
                Arg.Any<string>(),
                "Settings",
                popup,
                MessageBoxButton.OK);
        });
    }

    private static (SettingsPopup Popup, SettingsVM Settings, IAppDataService AppData) CreatePopup(
        Task saveTask,
        IDialogService? dialogService = null,
        Task? subsequentSaveTask = null,
        IMessenger? messenger = null)
    {
        messenger ??= new WeakReferenceMessenger();
        var appData = CreateAppData(saveTask, subsequentSaveTask);
        var budget = new SettingsBudgetTabVM(() => 0m, appData, messenger);
        var personalization = new SettingsPersonalizationTabVM(
            appData,
            messenger,
            passwordProtector: new PassThroughUiLockPasswordProtector());
        var settings = new SettingsVM(
            null!,
            appData,
            Substitute.For<IStartupRegistrationService>(),
            Substitute.For<IUiSettleAwaiter>(),
            budget,
            null!,
            null!,
            null!,
            null!,
            null!,
            personalization,
            messenger);
        budget.LoadAsync().GetAwaiter().GetResult();
        personalization.LoadAsync().GetAwaiter().GetResult();
        personalization.ShouldRunAtStartup = true;

        return (
            new SettingsPopup(settings, dialogService ?? Substitute.For<IDialogService>(), messenger),
            settings,
            appData);
    }

    private static IAppDataService CreateAppData(Task saveTask, Task? subsequentSaveTask)
    {
        var appData = Substitute.For<IAppDataService>();
        appData.GetBudgetAllocationAsync(Arg.Any<CancellationToken>()).Returns(new BudgetAllocation());
        appData.GetAccountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Account>>([]));
        appData.GetUserSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserSettings>>([]));
        appData.GetUserSettingByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UserSettings?>(null));
        appData.AddUserSettingAsync(Arg.Any<UserSettings>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var saveCount = 0;
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            saveCount++ == 0 ? saveTask : subsequentSaveTask ?? saveTask);
        return appData;
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Themes/Dark.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml",
                     "Styles/SettingsStyle.xaml", "Styles/StepNavigatorStyle.xaml", "Styles/QuickSetupWizardStyle.xaml"
                 })
        {
            if (application.Resources.MergedDictionaries.Any(dictionary =>
                    dictionary.Source?.OriginalString.EndsWith(
                        $"Resources/{resource}",
                        StringComparison.OrdinalIgnoreCase) == true))
            {
                continue;
            }

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Fluxo.Resources;component/Resources/{resource}", UriKind.Relative)
            });
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static void PumpDispatcherUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        timer.Tick += (_, _) =>
        {
            if (!condition() && DateTime.UtcNow < deadline)
                return;

            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
