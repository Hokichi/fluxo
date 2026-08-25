using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Helpers.MainWindow;

namespace Fluxo.Views.Popups;

public partial class GlobalSearchPopup : BasePopup
{
    private readonly Task<IReadOnlyList<GlobalSearchResult>> _candidateTask;
    private IReadOnlyList<GlobalSearchResult> _candidates = [];
    private IReadOnlyList<GlobalSearchResultGroup> _groups = [];

    public ObservableCollection<GlobalSearchResult> VisibleResults { get; } = [];

    internal GlobalSearchResultType? SelectedType { get; private set; }

    public GlobalSearchResult? SelectedResult { get; private set; }

    public GlobalSearchPopup(Task<IReadOnlyList<GlobalSearchResult>> candidateTask)
    {
        _candidateTask = candidateTask ?? throw new ArgumentNullException(nameof(candidateTask));
        InitializeComponent();
        DataContext = this;
        ResultsListBox.ItemsSource = VisibleResults;
        StateText.Text = "Loading search...";
        Loaded += OnLoadedAsync;
    }

    internal void SetCandidatesForTest(IReadOnlyList<GlobalSearchResult> candidates)
    {
        _candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
    }

    internal void RefreshResults(string? query)
    {
        _groups = GlobalSearchEngine.Search(_candidates, query);
        SelectedType = GlobalSearchEngine.ResolveSelectedType(_groups, SelectedType);
        RebuildResultTypes();
        RefreshVisibleResults();
        UpdateResultContentVisibility(query);
        UpdateStateMessage(query);
    }

    internal void SelectType(GlobalSearchResultType type)
    {
        if (!_groups.Any(group => group.Type == type))
            return;

        SelectedType = type;
        ResultTypesListBox.SelectedItem = _groups.First(group => group.Type == type);
        RefreshVisibleResults();
    }

    internal void ActivateSelectedResult()
    {
        if (ResultsListBox.SelectedItem is not GlobalSearchResult result)
            return;

        SelectedResult = result;
        DialogResult = true;
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ResultsListBox.IsKeyboardFocusWithin)
        {
            ActivateSelectedResult();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down && QueryTextBox.IsKeyboardFocusWithin && VisibleResults.Count > 0)
        {
            ResultsListBox.SelectedIndex = 0;
            ResultsListBox.Focus();
            e.Handled = true;
            return;
        }

        if (ResultTypesListBox.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right)
        {
            MoveSelectedType(e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            _candidates = await _candidateTask;
            QueryTextBox.IsEnabled = true;
            StateText.Visibility = Visibility.Collapsed;
            QueryTextBox.Focus();
            QueryTextBox.SelectAll();
        }
        catch
        {
            StateText.Text = "Search is unavailable. Close and try again.";
        }
    }

    private void OnQueryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!QueryTextBox.IsEnabled)
            return;

        RefreshResults(QueryTextBox.Text);
    }

    private void OnResultTypeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultTypesListBox.SelectedItem is GlobalSearchResultGroup group)
        {
            SelectType(group.Type);
            return;
        }

        if (SelectedType is not null)
            ResultTypesListBox.SelectedItem = _groups.FirstOrDefault(group => group.Type == SelectedType);
    }

    private void OnResultMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ActivateSelectedResult();

    private void RebuildResultTypes()
    {
        ResultTypesListBox.ItemsSource = _groups;
        ResultTypesListBox.SelectedItem = _groups.FirstOrDefault(group => group.Type == SelectedType);
    }

    private void RefreshVisibleResults()
    {
        VisibleResults.Clear();
        var selectedGroup = _groups.FirstOrDefault(group => group.Type == SelectedType);
        if (selectedGroup is not null)
        {
            foreach (var result in selectedGroup.Results)
                VisibleResults.Add(result);
        }

        ResultsListBox.SelectedIndex = VisibleResults.Count > 0 ? 0 : -1;
        SelectedTypeText.Text = selectedGroup?.Name ?? string.Empty;
    }

    private void UpdateStateMessage(string? query)
    {
        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery) || normalizedQuery.Length < 4)
        {
            StateText.Text = string.Empty;
            StateText.Visibility = Visibility.Collapsed;
            return;
        }

        StateText.Text = _groups.Count == 0 ? "No matching results. Try another name." : string.Empty;
        StateText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateResultContentVisibility(string? query)
    {
        var isEligibleQuery = query?.Trim().Length >= 4;
        ResultContent.Visibility = isEligibleQuery && _groups.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MoveSelectedType(int offset)
    {
        if (SelectedType is null || _groups.Count < 2)
            return;

        var currentIndex = _groups.ToList().FindIndex(group => group.Type == SelectedType);
        if (currentIndex < 0)
            return;

        var nextIndex = (currentIndex + offset + _groups.Count) % _groups.Count;
        SelectType(_groups[nextIndex].Type);
        if (ResultTypesListBox.ItemContainerGenerator.ContainerFromIndex(nextIndex) is ListBoxItem option)
            option.Focus();
        else
            ResultTypesListBox.Focus();
    }
}
