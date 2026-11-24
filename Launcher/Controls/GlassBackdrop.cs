using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Pereulki.Launcher.Controls;

public sealed class GlassBackdrop : FrameworkElement
{
    private const double BlurRadius = 9;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(FrameworkElement), typeof(GlassBackdrop),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnSourceChanged));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(GlassBackdrop),
        new FrameworkPropertyMetadata(10d, OnRadiusChanged));

    private readonly VisualBrush sample = new()
    {
        ViewboxUnits = BrushMappingMode.Absolute,
        Stretch = Stretch.Fill
    };
    private readonly DrawingVisual blurLayer = new();

    public GlassBackdrop()
    {
        var blur = new BlurEffect { Radius = BlurRadius, RenderingBias = RenderingBias.Performance };
        blur.Freeze();
        blurLayer.Effect = blur;
        AddVisualChild(blurLayer);
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            LayoutUpdated += UpdateSampleAfterLayout;
            UpdateSample();
        };
        Unloaded += (_, _) =>
        {
            LayoutUpdated -= UpdateSampleAfterLayout;
            sample.Visual = null;
        };
    }

    public static FrameworkElement? GetSource(DependencyObject element) =>
        (FrameworkElement?)element.GetValue(SourceProperty);

    public static void SetSource(DependencyObject element, FrameworkElement? value) =>
        element.SetValue(SourceProperty, value);

    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? blurLayer : throw new ArgumentOutOfRangeException(nameof(index));

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
        using (var drawing = blurLayer.RenderOpen())
            drawing.DrawRectangle(sample, null, GetSampleBounds());
        UpdateSample();
    }

    private void UpdateSampleAfterLayout(object? sender, EventArgs e) => UpdateSample();

    private static void OnSourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is GlassBackdrop backdrop)
            backdrop.UpdateSample();
    }

    private static void OnRadiusChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) =>
        ((GlassBackdrop)element).UpdateClip();

    private void UpdateClip()
    {
        var clip = new RectangleGeometry(new Rect(RenderSize), Radius, Radius);
        clip.Freeze();
        Clip = clip;
    }

    private void UpdateSample()
    {
        var source = GetSource(this);
        if (source is null || !IsLoaded || !source.IsLoaded || source.IsAncestorOf(this) || IsAncestorOf(source) ||
            PresentationSource.FromVisual(this) != PresentationSource.FromVisual(source))
        {
            sample.Visual = null;
            return;
        }

        Rect area = TransformToVisual(source).TransformBounds(GetSampleBounds());
        if (area.Width <= 0 || area.Height <= 0)
            return;

        if (sample.Viewbox != area)
            sample.Viewbox = area;
        if (sample.Visual != source)
            sample.Visual = source;
    }

    private Rect GetSampleBounds()
    {
        var bounds = new Rect(RenderSize);
        bounds.Inflate(BlurRadius, BlurRadius);
        return bounds;
    }
}
