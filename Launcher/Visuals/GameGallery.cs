using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pereulki.Launcher.Visuals;

public sealed class GameGallery : Grid
{
    private const double DisplaySeconds = 7;
    private const double CrossfadeSeconds = 1.75;
    private const double PlaybackTrackWidth = 68;

    private readonly Image[] _images = [CreateImage(), CreateImage()];
    private readonly ScaleTransform _playbackProgress = new(0, 1);
    private readonly Border _playbackTrack;
    private readonly StackPanel _emptyState;
    private readonly HashSet<int> _invalidFrames = [];

    private IReadOnlyList<GalleryFrame> _frames = [];
    private DecodedFrame? _current;
    private DecodedFrame? _incoming;
    private DecodedFrame? _preloaded;
    private int _visibleSlot;
    private double _holdTime;
    private double _fadeTime;
    private double _frameAge;
    private TimeSpan? _lastRenderTime;
    private bool _initialized;
    private bool _subscribed;
    private bool _active;
    private bool _animate = true;
    private bool _highQuality = true;
    private bool _preloading;
    private bool _navigating;

    public GameGallery()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        Background = new SolidColorBrush(Color.FromRgb(9, 11, 16));

        Children.Add(_images[0]);
        Children.Add(_images[1]);
        var shading = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                [
                    new(Color.FromArgb(76, 0, 0, 0), 0),
                    new(Color.FromArgb(0, 0, 0, 0), 0.22),
                    new(Color.FromArgb(0, 0, 0, 0), 0.66),
                    new(Color.FromArgb(168, 0, 0, 0), 1)
                ]
            }
        };
        SetZIndex(shading, 2);
        Children.Add(shading);

        _playbackTrack = new Border
        {
            Width = PlaybackTrackWidth,
            Height = 2,
            Margin = new Thickness(0, 0, 117, 31),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(74, 221, 228, 244)),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(223, 230, 248)),
                RenderTransform = _playbackProgress
            },
            Visibility = Visibility.Collapsed
        };
        SetZIndex(_playbackTrack, 3);
        Children.Add(_playbackTrack);

        _emptyState = CreateEmptyState();
        SetZIndex(_emptyState, 4);
        Children.Add(_emptyState);

        AutomationProperties.SetName(this, "Галерея игровых кадров");
        Loaded += OnLoaded;
        Unloaded += (_, _) => UpdateRenderingSubscription();
        IsVisibleChanged += (_, _) => UpdateRenderingSubscription();
    }

    public event EventHandler<GalleryFrame>? FrameChanged;

    public int CurrentFrameNumber => (_incoming ?? _current) is { } shown
        ? shown.Index + 1 - _invalidFrames.Count(index => index < shown.Index)
        : 0;

    public int FrameCount => _frames.Count - _invalidFrames.Count;

    public async Task NavigateAsync(int direction)
    {
        if (direction == 0 || _navigating || _current is null || FrameCount < 2)
            return;

        _navigating = true;
        try
        {
            if (_incoming is not null)
                CompleteTransition();

            var currentIndex = _current.Index;
            int step = Math.Sign(direction);
            var next = step > 0 ? _preloaded : null;
            _preloaded = null;
            next ??= await DecodeFromAsync(currentIndex + step, currentIndex, step);
            if (next is null || !IsLoaded)
                return;

            BeginTransition(next);
            if (!_animate || !_active)
                CompleteTransition();
        }
        finally
        {
            _navigating = false;
            UpdatePlaybackTrack();
            UpdateRenderingSubscription();
            StartPreloading();
        }
    }

    public void SetPlayback(bool active, bool animationsEnabled, bool highQuality)
    {
        _active = active;
        _animate = animationsEnabled;
        _highQuality = highQuality;
        if (!_animate && _incoming is not null)
        {
            CompleteTransition();
        }

        ApplyZoom(VisibleImage, _frameAge);
        if (_incoming is not null)
        {
            ApplyZoom(NextImage, _fadeTime);
        }

        UpdatePlaybackTrack();
        UpdateRenderingSubscription();
        StartPreloading();
    }

    private Image VisibleImage => _images[_visibleSlot];
    private Image NextImage => _images[1 - _visibleSlot];

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateRenderingSubscription();
        if (_initialized)
        {
            StartPreloading();
            return;
        }

        _initialized = true;
        _frames = await Task.Run(GalleryContent.ReadFrames);
        _current = await DecodeFromAsync(0, excludedIndex: -1);
        if (_current is null)
        {
            _emptyState.Visibility = Visibility.Visible;
            return;
        }

        VisibleImage.Source = _current.Source;
        VisibleImage.Opacity = 1;
        NotifyFrameChanged(_current);
        UpdatePlaybackTrack();
        UpdateRenderingSubscription();
        StartPreloading();
    }

    private async Task<DecodedFrame?> DecodeFromAsync(int startIndex, int excludedIndex, int step = 1)
    {
        int width = _highQuality ? 2048 : 1120;
        int height = _highQuality ? 1200 : 700;
        for (int offset = 0; offset < _frames.Count; offset++)
        {
            int index = (startIndex + step * offset + _frames.Count) % _frames.Count;
            if (index == excludedIndex || _invalidFrames.Contains(index))
            {
                continue;
            }

            GalleryFrame frame = _frames[index];
            BitmapSource? source = await Task.Run(() => GalleryContent.DecodeFrame(frame.Path, width, height));
            if (source is not null)
            {
                return new DecodedFrame(index, frame, source);
            }

            _invalidFrames.Add(index);
        }

        return null;
    }

    private async void StartPreloading()
    {
        if (!IsLoaded || !_active || !_animate || _preloading || _navigating || _preloaded is not null
            || _incoming is not null || _current is null || FrameCount < 2)
        {
            return;
        }

        int currentIndex = _current.Index;
        _preloading = true;
        try
        {
            var next = await DecodeFromAsync(currentIndex + 1, currentIndex);
            if (_current?.Index == currentIndex && _incoming is null && !_navigating)
                _preloaded = next;
        }
        finally
        {
            _preloading = false;
        }

        UpdatePlaybackTrack();
        UpdateRenderingSubscription();
        if (_current?.Index != currentIndex)
            StartPreloading();
    }

    private void UpdateRenderingSubscription()
    {
        bool shouldRender = IsLoaded && IsVisible && _active && _animate && _current is not null
            && (FrameCount > 1 || (_highQuality && _frameAge < DisplaySeconds + 2 * CrossfadeSeconds));
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
        _lastRenderTime = rendering.RenderingTime;
        if (elapsed <= 0)
        {
            return;
        }

        _frameAge += elapsed;
        ApplyZoom(VisibleImage, _frameAge);
        if (_incoming is not null)
        {
            _fadeTime += elapsed;
            double fraction = Math.Min(1, _fadeTime / CrossfadeSeconds);
            NextImage.Opacity = fraction * fraction * (3 - 2 * fraction);
            ApplyZoom(NextImage, _fadeTime);
            if (fraction >= 1)
            {
                CompleteTransition();
            }
        }
        else
        {
            _holdTime = Math.Min(DisplaySeconds, _holdTime + elapsed);
            if (_holdTime >= DisplaySeconds && _preloaded is not null)
            {
                BeginTransition(_preloaded);
            }
        }

        _playbackProgress.ScaleX = _incoming is null ? _holdTime / DisplaySeconds : 1;
        if (FrameCount < 2 && _frameAge >= DisplaySeconds + 2 * CrossfadeSeconds)
        {
            UpdateRenderingSubscription();
        }
    }

    private void BeginTransition(DecodedFrame next)
    {
        _incoming = next;
        _preloaded = null;
        _fadeTime = 0;
        NextImage.Source = next.Source;
        NextImage.Opacity = 0;
        ApplyZoom(NextImage, 0);
        SetZIndex(VisibleImage, 0);
        SetZIndex(NextImage, 1);
        NotifyFrameChanged(next);
    }

    private void CompleteTransition()
    {
        NextImage.Opacity = 1;
        VisibleImage.Opacity = 0;
        VisibleImage.Source = null;
        _visibleSlot = 1 - _visibleSlot;
        _current = _incoming;
        _incoming = null;
        _frameAge = _fadeTime;
        _holdTime = 0;
        StartPreloading();
    }

    private void NotifyFrameChanged(DecodedFrame next)
    {
        string caption = next.Frame.Caption;
        AutomationProperties.SetName(this, string.IsNullOrEmpty(caption) ? "Галерея игровых кадров" : caption);
        FrameChanged?.Invoke(this, next.Frame);
    }

    private void UpdatePlaybackTrack()
    {
        _playbackTrack.Visibility = _animate && _current is not null && FrameCount > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ApplyZoom(Image image, double age)
    {
        double scale = _highQuality && _animate
            ? 1 + Math.Min(age / (DisplaySeconds + 2 * CrossfadeSeconds), 1) * 0.065
            : 1;
        var transform = (ScaleTransform)image.RenderTransform;
        transform.ScaleX = scale;
        transform.ScaleY = scale;
    }

    private static Image CreateImage()
    {
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform()
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
        return image;
    }

    private static StackPanel CreateEmptyState()
    {
        var state = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        state.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 1,9 L 1,1 12,1 M 33,1 L 44,1 44,9 M 44,22 L 44,30 33,30 M 12,30 L 1,30 1,22 M 8,23 L 19,11 26,18 31,13 37,23"),
            Stroke = new SolidColorBrush(Color.FromRgb(133, 152, 193)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12)
        });
        state.Children.Add(new TextBlock
        {
            Text = "Пока нет игровых кадров",
            Foreground = Brushes.White,
            FontSize = 16,
            TextAlignment = TextAlignment.Center
        });
        state.Children.Add(new TextBlock
        {
            Text = "Добавьте снимки в GalleryContent/Screenshots",
            Foreground = new SolidColorBrush(Color.FromRgb(163, 175, 202)),
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return state;
    }

    private sealed record DecodedFrame(int Index, GalleryFrame Frame, BitmapSource Source);
}
