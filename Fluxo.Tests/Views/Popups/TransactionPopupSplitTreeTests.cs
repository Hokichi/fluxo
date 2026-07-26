using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Enums;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.CustomControls;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.Views.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

[CollectionDefinition(nameof(TransactionPopupSplitTreeCollection), DisableParallelization = true)]
public sealed class TransactionPopupSplitTreeCollection;

[Collection(nameof(TransactionPopupSplitTreeCollection))]
public sealed class TransactionPopupSplitTreeTests
{
    [Fact]
    public void Node_and_add_rails_share_one_center_line()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var (viewModel, parent, grandchild, _) = CreateSplitTree();
            var (popup, layout) = ArrangePopup(viewModel);
            var tree = Assert.IsType<TreeView>(popup.FindName("SplitTree"));
            var root = GetContainer(tree, parent);
            var leaf = GetContainer(root, grandchild);

            AssertLaneAligned(root, layout);
            AssertLaneAligned(leaf, layout);
            AssertAddAligned(
                GetPart<Border>(root, "InnerSplitAddUpperRail"),
                GetPart<BalloonButton>(root, "InnerSplitAddButton"), layout);
            AssertAddAligned(
                Assert.IsType<Border>(popup.FindName("OuterSplitAddUpperRail")),
                Assert.IsType<BalloonButton>(popup.FindName("OuterSplitAddButton")), layout);
        });
    }

    [Fact]
    public void Root_toggle_changes_expansion_but_grandchild_toggle_does_not()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var (viewModel, parent, grandchild, _) = CreateSplitTree();
            var (popup, _) = ArrangePopup(viewModel);
            var tree = Assert.IsType<TreeView>(popup.FindName("SplitTree"));
            var root = GetContainer(tree, parent);
            var leaf = GetContainer(root, grandchild);
            var rootCard = GetCard(root);

            Assert.True(root.IsExpanded);
            Assert.Equal(Visibility.Visible, GetPart<Grid>(root, "SplitExpandedGroup").Visibility);

            RaiseSingleClick(rootCard);
            Assert.Same(parent, viewModel.SelectedSplitTransaction);
            Assert.True(root.IsExpanded);

            Assert.True(TransactionPopup.TryToggleSplitRoot(root));
            Assert.False(root.IsExpanded);
            Assert.Equal(Visibility.Collapsed, GetPart<Grid>(root, "SplitExpandedGroup").Visibility);

            Assert.True(TransactionPopup.TryToggleSplitRoot(root));
            Assert.True(root.IsExpanded);

            Assert.False(leaf.IsExpanded);
            Assert.False(TransactionPopup.TryToggleSplitRoot(leaf));
            Assert.False(leaf.IsExpanded);
        });
    }

    [Fact]
    public void Empty_root_still_has_inner_rail_and_add_when_expanded()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var (viewModel, _, _, emptyParent) = CreateSplitTree();
            var (popup, _) = ArrangePopup(viewModel);
            var tree = Assert.IsType<TreeView>(popup.FindName("SplitTree"));
            var root = GetContainer(tree, emptyParent);

            Assert.True(root.IsExpanded);
            Assert.Equal(Visibility.Visible, GetPart<Grid>(root, "SplitExpandedGroup").Visibility);
            Assert.Equal(Visibility.Visible, GetPart<Border>(root, "InnerSplitAddUpperRail").Visibility);
            Assert.Same(emptyParent, GetPart<BalloonButton>(root, "InnerSplitAddButton").CommandParameter);
        });
    }

    private static void AssertLaneAligned(TreeViewItem item, FrameworkElement ancestor)
    {
        var upper = GetPart<Border>(item, "SplitNodeUpperRail");
        var remove = GetPart<BalloonButton>(item, "SplitNodeRemoveButton");
        var lower = GetPart<Border>(item, "SplitNodeLowerRail");
        AssertClose(CenterX(upper, ancestor), CenterX(remove, ancestor));
        AssertClose(CenterX(lower, ancestor), CenterX(remove, ancestor));
        Assert.Equal(new Thickness(0, 2, 0, 2), remove.Margin);
    }

    private static void AssertAddAligned(Border rail, BalloonButton add, FrameworkElement ancestor)
    {
        AssertClose(CenterX(rail, ancestor), CenterX(add, ancestor));
        AssertClose(BottomY(rail, ancestor), CenterY(add, ancestor));
    }

    private static TreeViewItem GetContainer(ItemsControl owner, object item) =>
        Assert.IsType<TreeViewItem>(owner.ItemContainerGenerator.ContainerFromItem(item));

    private static T GetPart<T>(TreeViewItem item, string name) where T : FrameworkElement =>
        Assert.IsType<T>(item.Template.FindName(name, item));

    private static Border GetCard(TreeViewItem item)
    {
        var header = GetPart<ContentPresenter>(item, "SplitNodeHeader");
        return Assert.IsType<Border>(header.ContentTemplate.FindName("SplitNodeCard", header));
    }

    private static void RaiseSingleClick(Border card) => card.RaiseEvent(new MouseButtonEventArgs(
        Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
    {
        RoutedEvent = UIElement.MouseLeftButtonUpEvent,
        Source = card
    });

    private static (TransactionPopupVM ViewModel, TransactionVM Parent,
        TransactionVM Grandchild, TransactionVM EmptyParent) CreateSplitTree()
    {
        var viewModel = CreateViewModel();
        viewModel.AddSplit(null);
        var parent = Assert.Single(viewModel.SplitTransactions);
        viewModel.AmountText = 40m;
        viewModel.AddSplit(parent);
        var grandchild = Assert.Single(parent.ChildTransactions);
        viewModel.AmountText = 40m;
        viewModel.ReturnToSplitRoot();
        viewModel.AddSplit(null);
        var emptyParent = viewModel.SplitTransactions[1];
        viewModel.AmountText = 40m;
        viewModel.ReturnToSplitRoot();
        viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
        return (viewModel, parent, grandchild, emptyParent);
    }

    private static TransactionPopupVM CreateViewModel()
    {
        var account = new AccountVM
        {
            Id = 1,
            Name = "Checking",
            AccountType = AccountType.Checking,
            Balance = 10_000m,
            IsEnabled = true,
            IsDefault = true
        };
        var tag = new TagVM { Id = 1, Name = "General", HexCode = "#22C55E" };
        var viewModel = new TransactionPopupVM(
            Substitute.For<IAppDataService>(), new WeakReferenceMessenger());
        viewModel.ConfigureCatalogs([account], [tag], []);
        viewModel.NameText = "Root";
        viewModel.AmountText = 100m;
        viewModel.SelectedAccount = account;
        viewModel.SelectedTag = tag;
        return viewModel;
    }

    private static (TransactionPopup Popup, FrameworkElement Layout) ArrangePopup(TransactionPopupVM viewModel)
    {
        var popup = new TransactionPopup(viewModel);
        var layout = Assert.IsAssignableFrom<FrameworkElement>(popup.Content);
        popup.Content = null;
        layout.DataContext = viewModel;
        layout.Measure(new Size(1200, 1200));
        layout.Arrange(new Rect(layout.DesiredSize));
        layout.UpdateLayout();
        return (popup, layout);
    }

    private static Point Position(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).Transform(new Point());

    private static double CenterX(FrameworkElement element, FrameworkElement ancestor) =>
        Position(element, ancestor).X + element.ActualWidth / 2;

    private static double CenterY(FrameworkElement element, FrameworkElement ancestor) =>
        Position(element, ancestor).Y + element.ActualHeight / 2;

    private static double BottomY(FrameworkElement element, FrameworkElement ancestor) =>
        Position(element, ancestor).Y + element.ActualHeight;

    private static void AssertClose(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0, 0.75);

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
        if (exception is not null) throw exception;
    }

    private static void EnsureApplicationResources()
    {
        var application = Application.Current ?? new Application();
        foreach (var resource in new[]
                 {
                     "Theme.xaml", "Fonts.xaml", "Icons.xaml", "Converters.xaml",
                     "Styles/ContainerStyles.xaml", "Styles/ButtonStyles.xaml", "Styles/TextBoxStyles.xaml",
                     "Styles/GlobalStyles.xaml", "Styles/PopupStyles.xaml", "Styles/MainWindowStyles.xaml",
                     "Styles/StepNavigatorStyle.xaml", "Styles/QuickSetupWizardStyle.xaml"
                 })
        {
            if (application.Resources.MergedDictionaries.Any(dictionary =>
                    dictionary.Source?.OriginalString.EndsWith($"Resources/{resource}",
                        StringComparison.OrdinalIgnoreCase) == true))
                continue;

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"/Fluxo.Resources;component/Resources/{resource}", UriKind.Relative)
            });
        }
    }
}
