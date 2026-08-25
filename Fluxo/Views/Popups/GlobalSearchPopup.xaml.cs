using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Fluxo.DataModels.Popups.GlobalSearch;
using Fluxo.Helpers.MainWindow;
using Fluxo.Resources.CustomControls;

namespace Fluxo.Views.Popups;

public partial class GlobalSearchPopup : BasePopup
{
    private readonly Task<IReadOnlyList<GlobalSearchResult>> _candidateTask;
    private IReadOnlyList<GlobalSearchResult> _candidates = [];
    private IReadOnlyList<GlobalSearchResultGroup> _groups = [];

    internal ObservableCollection<GlobalSearchResult> VisibleResults { get; } = [];

    internal GlobalSearchResultType? SelectedType { get; private set; }

    public GlobalSearchResult? SelectedResult { get; private set; }

    public GlobalSearchPopup(Task<IReadOnlyList<GlobalSearchResult>> candidateTask)
    {
        _candidateTask = candidateTask ?? throw new ArgumentNullException(nameof(candidateTask));
        InitializeComponent();
        DataContext = this;
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
        RebuildTypeOptions();
        RefreshVisibleResults();
        UpdateStateMessage(query);
    }

    internal void SelectType(GlobalSearchResultType type)
    {
        if (!_groups.Any(group => group.Type == type))
            return;

        SelectedType = type;
        ResultTypeGroup.SelectedValue = type;
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

        if (ResultTypeGroup.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right)
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
            StateText.Text = "Type at least 4 characters.";
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

    private void OnResultTypeSelected(object sender, RoutedEventArgs e)
    {
        if (ResultTypeGroup.SelectedValue is GlobalSearchResultType type)
            SelectType(type);
    }

    private void OnResultMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ActivateSelectedResult();

    private void RebuildTypeOptions()
    {
        ResultTypeGroup.Items.Clear();
        foreach (var group in _groups)
        {
            var option = new SegmentedToggleOption
            {
                Content = group,
                ContentTemplate = (DataTemplate)FindResource("GlobalSearchTypePillContentTemplate"),
                IsSelected = group.Type == SelectedType,
                Value = group.Type
            };
            AutomationProperties.SetName(option, group.Name);
            ResultTypeGroup.Items.Add(option);
        }

        ResultTypeGroup.SelectedValue = SelectedType;
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
        StateText.Text = string.IsNullOrEmpty(normalizedQuery) || normalizedQuery.Length < 4
            ? "Type at least 4 characters."
            : _groups.Count == 0
                ? "No matching results. Try another name."
                : string.Empty;
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
        if (ResultTypeGroup.Items[nextIndex] is SegmentedToggleOption option)
            option.Focus();
    }
}
