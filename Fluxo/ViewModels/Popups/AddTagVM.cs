using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;

using Fluxo.DataModels.Popups.AddTag;
namespace Fluxo.ViewModels.Popups;

public partial class AddTagVM : ObservableObject
{
    private const string DefaultColor = "#3FE0A1";
    private FormState _initialState;
    private bool _isChangeTrackingInitialized;
    private readonly int? _editingId;

    [ObservableProperty] private string _nameText = string.Empty;
    [ObservableProperty] private string _selectedColorHex = DefaultColor;
    [ObservableProperty] private string _spendingLimitText = string.Empty;

    public ObservableCollection<TagColorOptionVM> ColorOptions { get; } =
    [
        new("Mint", "#3FE0A1"),
        new("Ocean", "#4DA3FF"),
        new("Sun", "#FFB020"),
        new("Coral", "#FF7A59"),
        new("Rose", "#FF5C5C"),
        new("Lavender", "#A78BFA"),
        new("Sky", "#38BDF8"),
        new("Lime", "#84CC16"),
        new("Amber", "#F59E0B"),
        new("Peach", "#FB7185"),
        new("Slate", "#7C8796"),
        new("Teal", "#14B8A6")
    ];

    public AddTagVM(int? editingId = null, string? nameText = null, string? selectedColorHex = null, decimal? spendingLimit = null)
    {
        _editingId = editingId;
        NameText = (nameText ?? string.Empty).Trim();
        SelectedColorHex = NormalizeHex(selectedColorHex ?? DefaultColor);
        SpendingLimitText = spendingLimit.HasValue
            ? spendingLimit.Value.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        EnsureColorOptionExists(SelectedColorHex);
        _initialState = CaptureState();
    }

    public bool HasChanges => _isChangeTrackingInitialized && !CaptureState().Equals(_initialState);
    public bool IsEditMode => _editingId.HasValue;
    public string PopupTitle => IsEditMode ? "Edit Tag" : "Add New Tag";
    public string HeaderTitle => IsEditMode ? "Edit Tag" : "Create Tag";
    public string DescriptionText => IsEditMode
        ? "Update the tag label or color."
        : "Choose a color and set a label for the tag.";

    public void BeginChangeTracking()
    {
        _initialState = CaptureState();
        _isChangeTrackingInitialized = true;
        NotifyFormStateChanged();
    }

    partial void OnNameTextChanged(string value) => NotifyFormStateChanged();
    partial void OnSelectedColorHexChanged(string value) => NotifyFormStateChanged();
    partial void OnSpendingLimitTextChanged(string value) => NotifyFormStateChanged();

    public void AddCustomColorToFront(string hexCode)
    {
        var normalized = NormalizeHex(hexCode);
        AddColorToFront(normalized);
        SelectedColorHex = normalized;
        NotifyFormStateChanged();
    }

    private FormState CaptureState()
    {
        var optionFingerprint = string.Join("|", ColorOptions.Select(option => option.HexCode));
        return new FormState(NameText ?? string.Empty, SelectedColorHex ?? DefaultColor, SpendingLimitText ?? string.Empty, optionFingerprint);
    }

    private void NotifyFormStateChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
    }

    private static string NormalizeHex(string value)
    {
        var normalized = (value ?? string.Empty).Trim().TrimStart('#').ToUpperInvariant();
        return normalized.Length == 6 ? $"#{normalized}" : DefaultColor;
    }

    private void EnsureColorOptionExists(string hexCode)
    {
        var normalized = NormalizeHex(hexCode);
        var hasMatch = ColorOptions.Any(option =>
            string.Equals(option.HexCode, normalized, StringComparison.OrdinalIgnoreCase));
        if (!hasMatch)
            AddColorToFront(normalized);
    }

    private void AddColorToFront(string hexCode)
    {
        var existingMatch = ColorOptions.FirstOrDefault(option =>
            string.Equals(option.HexCode, hexCode, StringComparison.OrdinalIgnoreCase));
        if (existingMatch is not null)
            ColorOptions.Remove(existingMatch);

        ColorOptions.Insert(0, new TagColorOptionVM("Custom", hexCode));
        if (ColorOptions.Count > 12)
            ColorOptions.RemoveAt(ColorOptions.Count - 1);
    }

}
