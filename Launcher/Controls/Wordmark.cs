using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Pereulki.Launcher.Controls;

public sealed class Wordmark : FrameworkElement
{
    private const int DepthSlices = 12;
    private const double DepthX = 7;
    private const double DepthY = 9;
    private static readonly DrawingGroup Artwork = CreateArtwork();
    private static readonly Rect ArtworkBounds = Artwork.Bounds;

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
        var scale = Math.Min((ActualWidth - 2) / ArtworkBounds.Width, (ActualHeight - 2) / ArtworkBounds.Height);
        if (scale <= 0)
            return;

        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale,
            1 - ArtworkBounds.Left * scale, (ActualHeight - ArtworkBounds.Height * scale) / 2 - ArtworkBounds.Top * scale));
        drawingContext.DrawDrawing(Artwork);
        drawingContext.Pop();
    }

    private static DrawingGroup CreateArtwork()
    {
        var artwork = new DrawingGroup { Opacity = .70 };
        var letters = CreateLetters();
        var face = new LinearGradientBrush
        {
            StartPoint = new Point(.19, 0),
            EndPoint = new Point(.61, 1),
            GradientStops =
            [
                new(Color.FromRgb(255, 249, 235), 0),
                new(Color.FromRgb(246, 238, 231), .5),
                new(Color.FromRgb(215, 202, 212), 1)
            ]
        };
        var edge = new Pen(new SolidColorBrush(Color.FromRgb(245, 231, 227)), .65);

        using (var drawing = artwork.Open())
        {
            for (int slice = DepthSlices; slice >= 1; slice--)
            {
                double distance = (double)slice / DepthSlices;
                var side = new SolidColorBrush(Color.FromRgb(
                    (byte)(178 - 62 * distance),
                    (byte)(157 - 58 * distance),
                    (byte)(165 - 51 * distance)));

                drawing.PushTransform(new TranslateTransform(DepthX * distance, DepthY * distance));
                drawing.DrawGeometry(side, null, letters);
                drawing.Pop();
            }

            drawing.DrawGeometry(face, edge, letters);
        }

        artwork.Freeze();
        return artwork;
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

}
