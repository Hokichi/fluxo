using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.Resources.Components;
using Fluxo.Resources.CustomControls;
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

    [Fact]
    public void InvalidName_shows_hover_feedback_icon_without_affecting_layout()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger())
            {
                NameText = "Lunch"
            };
            viewModel.NameText = string.Empty;
            var popup = new TransactionPopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var feedback = FindControls<ContentControl>(popup).Single(control =>
                ReferenceEquals(control.Content, viewModel.NameFeedback));

            Assert.NotNull(feedback.ContentTemplate);

            var feedbackHost = Assert.IsType<Grid>(feedback.ContentTemplate.LoadContent());
            feedbackHost.DataContext = viewModel.NameFeedback;
            feedbackHost.Measure(new Size(100, 100));
            feedbackHost.Arrange(new Rect(0, 0, 100, 100));
            feedbackHost.UpdateLayout();
            Assert.Equal(10, feedbackHost.DesiredSize.Width);
            Assert.Equal(10, feedbackHost.DesiredSize.Height);

            var feedbackIcon = FindVisualControls<Icon>(feedbackHost).Single(icon =>
                Equals(icon.Path, popup.FindResource("ExclamationTriangle")));
            var feedbackPopup = FindControls<Popup>(feedbackHost).Single(candidate =>
                ReferenceEquals(candidate.PlacementTarget, feedbackIcon));

            Assert.Equal(popup.FindResource("Brush.Danger"), feedbackIcon.Color);
            Assert.Equal("IsMouseOver", feedbackPopup.GetBindingExpression(Popup.IsOpenProperty)!.ParentBinding.Path.Path);
        });
    }

    [Fact]
    public void Split_option_is_disabled_for_income()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger())
            {
                IsExpense = false
            };
            var popup = new TransactionPopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var split = FindControls<SegmentedToggleOption>(popup)
                .Single(option => Equals(option.Content, "Split"));

            Assert.False(split.IsEnabled);
        });
    }

    [Fact]
    public void Root_split_card_tracks_name_after_goal_update_returns_to_expense()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger())
            {
                NameText = "Original",
                AmountText = 100m
            };
            viewModel.AddSplitCommand.Execute(null);
            viewModel.SelectSplitCommand.Execute(null);
            var popup = new TransactionPopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var rootCard = FindButtons(popup).Single(button =>
                ReferenceEquals(button.Style, popup.FindResource("SplitTransactionRootCardStyle")));
            var rootName = FindControls<TextBlock>(Assert.IsType<Grid>(rootCard.Content)).Single(textBlock =>
                textBlock.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == "PendingTransaction.Name");

            viewModel.IsGoal = true;
            viewModel.IsExpense = true;
            viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
            viewModel.NameText = "Current";
            popup.UpdateLayout();

            Assert.Equal("Current", rootName.Text);
        });
    }

    [Fact]
    public void BalanceUpdateCard_ShowsAtZeroAndHidesForInstallments()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var account = new Fluxo.ViewModels.Entities.AccountVM
            {
                Id = 1,
                Name = "Checking",
                Balance = 500m,
                IsEnabled = true
            };
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger())
            {
                SelectedAccount = account,
                NameText = "Transaction",
                AmountText = 10m
            };
            viewModel.AddSplitCommand.Execute(null);
            viewModel.SelectSplitCommand.Execute(null);
            viewModel.AmountText = 0m;

            var popup = new TransactionPopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var card = FindControls<Border>(popup).Single(control => control.Name == "BalanceUpdateCard");
            Assert.Equal(Visibility.Visible, card.Visibility);
            Assert.Equal(2, FindControls<ListView>(card).Count());

            viewModel.IsInstallments = true;
            popup.UpdateLayout();

            Assert.Equal(Visibility.Collapsed, card.Visibility);
        });
    }

    [Fact]
    public void BalanceUpdateCard_IsBelowNoteAndHidesCategoriesForPostedIoU()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger())
            {
                NameText = "Transaction",
                AmountText = 10m
            };
            viewModel.AddSplitCommand.Execute(null);
            var root = viewModel.PendingTransaction;
            var leaf = root.ChildTransactions.Single();
            root.IsIoU = true;
            root.ShouldAffectBalance = true;
            leaf.IsIoU = false;
            leaf.ShouldAffectBalance = false;
            viewModel.SelectSplitCommand.Execute(leaf);
            var popup = new TransactionPopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var card = FindControls<Border>(popup).Single(control => control.Name == "BalanceUpdateCard");
            var note = FindControls<TextBox>(popup).Single(control => control.Name == "NoteRichTextBox");
            var noteSection = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(LogicalTreeHelper.GetParent(note)!));
            var formSection = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(noteSection));

            Assert.Same(formSection, LogicalTreeHelper.GetParent(card));
            Assert.True(formSection.Children.IndexOf(noteSection) < formSection.Children.IndexOf(card));

            var categories = FindControls<TextBlock>(card).Single(control => control.Text == "Categories:");
            var tags = FindControls<TextBlock>(card).Single(control => control.Text == "Tags:");
            Assert.Equal(Visibility.Collapsed, LogicalTreeHelper.GetParent(categories)!.GetValue(UIElement.VisibilityProperty));
            Assert.Equal(Visibility.Visible, LogicalTreeHelper.GetParent(tags)!.GetValue(UIElement.VisibilityProperty));
            var item = new ListViewItem { Style = Assert.IsType<Style>(popup.FindResource("BalanceUpdateListViewItemStyle")) };
            Assert.Equal(HorizontalAlignment.Stretch, item.HorizontalContentAlignment);
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

    private static IEnumerable<T> FindControls<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T control)
                yield return control;

            foreach (var descendant in FindControls<T>(child))
                yield return descendant;
        }
    }

    private static IEnumerable<T> FindVisualControls<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T control)
                yield return control;

            foreach (var descendant in FindVisualControls<T>(child))
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
