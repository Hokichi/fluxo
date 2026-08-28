using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluxo.DataModels.Popups.GlobalSearch;

namespace Fluxo.ViewModels.Popups;

public sealed class QuickAccessTileVM : ObservableObject
{
    private bool _isEditing;
    private bool _isOperationallyAvailable = true;
    private bool _isUserEnabled = true;

    public QuickAccessTileVM(
        GlobalSearchFeatureTarget target,
        string title,
        string description,
        Geometry icon,
        bool requiresSufficientFunds)
    {
        Target = target;
        Title = title;
        Description = description;
        Icon = icon;
        RequiresSufficientFunds = requiresSufficientFunds;
    }

    public GlobalSearchFeatureTarget Target { get; }
    public string Title { get; }
    public string Description { get; }
    public Geometry Icon { get; }
    public bool RequiresSufficientFunds { get; }

    public bool IsUserEnabled
    {
        get => _isUserEnabled;
        internal set
        {
            if (SetProperty(ref _isUserEnabled, value))
                NotifyPresentationChanged();
        }
    }

    public bool IsOperationallyAvailable
    {
        get => _isOperationallyAvailable;
        internal set
        {
            if (SetProperty(ref _isOperationallyAvailable, value))
                NotifyPresentationChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        internal set
        {
            if (SetProperty(ref _isEditing, value))
                NotifyPresentationChanged();
        }
    }

    public bool IsVisible => IsEditing || IsUserEnabled;
    public bool IsActionEnabled => IsEditing || (IsUserEnabled && IsOperationallyAvailable);
    public double PresentationOpacity => IsEditing
        ? IsUserEnabled ? 1d : 0.4d
        : IsOperationallyAvailable ? 1d : 0.4d;
    public bool IsHiddenIndicatorVisible => IsEditing && !IsUserEnabled;
    public string AutomationHelpText => IsEditing && !IsUserEnabled
        ? "Hidden from Quick Access. Activate to show."
        : !IsEditing && !IsOperationallyAvailable
            ? $"{Description} Currently unavailable."
            : Description;

    private void NotifyPresentationChanged()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsActionEnabled));
        OnPropertyChanged(nameof(PresentationOpacity));
        OnPropertyChanged(nameof(IsHiddenIndicatorVisible));
        OnPropertyChanged(nameof(AutomationHelpText));
    }
}
