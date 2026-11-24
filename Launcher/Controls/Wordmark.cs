using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Pereulki.Launcher.Controls;

public sealed class Wordmark : FrameworkElement
{
    private const string Title = "ПЕРЕУЛКИ";
    private readonly DrawingVisual artwork = new();
    private readonly DrawingVisual highlight = new();
    private readonly DrawingGroup lightArtwork = new();
    private readonly double artworkWidth;
    private readonly double artworkHeight;

    public Wordmark()
    {
        AddVisualChild(artwork);
        AddVisualChild(highlight);
        var face = new Typeface(new FontFamily("Arial Black"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var letters = new GeometryGroup();
        double nextX = 0;
        foreach (char letter in Title)
        {
            var text = new FormattedText(letter.ToString(), CultureInfo.GetCultureInfo("ru-RU"),
                FlowDirection.LeftToRight, face, 129, Brushes.White, 1);
            letters.Children.Add(text.BuildGeometry(new Point(nextX, 0)));
            nextX += text.Width - 0.6;
        }

        var bounds = letters.Bounds;
        letters.Transform = new TranslateTransform(6 - bounds.Left, 8 - bounds.Top);
        letters.Freeze();
        artworkWidth = bounds.Width + 29;
        artworkHeight = bounds.Height + 36;

        artwork.Effect = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 22,
            Direction = 268,
            ShadowDepth = 12,
            Opacity = .85,
            RenderingBias = RenderingBias.Performance
        };
        using (var drawing = artwork.RenderOpen())
        {
            for (int depth = 18; depth >= 1; depth--)
            {
                drawing.PushTransform(new TranslateTransform(depth * .50, depth * .83));
                byte tone = (byte)(38 + (18 - depth) * 3.8);
                var side = new SolidColorBrush(Color.FromRgb(tone, (byte)(tone - 2), (byte)(tone + 4)));
                side.Freeze();
                drawing.DrawGeometry(side, null, letters);
                drawing.Pop();
            }

            var surface = new LinearGradientBrush
            {
                StartPoint = new Point(.18, 0),
                EndPoint = new Point(.53, 1),
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromRgb(255, 248, 236), 0),
                    new(Color.FromRgb(243, 237, 232), .35),
                    new(Color.FromRgb(225, 216, 222), .77),
                    new(Color.FromRgb(194, 186, 199), 1)
                }
            };
            surface.Freeze();
            var edge = new Pen(new SolidColorBrush(Color.FromRgb(248, 241, 234)), .55);
            edge.Freeze();
            drawing.DrawGeometry(surface, edge, letters);
        }

        using (var drawing = lightArtwork.Open())
        {
            var light = new RadialGradientBrush
            {
                Center = new Point(.18, 0),
                GradientOrigin = new Point(.18, 0),
                RadiusX = .60,
                RadiusY = .86,
                GradientStops = new GradientStopCollection
                {
                    new(Color.FromArgb(165, 255, 213, 170), 0),
                    new(Color.FromArgb(0, 255, 213, 170), 1)
                }
            };
            light.Freeze();
            drawing.DrawGeometry(light, null, letters);
        }
        using (var drawing = highlight.RenderOpen())
            drawing.DrawDrawing(lightArtwork);
        SetRestingHighlight();
    }

    protected override int VisualChildrenCount => 2;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => artwork,
        1 => highlight,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    protected override Size ArrangeOverride(Size size)
    {
        double scale = Math.Min(size.Width / artworkWidth, size.Height / artworkHeight);
        var transform = new MatrixTransform(scale, 0, 0, scale,
            (size.Width - artworkWidth * scale) / 2, (size.Height - artworkHeight * scale) / 2);
        transform.Freeze();
        artwork.Transform = transform;
        highlight.Transform = transform;
        return size;
    }

    public void SetPlayback(bool active)
    {
        if (!active)
        {
            SetRestingHighlight();
            return;
        }

        var pulse = new DoubleAnimation(.20, .56, TimeSpan.FromSeconds(5.4))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        lightArtwork.BeginAnimation(DrawingGroup.OpacityProperty, pulse);
    }

    private void SetRestingHighlight()
    {
        lightArtwork.BeginAnimation(DrawingGroup.OpacityProperty, null);
        lightArtwork.Opacity = .20;
    }
}
