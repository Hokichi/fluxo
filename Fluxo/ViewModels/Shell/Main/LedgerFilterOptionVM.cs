using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluxo.ViewModels.Shell.Main;

public sealed partial class LedgerFilterOptionVM<T> : ObservableObject
{
    [ObservableProperty] private bool _isChecked;

    public LedgerFilterOptionVM(string label, T? value, bool isAll = false, bool isChecked = false)
    {
        Label = label;
        Value = value;
        IsAll = isAll;
        _isChecked = isChecked;
    }

    public string Label { get; }
    public T? Value { get; }
    public bool IsAll { get; }
}
