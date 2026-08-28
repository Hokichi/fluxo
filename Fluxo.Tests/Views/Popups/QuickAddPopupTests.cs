using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Constants;
using Fluxo.Core.Entities;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Resources.CustomControls;
using Fluxo.Services.Dialogs;
using Fluxo.ViewModels.Popups;
using Fluxo.ViewModels.Shell.Main;
using Fluxo.Views.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class QuickAddPopupTests
{
    [Fact]
    public void EditButton_ShowsFullCatalogAndUsesPersistentPenLabel()
    {
        RunOnStaThread(() =>
        {
            var (popup, viewModel) = CreateShownPopup("ViewAccounts,NewTag");
            var editButton = Assert.IsType<BalloonButton>(popup.FindName("EditQuickAccessButton"));
            var rows = Assert.IsType<ItemsControl>(popup.FindName("QuickAccessRows"));

            Assert.Equal("Edit", editButton.ButtonText);
            Assert.True(editButton.ShouldShowText);
            Assert.Equal(12, viewModel.TileRows.SelectMany(row => row).Count());

            editButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, editButton));
            popup.UpdateLayout();

            Assert.True(viewModel.IsEditing);
            Assert.Equal("Done", editButton.ButtonText);
            Assert.Equal(14, viewModel.TileRows.SelectMany(row => row).Count());
            Assert.Equal(5, rows.Items.Count);
            Assert.Equal("Done", AutomationProperties.GetName(editButton));
            popup.Close();
        });
    }

    [Fact]
    public void EditingHiddenTile_DimsItAndClickEnablesItWithoutActivation()
    {
        RunOnStaThread(() =>
        {
            var (popup, viewModel) = CreateShownPopup("NewTransaction");
            var editButton = Assert.IsType<BalloonButton>(popup.FindName("EditQuickAccessButton"));

            Assert.DoesNotContain(VisualChildren<Button>(popup), button =>
                button.DataContext is QuickAccessTileVM
                {
                    Target: GlobalSearchFeatureTarget.NewTransaction
                });

            editButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, editButton));
            popup.UpdateLayout();
            PumpDispatcher();
            var tileButton = TileButton(popup, GlobalSearchFeatureTarget.NewTransaction);

            Assert.True(tileButton.IsEnabled);
            Assert.Equal(0.4d, tileButton.Opacity);
            Assert.Equal("New Transaction", AutomationProperties.GetName(tileButton));
            Assert.Contains("Hidden from Quick Access", AutomationProperties.GetHelpText(tileButton));
            Assert.Contains(VisualChildren<TextBlock>(tileButton), text => text.Text == "Hidden");

            tileButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, tileButton));

            Assert.True(viewModel.Tiles.Single(tile =>
                tile.Target == GlobalSearchFeatureTarget.NewTransaction).IsUserEnabled);
            Assert.Equal(1d, tileButton.Opacity);
            viewModel.Toggle(viewModel.Tiles.Single(tile =>
                tile.Target == GlobalSearchFeatureTarget.NewTransaction));
            popup.Close();
        });
    }

    [Fact]
    public void OperationalGate_DimsNormalTileButEditRestoresInteractionAndFocus()
    {
        RunOnStaThread(() =>
        {
            var (popup, viewModel) = CreateShownPopup(isSufficientFundsLocked: true);
            var tileButton = TileButton(popup, GlobalSearchFeatureTarget.NewTransaction);

            Assert.True(double.IsNaN(tileButton.Height));
            Assert.Equal(112d, tileButton.MinHeight);
            Assert.False(tileButton.IsEnabled);
            Assert.Equal(0.4d, tileButton.Opacity);
            Assert.Contains("Currently unavailable", AutomationProperties.GetHelpText(tileButton));

            var editButton = Assert.IsType<BalloonButton>(popup.FindName("EditQuickAccessButton"));
            editButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, editButton));
            popup.UpdateLayout();
            PumpDispatcher();
            tileButton = TileButton(popup, GlobalSearchFeatureTarget.NewTransaction);

            Assert.True(tileButton.IsEnabled);
            Assert.Equal(1d, tileButton.Opacity);
            Assert.True(tileButton.Focus());
            PumpDispatcher();
            Assert.True(tileButton.IsKeyboardFocused);
            Assert.Equal(popup.FindResource("Brush.Border.Focus"), tileButton.BorderBrush);
            Assert.True(viewModel.IsEditing);
            popup.Close();
        });
    }

    [Fact]
    public void AllTilesHidden_ShowsEmptyStateWithEditActionStillAvailable()
    {
        RunOnStaThread(() =>
        {
            var disabledTargets = string.Join(",", new[]
            {
                GlobalSearchFeatureTarget.NewTransaction,
                GlobalSearchFeatureTarget.ViewAccounts,
                GlobalSearchFeatureTarget.NewAccount,
                GlobalSearchFeatureTarget.NewRecurringTransaction,
                GlobalSearchFeatureTarget.NewSavingGoal,
                GlobalSearchFeatureTarget.NewTag,
                GlobalSearchFeatureTarget.PlanningReport,
                GlobalSearchFeatureTarget.BudgetForecast,
                GlobalSearchFeatureTarget.SearchEverything,
                GlobalSearchFeatureTarget.DataManagement,
                GlobalSearchFeatureTarget.LockApplication,
                GlobalSearchFeatureTarget.RunQuickSetup,
                GlobalSearchFeatureTarget.Hotkeys,
                GlobalSearchFeatureTarget.CheckForUpdates
            });
            var (popup, _) = CreateShownPopup(disabledTargets);

            var emptyState = Assert.IsType<TextBlock>(popup.FindName("QuickAccessEmptyState"));
            var editButton = Assert.IsType<BalloonButton>(popup.FindName("EditQuickAccessButton"));
            Assert.Equal(Visibility.Visible, emptyState.Visibility);
            Assert.Contains("Choose Edit", emptyState.Text);
            Assert.True(editButton.IsEnabled);
            popup.Close();
        });
    }

    private static (QuickAddPopup Popup, QuickAccessVM ViewModel) CreateShownPopup(
        string? disabledTiles = null,
        bool isSufficientFundsLocked = false)
    {
        EnsureApplicationResources();
        var appData = Substitute.For<IAppDataService>();
        var setting = disabledTiles is null
            ? null
            : new UserSettings
            {
                Name = UserSettingNames.DisabledQuickAccessTiles,
                Value = disabledTiles
            };
        var loadCompleted = false;
        appData.GetUserSettingByNameAsync(UserSettingNames.DisabledQuickAccessTiles,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadCompleted = true;
                return Task.FromResult(setting);
            });
        appData.AddUserSettingAsync(Arg.Any<UserSettings>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        appData.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var messenger = new WeakReferenceMessenger();
        var dashboard = new DashboardVM(null!, null!, null!, null!, null!, null!)
        {
            IsSufficientFundsActionGateLocked = isSufficientFundsLocked
        };
        var mainViewModel = new MainVM(appData, dashboard, null!, messenger: messenger);
        var viewModel = new QuickAccessVM(appData);
        var dialogService = Substitute.For<IDialogService>();
        dialogService.ShowQuestion(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Window?>(),
                Arg.Any<MessageBoxButton>())
            .Returns(MessageBoxResult.No);
        var popup = new QuickAddPopup(viewModel, mainViewModel, dialogService, messenger)
        {
            ShowInTaskbar = false,
            Opacity = 0
        };
        popup.Show();
        PumpDispatcherUntil(() => loadCompleted);
        popup.UpdateLayout();
        PumpDispatcher();
        return (popup, viewModel);
    }

    private static Button TileButton(DependencyObject root, GlobalSearchFeatureTarget target) =>
        Assert.Single(VisualChildren<Button>(root), button =>
            button.DataContext is QuickAccessTileVM tile && tile.Target == target);

    private static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in VisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        foreach (var resource in new[]
                 {
                     "Themes/Dark.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml"
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

    private static void PumpDispatcherUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!condition() && DateTime.UtcNow < deadline)
            PumpDispatcher();
        Assert.True(condition());
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnStaThread(Action action)
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
            throw new Xunit.Sdk.XunitException(exception.ToString());
    }
}
