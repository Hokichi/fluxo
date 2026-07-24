using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Fluxo.Resources.CustomControls;

[TemplatePart(Name = PartShape, Type = typeof(Path))]
[TemplatePart(Name = PartOverlay, Type = typeof(Path))]
[TemplatePart(Name = PartIconSlot, Type = typeof(FrameworkElement))]
[TemplatePart(Name = PartIcon, Type = typeof(Path))]
[TemplatePart(Name = PartTextReveal, Type = typeof(Border))]
[TemplatePart(Name = PartButtonText, Type = typeof(TextBlock))]
[TemplatePart(Name = PartSubText, Type = typeof(TextBlock))]
public class BalloonControl : ButtonBase
{
    private const string PartShape = "PART_Shape";
    private const string PartOverlay = "PART_Overlay";
    private const string PartIconSlot = "PART_IconSlot";
    private const string PartIcon = "PART_Icon";
    private const string PartTextReveal = "PART_TextReveal";
    private const string PartButtonText = "PART_ButtonText";
    private const string PartSubText = "PART_SubText";
    private static readonly Thickness ButtonTextMargin = new(24, 0, 8, 0);
    private static readonly Thickness SubTextMargin = new(8, 0, 0, 0);

    private static readonly Duration ExpandDuration = new(TimeSpan.FromSeconds(0.18));
    private static readonly Duration CollapseDuration = new(TimeSpan.FromSeconds(0.16));
    private static readonly Duration TextFadeDuration = new(TimeSpan.FromSeconds(0.12));

    // --- DefaultBackground ---
    public static readonly DependencyProperty DefaultBackgroundProperty =
        DependencyProperty.Register(nameof(DefaultBackground), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(Brushes.CornflowerBlue, OnDefaultBackgroundChanged));

    // --- HoveredBackground ---
    public static readonly DependencyProperty HoveredBackgroundProperty =
        DependencyProperty.Register(nameof(HoveredBackground), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(Brushes.RoyalBlue));

    // --- StrokeBrush ---
    public static readonly DependencyProperty StrokeBrushProperty =
        DependencyProperty.Register(nameof(StrokeBrush), typeof(Brush), typeof(BalloonControl),
            new PropertyMetadata(null));

    // --- StrokeThickness ---
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(BalloonControl),
            new PropertyMetadata(0d));

    // --- ButtonIcon ---
    public static readonly DependencyProperty ButtonIconProperty =
        DependencyProperty.Register(nameof(ButtonIcon), typeof(object), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    // --- ButtonText ---
    public static readonly DependencyProperty ButtonTextProperty =
        DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    // --- SubText ---
    public static readonly DependencyProperty SubTextProperty =
        DependencyProperty.Register(nameof(SubText), typeof(string), typeof(BalloonControl),
            new PropertyMetadata(null, OnPresentationPropertyChanged));

    // --- ShouldExpand ---
    public static readonly DependencyProperty ShouldExpandProperty =
        DependencyProperty.Register(nameof(ShouldExpand), typeof(bool), typeof(BalloonControl),
            new FrameworkPropertyMetadata(false, null, CoerceShouldExpand));

    // --- ShouldShowText ---
    public static readonly DependencyProperty ShouldShowTextProperty =
        DependencyProperty.Register(nameof(ShouldShowText), typeof(bool), typeof(BalloonControl),
            new PropertyMetadata(false, OnShouldShowTextChanged));

    // --- ButtonSize ---
    public static readonly DependencyProperty ButtonSizeProperty =
        DependencyProperty.Register(nameof(ButtonSize), typeof(double), typeof(BalloonControl),
            new PropertyMetadata(30.0, OnLayoutPropertyChanged));

    // --- CurveHeight ---
    public static readonly DependencyProperty CurveHeightProperty =
        DependencyProperty.Register(nameof(CurveHeight), typeof(double), typeof(BalloonControl),
            new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    // --- CornerRadius ---
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(BalloonControl),
            new FrameworkPropertyMetadata(15.0, FrameworkPropertyMetadataOptions.AffectsRender, OnGeometryInvalidated));

    // --- IconSize ---
    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(BalloonControl),
            new PropertyMetadata(12.0, OnLayoutPropertyChanged));

    private Path? _icon;
    private FrameworkElement? _iconSlot;
    private Path? _overlay;

    private Path? _shape;
    private TextBlock? _buttonHotkeys;
    private TextBlock? _buttonText;
    private Border? _textReveal;
    private double? _explicitWidth;
    private bool _isApplyingWidth;

    static BalloonControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BalloonControl),
            new FrameworkPropertyMetadata(typeof(BalloonControl)));
    }

    public Brush DefaultBackground
    {
        get => (Brush)GetValue(DefaultBackgroundProperty);
        set => SetValue(DefaultBackgroundProperty, value);
    }

    public Brush HoveredBackground
    {
        get => (Brush)GetValue(HoveredBackgroundProperty);
        set => SetValue(HoveredBackgroundProperty, value);
    }

    public Brush? StrokeBrush
    {
        get => (Brush?)GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public object? ButtonIcon
    {
        get => GetValue(ButtonIconProperty);
        set => SetValue(ButtonIconProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? ButtonText
    {
        get => (string?)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public string? SubText
    {
        get => (string?)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public bool ShouldExpand
    {
        get => (bool)GetValue(ShouldExpandProperty);
        set => SetValue(ShouldExpandProperty, value);
    }

    public bool ShouldShowText
    {
        get => (bool)GetValue(ShouldShowTextProperty);
        set => SetValue(ShouldShowTextProperty, value);
    }

    public double ButtonSize
    {
        get => (double)GetValue(ButtonSizeProperty);
        set => SetValue(ButtonSizeProperty, value);
    }

    public double CurveHeight
    {
        get => (double)GetValue(CurveHeightProperty);
        set => SetValue(CurveHeightProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    // -------------------------------------------------------

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shape = GetTemplateChild(PartShape) as Path;
        _overlay = GetTemplateChild(PartOverlay) as Path;
        _iconSlot = GetTemplateChild(PartIconSlot) as FrameworkElement;
        _icon = GetTemplateChild(PartIcon) as Path;
        _textReveal = GetTemplateChild(PartTextReveal) as Border;
        _buttonText = GetTemplateChild(PartButtonText) as TextBlock;
        _buttonHotkeys = GetTemplateChild(PartSubText) as TextBlock;
        RefreshPresentation();
        RebuildGeometry();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        SetShapeFill(ResolveHoveredBackground());
        AnimateExpansion(isExpanded: true);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetShapeFill(ResolveRestingBackground());
        AnimateExpansion(isExpanded: false);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        RebuildGeometry();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == PaddingProperty)
            ResetExpansion();
        else if (e.Property == WidthProperty && !_isApplyingWidth)
            _explicitWidth = ReadLocalValue(WidthProperty) == DependencyProperty.UnsetValue ||
                double.IsNaN((double)e.NewValue)
                    ? null
                    : (double)e.NewValue;
    }

    private static void OnGeometryInvalidated(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RebuildGeometry();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).ResetExpansion();
    }

    private static void OnShouldShowTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var button = (BalloonControl)d;
        button.CoerceValue(ShouldExpandProperty);
        button.ResetExpansion();
    }

    private static object CoerceShouldExpand(DependencyObject d, object baseValue)
    {
        return ((BalloonControl)d).ShouldShowText ? false : baseValue;
    }

    private static void OnDefaultBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RefreshPresentation();
    }

    private static void OnPresentationPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BalloonControl)d).RefreshPresentation();
    }

    protected virtual Brush ResolveRestingBackground() => DefaultBackground;

    protected virtual Brush ResolveHoveredBackground() => HoveredBackground;

    protected virtual object? ResolveButtonIcon() => Icon ?? ButtonIcon;

    protected virtual string? ResolveButtonText() => ButtonText;

    protected void RefreshPresentation()
    {
        ResetShapeFill();
        ApplyIcon();
        ApplyText();
        ResetExpansion();
    }

    // Snaps the fill back to DefaultBackground (no animation); called on template apply or DP change.
    private void ResetShapeFill()
    {
        SetShapeFill(ResolveRestingBackground());
    }

    private void SetShapeFill(Brush background)
    {
        if (_shape is not null)
            _shape.Fill = background;
    }

    private void ResetExpansion()
    {
        BeginAnimation(WidthProperty, null);
        ApplyIconSlotLayout(ShouldShowText);
        var openWidth = GetEffectiveOpenWidth();
        SetCurrentWidth(ShouldShowText
            ? _explicitWidth ?? openWidth
            : ButtonSize);
        MinWidth = ShouldShowText ? openWidth : ButtonSize;

        if (_textReveal is not null)
        {
            _textReveal.BeginAnimation(WidthProperty, null);
            _textReveal.BeginAnimation(OpacityProperty, null);
            _textReveal.Width = ShouldShowText ? GetTextRevealWidth(Width) : 0;
            _textReveal.Opacity = ShouldShowText ? 1 : 0;
        }

        if (_buttonText is not null)
        {
            _buttonText.BeginAnimation(OpacityProperty, null);
            _buttonText.Opacity = ShouldShowText ? 1 : 0;
        }

        if (_buttonHotkeys is not null)
        {
            _buttonHotkeys.BeginAnimation(OpacityProperty, null);
            _buttonHotkeys.Opacity = ShouldShowText ? 0.5 : 0;
        }
    }

    private void AnimateExpansion(bool isExpanded)
    {
        if (!ShouldExpand)
            return;

        ApplyIconSlotLayout(isExpanded);
        var openWidth = GetEffectiveOpenWidth();
        var widthTarget = isExpanded ? openWidth : ButtonSize;
        var textWidthTarget = isExpanded ? GetTextRevealWidth(openWidth) : 0;
        var opacityTarget = isExpanded ? 1 : 0;
        var duration = isExpanded ? ExpandDuration : CollapseDuration;
        var easingMode = isExpanded ? EasingMode.EaseOut : EasingMode.EaseIn;
        var easing = new CubicEase { EasingMode = easingMode };

        if (double.IsNaN(Width))
            SetCurrentWidth(ButtonSize);

        BeginAnimation(WidthProperty, new DoubleAnimation(widthTarget, duration)
        {
            EasingFunction = easing
        });

        if (_textReveal is not null)
        {
            _textReveal.BeginAnimation(WidthProperty, new DoubleAnimation(textWidthTarget, duration)
            {
                EasingFunction = easing
            });
            _textReveal.BeginAnimation(OpacityProperty, new DoubleAnimation(opacityTarget, TextFadeDuration));
        }

        if (_buttonText is not null)
            _buttonText.BeginAnimation(OpacityProperty, new DoubleAnimation(opacityTarget, TextFadeDuration));

        if (_buttonHotkeys is not null)
            _buttonHotkeys.BeginAnimation(OpacityProperty, new DoubleAnimation(isExpanded ? 0.5 : 0, TextFadeDuration));
    }

    internal double GetEffectiveOpenWidth()
    {
        var autoWidth = CalculateAutoOpenWidth(
            ButtonSize,
            IconSize,
            Padding,
            MeasureButtonTextWidth(),
            MeasureSubTextWidth());

        return ResolveOpenWidth(ButtonSize, autoWidth);
    }

    internal static double ResolveOpenWidth(double buttonSize, double autoWidth)
    {
        return Math.Max(buttonSize, autoWidth);
    }

    internal static double CalculateAutoOpenWidth(
        double buttonSize,
        double iconSize,
        Thickness padding,
        double textWidth,
        Thickness textMargin)
    {
        var contentWidth = ResolveOpenIconSlotWidth(buttonSize, iconSize, padding) +
            textMargin.Left +
            textWidth +
            textMargin.Right;
        var totalWidth = padding.Left + contentWidth + padding.Right;
        return Math.Max(buttonSize, Math.Ceiling(totalWidth));
    }

    internal static double CalculateAutoOpenWidth(
        double buttonSize,
        double iconSize,
        Thickness padding,
        double textWidth,
        double hotkeysWidth)
    {
        var contentWidth = ResolveOpenIconSlotWidth(buttonSize, iconSize, padding);
        if (textWidth > 0)
            contentWidth += ButtonTextMargin.Left + textWidth + ButtonTextMargin.Right;
        if (hotkeysWidth > 0)
            contentWidth += SubTextMargin.Left + hotkeysWidth + SubTextMargin.Right;

        var totalWidth = padding.Left + contentWidth + padding.Right;
        return Math.Max(buttonSize, Math.Ceiling(totalWidth));
    }

    internal static double ResolveOpenIconSlotWidth(double buttonSize, double iconSize, Thickness padding)
    {
        return Math.Max(iconSize, buttonSize - padding.Left - padding.Right);
    }

    private double GetTextRevealWidth(double width)
    {
        _ = width;
        var widthTarget = ButtonTextMargin.Left + MeasureButtonTextWidth() + ButtonTextMargin.Right;
        var hotkeysWidth = MeasureSubTextWidth();
        if (hotkeysWidth > 0)
            widthTarget += SubTextMargin.Left + hotkeysWidth + SubTextMargin.Right;
        return widthTarget;
    }

    private void ApplyIconSlotLayout(bool useExpandedSlot)
    {
        if (_iconSlot is null)
            return;

        _iconSlot.Width = useExpandedSlot ? GetOpenIconSlotWidth() : IconSize;
    }

    private double GetOpenIconSlotWidth()
    {
        return ResolveOpenIconSlotWidth(ButtonSize, IconSize, Padding);
    }

    private double MeasureButtonTextWidth()
    {
        var buttonText = ResolveButtonText();
        if (string.IsNullOrEmpty(buttonText))
            return 0;

        return MeasureText(buttonText, _buttonText, FontFamily, FontSize, FontStretch, FontStyle, FontWeight);
    }

    private double MeasureSubTextWidth()
    {
        if (string.IsNullOrEmpty(SubText))
            return 0;

        return MeasureText(SubText, _buttonHotkeys, FontFamily, 10, FontStretch, FontStyle, FontWeight);
    }

    private void ApplyIcon()
    {
        if (_icon is not null)
            _icon.Data = ResolveGeometry(ResolveButtonIcon());
    }

    private void ApplyText()
    {
        if (_buttonText is not null)
            _buttonText.Text = ResolveButtonText();
        if (_buttonHotkeys is not null)
        {
            _buttonHotkeys.Text = SubText;
            _buttonHotkeys.Visibility = string.IsNullOrEmpty(SubText) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void SetCurrentWidth(double width)
    {
        _isApplyingWidth = true;
        if (ReadLocalValue(WidthProperty) == DependencyProperty.UnsetValue)
            SetValue(WidthProperty, width);
        else
            SetCurrentValue(WidthProperty, width);
        _isApplyingWidth = false;
    }

    private static double MeasureText(
        string text,
        TextBlock? textBlock,
        FontFamily fontFamily,
        double fontSize,
        FontStretch fontStretch,
        FontStyle fontStyle,
        FontWeight fontWeight)
    {
        if (textBlock is not null)
        {
            textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return textBlock.DesiredSize.Width;
        }

        var measuredText = new TextBlock
        {
            Text = text,
            FontFamily = fontFamily,
            FontSize = fontSize,
            FontStretch = fontStretch,
            FontStyle = fontStyle,
            FontWeight = fontWeight
        };

        measuredText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return measuredText.DesiredSize.Width;
    }

    private static Geometry? ResolveGeometry(object? source)
    {
        switch (source)
        {
            case Geometry geometry:
                return geometry;
            case GeometryDrawing geometryDrawing:
                return geometryDrawing.Geometry;
            case DrawingGroup drawingGroup:
                foreach (var drawing in drawingGroup.Children)
                {
                    var resolved = ResolveGeometry(drawing);
                    if (resolved is not null)
                        return resolved;
                }

                break;
        }

        return null;
    }

    private void RebuildGeometry()
    {
        if (_shape == null || _overlay == null) return;

        var w = ActualWidth;
        var h = ActualHeight;
        var c = CurveHeight;
        var r = CornerRadius;

        if (w <= 0 || h <= 0) return;

        var figure = new PathFigure { StartPoint = new Point(r, 0), IsClosed = true };

        figure.Segments.Add(new BezierSegment(new Point(w * 0.25, -c), new Point(w * 0.75, -c), new Point(w - r, 0),
            true));
        figure.Segments.Add(new ArcSegment(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new BezierSegment(new Point(w + c, h * 0.25), new Point(w + c, h * 0.75),
            new Point(w, h - r), true));
        figure.Segments.Add(new ArcSegment(new Point(w - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new BezierSegment(new Point(w * 0.75, h + c), new Point(w * 0.25, h + c), new Point(r, h),
            true));
        figure.Segments.Add(new ArcSegment(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new BezierSegment(new Point(-c, h * 0.75), new Point(-c, h * 0.25), new Point(0, r), true));
        figure.Segments.Add(new ArcSegment(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry(new[] { figure });
        _shape.Data = geometry;
        _overlay.Data = geometry;
    }
}
