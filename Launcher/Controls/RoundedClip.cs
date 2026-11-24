using System.Windows;
using System.Windows.Media;

namespace Pereulki.Launcher.Controls;

public static class RoundedClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius", typeof(double), typeof(RoundedClip), new PropertyMetadata(0d, RadiusChanged),
        value => value is double radius && double.IsFinite(radius) && radius >= 0);

    public static double GetRadius(DependencyObject element) => (double)element.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject element, double value) => element.SetValue(RadiusProperty, value);

    private static void RadiusChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element)
            return;

        element.SizeChanged -= ElementSizeChanged;
        if (GetRadius(element) > 0)
            element.SizeChanged += ElementSizeChanged;

        RefreshClip(element);
    }

    private static void ElementSizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshClip((FrameworkElement)sender);

    private static void RefreshClip(FrameworkElement element)
    {
        var radius = GetRadius(element);
        if (radius == 0)
        {
            element.ClearValue(UIElement.ClipProperty);
            return;
        }

        var clip = new RectangleGeometry(new Rect(element.RenderSize), radius, radius);
        clip.Freeze();
        element.SetCurrentValue(UIElement.ClipProperty, clip);
    }
}
