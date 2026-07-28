using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluxo.ViewModels.Shell.Main;

public sealed class LedgerFilterSelectionPresentationVM(
    string label,
    Func<int> selectionCountProvider,
    Func<string?> selectionToolTipProvider)
    : ObservableObject
{
    public string Label { get; } = label;
    public int SelectionCount => selectionCountProvider();
    public string? SelectionToolTip => selectionToolTipProvider();

    public void Refresh()
    {
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(SelectionToolTip));
    }
}
