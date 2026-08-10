using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Fluxo.Core.Interfaces.Services;
using Fluxo.DataModels.Messages;
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
    public void Form_uses_capped_layout_with_fixed_transaction_type_toggles()
    {
        RunOnStaThread(() =>
        {
            var popup = CreatePopup();
            popup.Measure(new Size(800, 900));
            popup.Arrange(new Rect(0, 0, 800, 900));
            popup.UpdateLayout();

            var formLayout = Assert.IsType<Grid>(popup.FindName("TransactionFormLayout"));
            var form = Assert.IsType<FadingScrollViewer>(popup.FindName("TransactionFormScrollViewer"));
            var transactionTypeToggle = FindControls<SegmentedToggleGroup>(popup).Single(group =>
                FindControls<SegmentedToggleOption>(group).Any(option => Equals(option.Content, "Expense")));

            Assert.Equal(520d, formLayout.MaxHeight);
            Assert.Equal(1, Grid.GetRow(form));
            Assert.False(IsLogicalDescendantOf(transactionTypeToggle, form));
            Assert.Equal(ScrollBarVisibility.Disabled, form.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, form.VerticalScrollBarVisibility);
        });
    }

    [Fact]
    public void Tags_use_horizontal_fading_scroll_viewer_without_more_popup()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), new WeakReferenceMessenger());
            var popup = CreatePopup(viewModel);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var tagsScrollViewer = Assert.IsType<FadingScrollViewer>(popup.FindName("TagsScrollViewer"));
            var tagsList = Assert.IsType<ListBox>(popup.FindName("TagsListBox"));

            Assert.Equal(ScrollBarVisibility.Hidden, tagsScrollViewer.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, tagsScrollViewer.VerticalScrollBarVisibility);
            Assert.Same(viewModel.Tags, tagsList.ItemsSource);
            Assert.DoesNotContain(FindControls<ToggleButton>(popup), button => Equals(button.Content, "More"));
        });
    }

    [Fact]
    public void Tag_click_selects_tag()
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
    public void Shift_wheel_is_the_only_horizontal_tag_scroll_gesture()
    {
        Assert.True(TransactionPopup.IsHorizontalTagScroll(ModifierKeys.Shift));
        Assert.False(TransactionPopup.IsHorizontalTagScroll(ModifierKeys.None));
        Assert.True(TransactionPopup.IsHorizontalTagScroll(ModifierKeys.Control | ModifierKeys.Shift));
    }

    [Fact]
    public void Delete_shortcut_only_targets_active_bulk_queue_item_outside_text_editors()
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
    public void Child_split_equally_command_distributes_to_all_direct_grandchildren()
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
    public void CalculateTagScrollOffset_ClampsHorizontalDrag(
        double startingOffset,
        double horizontalDelta,
        double scrollableWidth,
        double expectedOffset)
    {
        Assert.Equal(expectedOffset,
            TransactionPopup.CalculateTagScrollOffset(startingOffset, horizontalDelta, scrollableWidth));
    }

    [Fact]
    public void Category_includes_excluded_option_and_no_budget_checkbox()
    {
        RunOnStaThread(() =>
        {
            var popup = CreatePopup();
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();
            var excluded = FindControls<BalloonRadioButton>(popup)
                .Single(button => Equals(button.UncheckedText, "Excluded"));

            var binding = excluded.GetBindingExpression(BalloonCheckBox.IsCheckedProperty)!.ParentBinding;

            Assert.Equal(nameof(TransactionPopupVM.IsExcludedCategory), binding.Path.Path);
            Assert.DoesNotContain(
                FindControls<BalloonCheckBox>(popup),
                checkBox => checkBox.GetBindingExpression(BalloonCheckBox.IsCheckedProperty)?.ParentBinding.Path.Path ==
                            nameof(TransactionPopupVM.IsBudgetExcluded));
        });
    }

    [Fact]
    public void BulkInsert_shows_queue_panel()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.IsBulkMode = true;
            var popup = new TransactionPopup(viewModel, bulk, splits);
            popup.Measure(new Size(1300, 800));
            popup.Arrange(new Rect(0, 0, 1300, 800));
            popup.UpdateLayout();

            var queue = FindControls<ListBox>(popup).Single(list => ReferenceEquals(list.ItemsSource, bulk.QueuedTransactionsView));
            var queuePanel = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(queue));

            Assert.Equal(Visibility.Visible, queuePanel.Visibility);
        });
    }

    [Fact]
    public void Transaction_date_fields_have_neighboring_time_selectors()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var popup = CreatePopup();

            Assert.IsType<TimeSelector>(popup.FindName("ExpenseTransactionTimeSelector"));
            Assert.IsType<TimeSelector>(popup.FindName("IncomeTransactionTimeSelector"));
            Assert.IsType<TimeSelector>(popup.FindName("GoalTransactionTimeSelector"));
        });
    }

    [Fact]
    public void Bulk_queue_item_has_no_context_menu_and_transparent_unselected_background()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            var popup = new TransactionPopup(viewModel, new TransactionBulkQueueVM(messenger),
                new TransactionSplitsVM(messenger));
            var item = new ListBoxItem
            {
                DataContext = new TransactionVM { Name = "Queue item" },
                Style = Assert.IsType<Style>(popup.FindResource("TransactionQueueListBoxItemStyle"))
            };
            item.ApplyTemplate();
            var itemBackground = Assert.IsType<Border>(item.Template.FindName("ItemBackground", item));

            Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(itemBackground.Background).Color);
            Assert.Null(item.ContextMenu);
        });
    }

    [Fact]
    public void SplitPanel_without_subtransactions_shows_root_dashed_add_button()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            var popup = new TransactionPopup(viewModel, bulk, splits);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var rootButton = FindButtons(popup).SingleOrDefault(candidate =>
                Equals(candidate.Content, "Add a sub-transaction"));

            Assert.NotNull(rootButton);
            rootButton.GetBindingExpression(Button.CommandProperty)!.UpdateTarget();
            Assert.Same(splits.AddSplitCommand, rootButton.Command);
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
    public void Queue_switch_updates_note_before_editing_selected_transaction()
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
    public void Queue_selection_switches_between_equal_new_transactions()
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

    [Fact]
    public void InvalidName_shows_hover_feedback_icon_without_affecting_layout()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                NameText = "Lunch"
            };
            viewModel.NameText = string.Empty;
            var popup = new TransactionPopup(
                viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
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
    public void Split_option_is_enabled_for_income_and_side_panel_matches_form_height()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                IsExpense = false
            };
            var popup = new TransactionPopup(
                viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
            popup.Measure(new Size(1300, 800));
            popup.Arrange(new Rect(0, 0, 1300, 800));
            popup.UpdateLayout();

            var split = FindControls<SegmentedToggleOption>(popup)
                .Single(option => Equals(option.Content, "Split"));
            var sidePanel = FindControls<Grid>(popup).Single(grid => grid.Width == 480);
            var popupGrid = Assert.IsType<Grid>(LogicalTreeHelper.GetParent(sidePanel));
            var formLayout = Assert.IsType<Grid>(popup.FindName("TransactionFormLayout"));
            var form = Assert.IsType<FadingScrollViewer>(popup.FindName("TransactionFormScrollViewer"));

            Assert.True(split.IsEnabled);
            Assert.False(IsLogicalDescendantOf(sidePanel, form));
            Assert.Equal(formLayout.ActualHeight, sidePanel.ActualHeight);
            Assert.Equal(0, sidePanel.TranslatePoint(new Point(), popupGrid).Y);
        });
    }

    [Fact]
    public void Root_split_card_tracks_name_after_goal_update_returns_to_expense()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                NameText = "Original",
                AmountText = 100m
            };
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
            splits.AddSplitCommand.Execute(null);
            splits.SelectSplitCommand.Execute(null);
            var popup = new TransactionPopup(viewModel, bulk, splits);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var rootCard = FindButtons(popup).Single(button =>
                ReferenceEquals(button.Style, popup.FindResource("SplitTransactionRootCardStyle")));
            var rootName = FindControls<TextBlock>(Assert.IsType<Grid>(rootCard.Content)).Single(textBlock =>
                textBlock.GetBindingExpression(TextBlock.TextProperty)?.ParentBinding.Path.Path == "RootTransaction.Name");

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
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                SelectedAccount = account,
                NameText = "Transaction",
                AmountText = 10m
            };
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
            splits.AddSplitCommand.Execute(null);
            splits.SelectSplitCommand.Execute(null);
            viewModel.AmountText = 0m;

            var popup = new TransactionPopup(viewModel, bulk, splits);
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
    public void BalanceUpdateCard_IsBelowNoteAndHidesUnavailableSections()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                NameText = "Transaction",
                AmountText = 10m
            };
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.NameText = "Transaction";
            viewModel.AmountText = 10m;
            viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
            splits.AddSplitCommand.Execute(null);
            var root = splits.RootTransaction!;
            Assert.True(root.ChildTransactions.Count == 1, "Split child was not created.");
            var leaf = root.ChildTransactions[0];
            root.IsIoU = true;
            root.ShouldAffectBalance = true;
            leaf.IsIoU = false;
            leaf.ShouldAffectBalance = false;
            splits.SelectSplitCommand.Execute(leaf);
            var popup = new TransactionPopup(viewModel, bulk, splits);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var cards = FindControls<Border>(popup).Where(control => control.Name == "BalanceUpdateCard").ToList();
            Assert.True(cards.Count == 1, "Balance update card was not found.");
            var card = cards[0];
            var notes = FindControls<TextBox>(popup).Where(control => control.Name == "NoteRichTextBox").ToList();
            Assert.True(notes.Count == 1, "Note field was not found.");
            var note = notes[0];
            var noteSection = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(LogicalTreeHelper.GetParent(note)!));
            var formSection = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(noteSection));

            Assert.Same(formSection, LogicalTreeHelper.GetParent(card));
            Assert.True(formSection.Children.IndexOf(noteSection) < formSection.Children.IndexOf(card));

            var categories = Assert.IsType<StackPanel>(popup.FindName("BalanceUpdateCategories"));
            var tags = Assert.IsType<StackPanel>(popup.FindName("BalanceUpdateTags"));
            Assert.Equal(Visibility.Collapsed, categories.Visibility);
            Assert.Equal(Visibility.Collapsed, tags.Visibility);
            var item = new ListViewItem { Style = Assert.IsType<Style>(popup.FindResource("BalanceUpdateListViewItemStyle")) };
            Assert.Equal(HorizontalAlignment.Stretch, item.HorizontalContentAlignment);
        });
    }

    [Fact]
    public void BalanceUpdateCard_HidesCategoriesAndTagsForUnselectableRoot()
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
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                SelectedAccount = account
            };
            var bulk = new TransactionBulkQueueVM(messenger);
            var splits = new TransactionSplitsVM(messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.NameText = "Transaction";
            viewModel.AmountText = 10m;
            viewModel.SelectedTag = new TagVM { Id = 1, Name = "General" };
            viewModel.SelectedSidePanel = TransactionPopupSidePanel.Split;
            splits.AddSplitCommand.Execute(null);
            splits.SelectSplitCommand.Execute(null);

            Assert.True(splits.RootTransaction!.ChildTransactions.Count > 0);
            Assert.False(viewModel.CanEditCategory);
            Assert.False(viewModel.CanEditTags);

            var popup = new TransactionPopup(viewModel, bulk, splits);
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var categories = Assert.IsType<StackPanel>(popup.FindName("BalanceUpdateCategories"));
            var tags = Assert.IsType<StackPanel>(popup.FindName("BalanceUpdateTags"));

            Assert.Equal(Visibility.Collapsed, categories.Visibility);
            Assert.Equal(Visibility.Collapsed, tags.Visibility);
        });
    }

    [Fact]
    public void BalanceUpdateCard_HidesCategoriesWhenBudgetExcluded()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.NameText = "Excluded transaction";
            viewModel.AmountText = 10m;
            viewModel.IsExcludedCategory = true;
            var popup = new TransactionPopup(viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var categories = Assert.IsType<StackPanel>(popup.FindName("BalanceUpdateCategories"));

            Assert.Equal(Visibility.Collapsed, categories.Visibility);
        });
    }

    [Fact]
    public void BalanceUpdateCard_ShowsTagsForSelectedEditableTag()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.NameText = "Tagged transaction";
            viewModel.AmountText = 10m;
            viewModel.SelectedTag = new TagVM { Id = 1, Name = "General" };
            var popup = new TransactionPopup(viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var tags = Assert.IsType<StackPanel>(popup.FindName("BalanceUpdateTags"));

            Assert.Equal(Visibility.Visible, tags.Visibility);
        });
    }

    [Fact]
    public void GoalSelector_IsRenderedBeforeBalanceUpdateCard()
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
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger)
            {
                SelectedAccount = account
            };
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.NameText = "Goal update";
            viewModel.AmountText = 10m;
            viewModel.IsGoal = true;
            var popup = new TransactionPopup(viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var card = Assert.IsType<Border>(popup.FindName("BalanceUpdateCard"));
            var goal = Assert.IsType<ComboBox>(popup.FindName("SharedGoalComboBox"));
            var goalSection = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(goal));
            var formSection = Assert.IsType<StackPanel>(LogicalTreeHelper.GetParent(card));

            Assert.Same(formSection, LogicalTreeHelper.GetParent(goalSection));
            Assert.True(formSection.Children.IndexOf(goalSection) < formSection.Children.IndexOf(card));
        });
    }

    [Fact]
    public void Repayment_ExpandsCategoryAccountFirstColumn()
    {
        RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            var messenger = new WeakReferenceMessenger();
            var viewModel = new TransactionPopupVM(Substitute.For<IAppDataService>(), messenger);
            viewModel.InitializeAsync().GetAwaiter().GetResult();
            viewModel.IsRepayment = true;
            var popup = new TransactionPopup(viewModel, new TransactionBulkQueueVM(messenger), new TransactionSplitsVM(messenger));
            popup.Measure(new Size(800, 600));
            popup.Arrange(new Rect(0, 0, 800, 600));
            popup.UpdateLayout();

            var categoryAccountGrid = Assert.IsType<Grid>(popup.FindName("CategoryAccountGrid"));

            Assert.Equal(GridUnitType.Star, categoryAccountGrid.ColumnDefinitions[0].Width.GridUnitType);
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
