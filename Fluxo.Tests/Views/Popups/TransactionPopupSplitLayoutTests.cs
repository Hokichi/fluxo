using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Styles;
using Fluxo.ViewModels.Popups;
using Fluxo.Views.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class TransactionPopupSplitLayoutTests
{
    [Fact]
    public void SplitPanel_without_subtransactions_shows_root_dashed_add_button()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger());
            var popup = new TransactionPopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var rootButton = FindButtons(popup).SingleOrDefault(candidate =>
                Equals(candidate.Content, "Add a sub-transaction"));

            Assert.NotNull(rootButton);
            rootButton.GetBindingExpression(Button.CommandProperty)!.UpdateTarget();
            Assert.Same(viewModel.AddSplitCommand, rootButton.Command);
            Assert.Null(rootButton.CommandParameter);
            Assert.Same(popup.FindResource("DashedButtonStyle"), rootButton.Style);
        });
    }

    [Fact]
    public void SplitTree_selection_syncs_to_the_loaded_transaction()
    {
        RunOnStaThread(() =>
        {
            var loaded = new object();
            var tree = new TreeView();
            var first = new TreeViewItem { DataContext = new object(), IsSelected = true };
            var selected = new TreeViewItem { DataContext = loaded };
            tree.Items.Add(first);
            tree.Items.Add(selected);

            TransactionSplitTreeStyles.SyncSelection(tree, loaded);

            Assert.False(first.IsSelected);
            Assert.True(selected.IsSelected);
        });
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Button button)
                yield return button;

            foreach (var descendant in FindButtons(child))
                yield return descendant;
        }
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception caught) { exception = caught; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            throw exception;
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Theme.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml",
                     "Styles/SettingsStyle.xaml", "Styles/StepNavigatorStyle.xaml", "Styles/QuickSetupWizardStyle.xaml"
                 })
        {
            if (application.Resources.MergedDictionaries.Any(dictionary =>
                    dictionary.Source?.OriginalString.EndsWith($"Resources/{resource}", StringComparison.OrdinalIgnoreCase) == true))
                continue;

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Fluxo.Resources;component/Resources/{resource}", UriKind.Relative)
            });
        }
    }
}
