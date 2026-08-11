using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
using Fluxo.DataModels.Popups.TransactionPopup;
using Fluxo.Resources.Components;
using Fluxo.Resources.CustomControls;
using Fluxo.Resources.Styles;
using Fluxo.ViewModels.Entities;
using Fluxo.ViewModels.Popups;
using Fluxo.Views.Popups;
using NSubstitute;
using Xunit;

namespace Fluxo.Tests.Views.Popups;

public sealed class TransactionPopupSplitLayoutTests
{


    [Fact]
    public void TransactionPopupSplitLayout_Tag_ClickSelects_Tag()
    {
        RunOnStaThread(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            var tag = new TagVM { Id = 1, Name = "General", HexCode = "#111111" };
            viewModel.Tags.Add(tag);
            var popup = CreatePopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();
            var tags = Assert.IsType<ListBox>(popup.FindName("TagsListBox"));

            RaisePreviewTagClick(popup, Assert.IsType<FadingScrollViewer>(popup.FindName("TagsScrollViewer")), tag);

            Assert.Same(tag, tags.SelectedItem);
            Assert.Same(tag, viewModel.SelectedTag);
        });
    }

    [Fact]
    public void TransactionPopupSplitLayout_Clicking_SelectedTagClearsTransaction_Tag()
    {
        RunOnStaThread(() =>
        {
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            var tag = new TagVM { Id = 1, Name = "General", HexCode = "#111111" };
            viewModel.Tags.Add(tag);
            var popup = CreatePopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();
            var tags = Assert.IsType<ListBox>(popup.FindName("TagsListBox"));
            var scrollViewer = Assert.IsType<FadingScrollViewer>(popup.FindName("TagsScrollViewer"));
            tags.SelectedItem = tag;
            viewModel.SelectedTag = tag;

            RaisePreviewTagClick(popup, scrollViewer, tag);

            Assert.Null(tags.SelectedItem);
            Assert.Null(viewModel.SelectedTag);
        });
    }

    [Fact]
    public void TransactionPopupSplitLayout_Shift_WheelIsTheOnlyHorizontalTagScroll_Gesture()
    {
        Assert.True(TransactionPopup.IsHorizontalTagScroll(ModifierKeys.Shift));
        Assert.False(TransactionPopup.IsHorizontalTagScroll(ModifierKeys.None));
        Assert.True(TransactionPopup.IsHorizontalTagScroll(ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void TransactionPopupSplitLayout_Delete_ShortcutOnlyTargetsActiveBulkQueueItemOutsideText_Editors()
    {
        RunOnStaThread(() =>
        {
            var transaction = new TransactionVM();

            Assert.True(TransactionPopup.CanDeleteActiveQueuedTransaction(
                true, transaction, Key.Delete, ModifierKeys.None, new Button()));
            Assert.False(TransactionPopup.CanDeleteActiveQueuedTransaction(
                true, transaction, Key.Delete, ModifierKeys.None, new TextBox()));
            Assert.False(TransactionPopup.CanDeleteActiveQueuedTransaction(
                true, transaction, Key.Delete, ModifierKeys.Control, new Button()));
            Assert.False(TransactionPopup.CanDeleteActiveQueuedTransaction(
                false, transaction, Key.Delete, ModifierKeys.None, new Button()));
            Assert.False(TransactionPopup.CanDeleteActiveQueuedTransaction(
                true, null, Key.Delete, ModifierKeys.None, new Button()));
        });
    }

    [Fact]
    public void TransactionPopupSplitLayout_Child_SplitEquallyCommandDistributesToAllDirect_Grandchildren()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.NameText = "Transaction";
            viewModel.AmountText = 100m;
            viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
            splits.AddSplitCommand.Execute(null);
            var root = splits.RootTransaction!;
            var child = Assert.Single(root.ChildTransactions);
            viewModel.AmountText = 100m;
            splits.AddSplitCommand.Execute(child);
            splits.AddSplitCommand.Execute(child);
            splits.AddSplitCommand.Execute(child);
            Assert.Equal(3, child.ChildTransactions.Count);
            splits.SplitEquallyCommand.Execute(child);

            Assert.Equal([33m, 33m, 34m], child.ChildTransactions.Select(item => item.Amount));
        });
    }

    [Theory]
    [InlineData(120d, 30d, 500d, 90d)]
    [InlineData(20d, 50d, 500d, 0d)]
    [InlineData(480d, -80d, 500d, 500d)]
    public void TransactionPopupSplitLayout_CalculateTagScrollOffset_ClampsHorizontalDrag(
        double startingOffset,
        double horizontalDelta,
        double scrollableWidth,
        double expectedOffset)
    {
        Assert.Equal(expectedOffset,
            TransactionPopup.CalculateTagScrollOffset(startingOffset, horizontalDelta, scrollableWidth));
    }








    [Fact]
    public void TransactionPopupSplitLayout_SplitTree_SelectionSyncsToTheLoaded_Transaction()
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
    public void TransactionPopupSplitLayout_Queue_SwitchUpdatesNoteBeforeEditingSelected_Transaction()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                NameText = "First",
                AmountText = 10m,
                NoteText = "First note"
            };
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.IsBulkMode = true;
            var first = bulk.SelectedQueuedTransaction!;
            bulk.AddQueuedTransactionCommand.Execute(null);
            viewModel.NameText = "Second";
            viewModel.NoteText = "Second note";
            var popup = new TransactionPopup(viewModel, bulk, splits);
            var note = Assert.IsType<TextBox>(popup.FindName("NoteRichTextBox"));

            bulk.SelectedQueuedTransaction = first;

            Assert.Equal("First note", note.Text);
            note.Text = "Edited first note";
            Assert.Equal("Edited first note", first.Notes);
        });
    }

    [Fact]
    public void TransactionPopupSplitLayout_Queue_SelectionSwitchesBetweenEqualNew_Transactions()
    {
        RunOnStaThread(() =>
        {
            var first = new TransactionVM();
            var second = new TransactionVM();
            var third = new TransactionVM();
            var queue = new ObservableCollection<TransactionVM> { first, second, third };
            var listBox = new ListBox { ItemsSource = queue };
            listBox.Measure(new Size(400, 300));
            listBox.Arrange(new Rect(0, 0, 400, 300));
            listBox.UpdateLayout();

            listBox.SelectedItem = first;
            Assert.Same(first, listBox.SelectedItem);

            listBox.SelectedItem = second;
            Assert.Same(second, listBox.SelectedItem);

            listBox.SelectedItem = third;
            Assert.Same(third, listBox.SelectedItem);
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

    private static bool IsLogicalDescendantOf(DependencyObject control, DependencyObject ancestor)
    {
        for (var current = LogicalTreeHelper.GetParent(control); current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
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

    private static TransactionPopup CreatePopup(TransactionPopupVM? viewModel = null)
    {
        EnsureApplicationResources();
        var messenger = new WeakReferenceMessenger();
        viewModel ??= new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
        return new TransactionPopup(viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
    }

    private static void RaisePreviewTagClick(TransactionPopup popup, FadingScrollViewer scrollViewer, TagVM tag)
    {
        var item = new ListBoxItem { DataContext = tag };
        popup.OnTagsScrollViewerPreviewMouseLeftButtonDown(scrollViewer, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
            Source = item
        });
        popup.OnTagsScrollViewerPreviewMouseLeftButtonUp(scrollViewer, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseUpEvent,
            Source = item
        });
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
