using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluxo.Resources.Components;

public partial class TimeSelector : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty SelectedTimeProperty =
        DependencyProperty.Register(
            nameof(SelectedTime),
            typeof(TimeSpan),
            typeof(TimeSelector),
            new FrameworkPropertyMetadata(
                TimeSpan.Zero,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedTimeChanged));

    private string _segmentInput = string.Empty;
    private TimeSelectorSegment _activeSegment = TimeSelectorSegment.Hours;

    public TimeSelector()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdatePresentation();
    }

    public TimeSpan SelectedTime
    {
        get => (TimeSpan)GetValue(SelectedTimeProperty);
        set => SetValue(SelectedTimeProperty, value);
    }

    public TimeSelectorSegment ActiveSegment
    {
        get => _activeSegment;
        private set
        {
            if (_activeSegment == value)
                return;

            _activeSegment = value;
            _segmentInput = string.Empty;
            OnPropertyChanged();
            UpdatePresentation();
        }
    }

    public string FormattedSelectedTime => SelectedTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    public string FormattedHours => SelectedTime.Hours.ToString("00", CultureInfo.InvariantCulture);
    public string FormattedMinutes => SelectedTime.Minutes.ToString("00", CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void ProcessTextForTests(string text) => ProcessText(text);
    internal void ProcessKeyForTests(Key key) => ProcessKey(key);

    private static void OnSelectedTimeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TimeSelector selector)
        {
            selector.OnPropertyChanged(nameof(FormattedSelectedTime));
            selector.OnPropertyChanged(nameof(FormattedHours));
            selector.OnPropertyChanged(nameof(FormattedMinutes));
            selector.UpdatePresentation();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ProcessKey(e.Key))
            return;

        e.Handled = true;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!e.Text.All(char.IsAsciiDigit))
        {
            e.Handled = true;
            return;
        }

        ProcessText(e.Text);
        e.Handled = true;
    }

    private void OnHoursSegmentMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        ActiveSegment = TimeSelectorSegment.Hours;
        e.Handled = true;
    }

    private void OnMinutesSegmentMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        ActiveSegment = TimeSelectorSegment.Minutes;
        e.Handled = true;
    }

    private bool ProcessKey(Key key)
    {
        switch (key)
        {
            case Key.Left:
                ActiveSegment = TimeSelectorSegment.Hours;
                return true;
            case Key.Right:
                ActiveSegment = TimeSelectorSegment.Minutes;
                return true;
            case Key.Up:
                AdjustActiveSegment(1);
                return true;
            case Key.Down:
                AdjustActiveSegment(-1);
                return true;
            default:
                return false;
        }
    }

    private void ProcessText(string text)
    {
        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
                continue;

            _segmentInput += character;
            if (_segmentInput.Length < 2)
                continue;

            ApplySegmentInput(int.Parse(_segmentInput, CultureInfo.InvariantCulture));
            _segmentInput = string.Empty;
            if (ActiveSegment == TimeSelectorSegment.Hours)
                ActiveSegment = TimeSelectorSegment.Minutes;
        }
    }

    private void ApplySegmentInput(int value)
    {
        var timestamp = DateTime.Today.Add(SelectedTime);
        SelectedTime = ActiveSegment == TimeSelectorSegment.Hours
            ? timestamp.Date.AddHours(value).TimeOfDay
            : timestamp.AddMinutes(value).TimeOfDay;
    }

    private void AdjustActiveSegment(int amount)
    {
        var timestamp = DateTime.Today.Add(SelectedTime);
        SelectedTime = ActiveSegment == TimeSelectorSegment.Hours
            ? timestamp.AddHours(amount).TimeOfDay
            : timestamp.AddMinutes(amount).TimeOfDay;
    }

    private void UpdatePresentation()
    {
        if (HoursSegment is null || MinutesSegment is null)
            return;

        var selectedBackground = TryFindResource("Brush.Background.Hover") as Brush ?? Brushes.Transparent;
        var selectedBorder = TryFindResource("Brush.Border.Focus") as Brush ?? Brushes.Transparent;
        HoursSegment.Background = ActiveSegment == TimeSelectorSegment.Hours ? selectedBackground : Brushes.Transparent;
        HoursSegment.BorderBrush = ActiveSegment == TimeSelectorSegment.Hours ? selectedBorder : Brushes.Transparent;
        MinutesSegment.Background = ActiveSegment == TimeSelectorSegment.Minutes ? selectedBackground : Brushes.Transparent;
        MinutesSegment.BorderBrush = ActiveSegment == TimeSelectorSegment.Minutes ? selectedBorder : Brushes.Transparent;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
