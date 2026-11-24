using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Pereulki.Launcher.Controls;

public sealed class Wordmark : FrameworkElement
{
    private static readonly Geometry Letters = CreateLetters();
    private static readonly Rect LetterBounds = Letters.Bounds;
    private static readonly Brush Face = CreateBrush(Color.FromArgb(179, 255, 250, 240));
    private static readonly Pen Outline = CreateOutline();

    public Wordmark()
    {
        Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            Direction = 270,
            ShadowDepth = 2,
            BlurRadius = 7,
            Opacity = .56,
            RenderingBias = RenderingBias.Performance
        };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var scale = Math.Min((ActualWidth - 2) / LetterBounds.Width, (ActualHeight - 2) / LetterBounds.Height);
        if (scale <= 0)
            return;

        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale,
            1 - LetterBounds.Left * scale, (ActualHeight - LetterBounds.Height * scale) / 2 - LetterBounds.Top * scale));
        drawingContext.DrawGeometry(Face, Outline, Letters);
        drawingContext.Pop();
    }

    private static Geometry CreateLetters()
    {
        var typeface = new Typeface(new FontFamily("Arial Black"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText("ПЕРЕУЛКИ", CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight,
            typeface, 64, Brushes.White, 1);
        var geometry = text.BuildGeometry(new Point());
        geometry.Freeze();
        return geometry;
    }

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateOutline()
    {
        var pen = new Pen(CreateBrush(Color.FromArgb(120, 45, 37, 44)), .8);
        pen.Freeze();
        return pen;
    }
}
