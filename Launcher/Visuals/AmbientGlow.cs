using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pereulki.Launcher.Visuals;

public sealed class AmbientGlow : Grid
{
    private const double RiseSeconds = 1.3;
    private const double SpreadSeconds = 3;
    private const double FadeSeconds = 2.1;
    private const double RestTime = RiseSeconds + SpreadSeconds + FadeSeconds;
    private static readonly Color DefaultColor = Color.FromRgb(76, 99, 242);
    private static readonly Color AccentColor = Color.FromRgb(154, 71, 219);

    private readonly ScaleTransform _bottomReach = new(1, 1);
    private readonly Border _bottom;
    private readonly Border _accent;
    private readonly Border _left;
    private readonly Border _right;
    private TimeSpan? _lastRenderTime;
    private double _elapsed = RestTime;
    private double _pulseTime;
    private double _intensity = 0.75;
    private GlowDirection _direction;
    private Color _fromColor = DefaultColor;
    private Color _color = DefaultColor;
    private Color _targetColor = DefaultColor;
    private Color? _appliedColor;
    private LightPose _startPose = RestPose(0.75);
    private LightPose _pose = RestPose(0.75);
    private bool _active;
    private bool _animate = true;
    private bool _highQuality = true;
    private bool _subscribed;

    public AmbientGlow()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;

        // Overlapping washes stay anchored beyond the edges. The light travels
        // as their relative contributions change across the window.
        _bottom = AddWash(new Point(0.50, 1.12), 0.81, 0.64);
        _bottom.RenderTransformOrigin = new Point(0.5, 1);
        _bottom.RenderTransform = _bottomReach;
        _accent = AddWash(new Point(0.58, 1.21), 0.92, 0.74);
        _left = AddWash(new Point(-0.15, 0.70), 0.72, 0.86);
        _right = AddWash(new Point(1.15, 0.70), 0.72, 0.86);

        ApplyPose();
        Loaded += (_, _) => UpdateRenderingSubscription();
        Unloaded += (_, _) => UpdateRenderingSubscription();
        IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
    }

    public void ApplyFrame(GalleryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _intensity = frame.Intensity;
        _direction = frame.Direction;
        _fromColor = _color;
        _targetColor = frame.Color;
        _startPose = _pose;
        _elapsed = 0;

        if (!_animate)
        {
            ApplyStaticPose();
        }
    }

    public void SetPlayback(bool active, bool animationsEnabled, bool highQuality)
    {
        _active = active;
        _animate = animationsEnabled;
        _highQuality = highQuality;
        _accent.Visibility = highQuality ? Visibility.Visible : Visibility.Collapsed;

        if (_animate)
        {
            ApplyPose();
        }
        else
        {
            ApplyStaticPose();
        }

        UpdateRenderingSubscription();
    }

    private void UpdateRenderingSubscription()
    {
        bool shouldRender = IsLoaded && _active && _animate && IsVisible;
        if (shouldRender == _subscribed)
        {
            return;
        }

        _subscribed = shouldRender;
        _lastRenderTime = null;
        if (_subscribed)
        {
            CompositionTarget.Rendering += Render;
        }
        else
        {
            CompositionTarget.Rendering -= Render;
        }
    }

    private void Render(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs rendering)
        {
            return;
        }

        double elapsed = _lastRenderTime is { } last
            ? (rendering.RenderingTime - last).TotalSeconds
            : 0;
        if (!_highQuality && elapsed is > 0 and < 0.028)
        {
            return;
        }

        _lastRenderTime = rendering.RenderingTime;
        if (elapsed <= 0)
        {
            return;
        }

        _pulseTime += elapsed;
        _elapsed = Math.Min(RestTime, _elapsed + elapsed);
        _pose = SamplePose(_elapsed);
        _color = Mix(_fromColor, _targetColor, Ease(_elapsed / (RiseSeconds + 0.45)));
        ApplyPose();
    }

    private LightPose SamplePose(double elapsed)
    {
        double strength = _intensity;
        var peak = new LightPose(strength, 0.04 * strength, 0.04 * strength, 1.23);
        if (elapsed <= RiseSeconds)
        {
            return Interpolate(_startPose, peak, Ease(elapsed / RiseSeconds));
        }

        double edge = strength * 0.94;
        var side = _direction == GlowDirection.Left
            ? new LightPose(strength * 0.38, edge, 0.025 * strength, 1.12)
            : new LightPose(strength * 0.38, 0.025 * strength, edge, 1.12);
        double spreadingTime = elapsed - RiseSeconds;
        if (spreadingTime <= SpreadSeconds)
        {
            return Interpolate(peak, side, Ease(spreadingTime / SpreadSeconds));
        }

        return Interpolate(side, RestPose(_intensity), Ease((spreadingTime - SpreadSeconds) / FadeSeconds));
    }

    private void ApplyStaticPose()
    {
        _elapsed = RestTime;
        _pose = RestPose(_intensity);
        _color = _targetColor;
        ApplyPose();
    }

    private void ApplyPose()
    {
        if (_appliedColor != _color)
        {
            Tint(_bottom, _color);
            Tint(_left, _color);
            Tint(_right, _color);
            Tint(_accent, Mix(_color, AccentColor, 0.58));
            _appliedColor = _color;
        }

        double pulse = _animate ? 0.96 + 0.04 * Math.Sin(_pulseTime * 1.38) : 0.96;
        _bottom.Opacity = _pose.Bottom * pulse;
        _accent.Opacity = _pose.Bottom * (0.43 + 0.06 * Math.Cos(_pulseTime * 0.92));
        _left.Opacity = _pose.Left * pulse;
        _right.Opacity = _pose.Right * pulse;
        _bottomReach.ScaleY = _pose.Reach;
    }

    private Border AddWash(Point origin, double radiusX, double radiusY)
    {
        var wash = new Border
        {
            Background = new RadialGradientBrush
            {
                Center = origin,
                GradientOrigin = origin,
                RadiusX = radiusX,
                RadiusY = radiusY,
                GradientStops =
                [
                    new(Color.FromArgb(168, 0, 0, 0), 0),
                    new(Color.FromArgb(104, 0, 0, 0), 0.24),
                    new(Color.FromArgb(46, 0, 0, 0), 0.48),
                    new(Color.FromArgb(11, 0, 0, 0), 0.72),
                    new(Color.FromArgb(0, 0, 0, 0), 1)
                ]
            }
        };
        Children.Add(wash);
        return wash;
    }

    private static void Tint(Border wash, Color tint)
    {
        var gradient = (RadialGradientBrush)wash.Background;
        foreach (GradientStop stop in gradient.GradientStops)
        {
            stop.Color = Color.FromArgb(stop.Color.A, tint.R, tint.G, tint.B);
        }
    }

    private static LightPose RestPose(double intensity) => new(0.76 * intensity, 0, 0, 1);

    private static double Ease(double value)
    {
        double t = Math.Clamp(value, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Interpolate(double from, double to, double amount) => from + (to - from) * amount;

    private static LightPose Interpolate(LightPose from, LightPose to, double amount) => new(
        Interpolate(from.Bottom, to.Bottom, amount),
        Interpolate(from.Left, to.Left, amount),
        Interpolate(from.Right, to.Right, amount),
        Interpolate(from.Reach, to.Reach, amount));

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(Interpolate(from.R, to.R, amount)),
        (byte)Math.Round(Interpolate(from.G, to.G, amount)),
        (byte)Math.Round(Interpolate(from.B, to.B, amount)));

    private readonly record struct LightPose(double Bottom, double Left, double Right, double Reach);
}
