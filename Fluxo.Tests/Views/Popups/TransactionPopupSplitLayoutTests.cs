using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.ViewModels.Popups;
using Fluxo.Views.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class TransactionPopupSplitLayoutTests
{
    [Fact]
    public void SplitPanel_ShowsDashedAddSubTransactionButton()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger());
            viewModel.AddSplitCommand.Execute(null);
            var child = Assert.Single(viewModel.PendingTransaction.ChildTransactions);
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

            var branchTemplate = Assert.IsType<DataTemplate>(popup.FindResource("SplitTransactionBranchTemplate"));
            var branch = Assert.IsAssignableFrom<FrameworkElement>(branchTemplate.LoadContent());
            var host = new Window { Content = branch, DataContext = viewModel };
            branch.DataContext = child;
            host.Measure(new Size(800, 600));
            host.Arrange(new Rect(0, 0, 800, 600));
            host.UpdateLayout();

            var branchButton = FindButtons(branch).Single(candidate =>
                Equals(candidate.Content, "Add a sub-transaction"));

            branchButton.GetBindingExpression(Button.CommandProperty)!.UpdateTarget();
            branchButton.GetBindingExpression(Button.CommandParameterProperty)!.UpdateTarget();
            Assert.Same(viewModel.AddSplitCommand, branchButton.Command);
            Assert.Same(child, branchButton.CommandParameter);
            Assert.Same(popup.FindResource("DashedButtonStyle"), branchButton.Style);
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
