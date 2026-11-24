using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Pereulki.Launcher.Controls;
using Pereulki.Launcher.Models;
using Pereulki.Launcher.Services;
using Pereulki.Launcher.Visuals;

namespace Pereulki.Launcher;

public partial class MainWindow : Window
{
    private static readonly Geometry MaximizeIcon = Geometry.Parse("M 2,2 L 10,2 10,10 2,10 Z");
    private static readonly Geometry RestoreIcon = Geometry.Parse("M 4,4 L 10,4 10,10 4,10 Z M 2,8 L 2,2 8,2");

    private readonly SettingsStore settingsStore = new();
    private readonly GameLauncher game = new();
    private readonly UpdateService updates = new();
    private LauncherSettings settings = new();
    private Button? panelOrigin;
    private bool initialized;
    private bool? ambientMotion;

    public MainWindow()
    {
        InitializeComponent();
        Gallery.FrameChanged += Gallery_FrameChanged;
        SettingsPanel.SaveRequested += SettingsPanel_SaveRequested;
        Loaded += Window_Loaded;
        Activated += (_, _) => ApplyPlayback();
        Deactivated += (_, _) => ApplyPlayback();
        StateChanged += (_, _) =>
        {
            MaximizeGlyph.Data = WindowState == WindowState.Maximized ? RestoreIcon : MaximizeIcon;
            ApplyPlayback();
        };
    }

    private bool AnimationsEnabled => settings.AnimationsEnabled && SystemParameters.ClientAreaAnimation;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized)
            return;

        var loaded = settingsStore.Load();
        settings = loaded.Settings;
        var version = updates.GetStatus();
        TopVersion.Text = $"v{version.LauncherVersion}";
        UpdatesPanel.ShowStatus(version);
        RefreshGameStatus();
        if (loaded.Warning is { } warning)
            SetStatus(warning, isError: true);

        initialized = true;
        ApplyPlayback();
        if (AnimationsEnabled)
            Fade(TitleComposition, .22, 1, .54);
    }

    private void Gallery_FrameChanged(object? sender, GalleryFrame frame)
    {
        FrameCaption.Text = frame.Caption;
        FrameIndex.Text = string.Format(CultureInfo.InvariantCulture, "{0:00} / {1:00}", Gallery.CurrentFrameNumber, Gallery.FrameCount);
        Glow.ApplyFrame(frame);
    }

    private void ApplyPlayback()
    {
        if (!initialized)
            return;

        bool animations = AnimationsEnabled;
        bool inForeground = IsActive && WindowState != WindowState.Minimized;
        bool playAmbient = inForeground && Overlay.Visibility != Visibility.Visible;
        bool highQuality = settings.EffectQuality == EffectQuality.High;
        MotionSettings.SetIsEnabled(this, animations && inForeground);
        Gallery.SetPlayback(playAmbient, animations, highQuality);
        Glow.SetPlayback(playAmbient, animations, highQuality);

        bool animateDetails = playAmbient && animations && highQuality;
        if (ambientMotion == animateDetails)
            return;

        ambientMotion = animateDetails;
        Logo.SetPlayback(animateDetails);
        if (animateDetails)
        {
            RouteMotion.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 17, TimeSpan.FromSeconds(3.8))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            });
        }
        else
        {
            RouteMotion.BeginAnimation(TranslateTransform.XProperty, null);
        }
    }

    private string? ResolveGamePath(string configuredPath) => string.IsNullOrWhiteSpace(configuredPath)
        ? game.DiscoverGamePath()
        : configuredPath;

    private void RefreshGameStatus()
    {
        var executable = ResolveGamePath(settings.GamePath);
        var issue = game.ValidatePath(executable);
        SetStatus(issue ?? "Игра готова к запуску", isError: issue is not null, executable);
    }

    private void SetStatus(string message, bool isError = false, string? path = null)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(isError ? "Error" : "Muted");
        StatusDot.Fill = (Brush)FindResource(isError ? "Error" : "Brass");
        StatusText.ToolTip = path;
    }

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!PlayButton.IsEnabled)
            return;

        PlayButton.IsEnabled = false;
        SetStatus("Запускаем игру…");
        var launchSettings = settings;
        var (launch, invalidPath) = await Task.Run(() =>
        {
            var path = ResolveGamePath(launchSettings.GamePath);
            var result = game.Launch(path);
            return (result, !result.Success && game.ValidatePath(path) is not null);
        });
        if (!IsLoaded)
            return;

        PlayButton.IsEnabled = true;
        if (!launch.Success)
        {
            SetStatus(launch.Error ?? "Не удалось запустить игру.", isError: true);
            if (invalidPath && Overlay.Visibility != Visibility.Visible && launchSettings == settings)
            {
                OpenSettings(PlayButton);
                SettingsPanel.ShowIssue(launch.Error);
            }
            return;
        }

        SetStatus("Игра запущена.");
        switch (launchSettings.LaunchBehavior)
        {
            case LaunchBehavior.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case LaunchBehavior.Close:
                Close();
                break;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings(SettingsButton);

    private void OpenSettings(Button origin)
    {
        SettingsPanel.LoadSettings(settings, game.DiscoverGamePath());
        OpenPanel("Настройки", showSettings: true, origin);
    }

    private void Updates_Click(object sender, RoutedEventArgs e)
    {
        UpdatesPanel.ShowStatus(updates.GetStatus());
        OpenPanel("Обновления", showSettings: false, UpdatesButton);
    }

    private void SettingsPanel_SaveRequested(object? sender, LauncherSettings proposed)
    {
        var error = string.IsNullOrEmpty(proposed.GamePath) ? null : game.ValidatePath(proposed.GamePath);
        error ??= settingsStore.Save(proposed);
        if (error is not null)
        {
            SettingsPanel.ShowIssue(error);
            return;
        }

        settings = proposed;
        RefreshGameStatus();
        ClosePanel();
    }

    private void OpenPanel(string title, bool showSettings, Button origin)
    {
        panelOrigin = origin;
        PanelTitle.Text = title;
        SettingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        UpdatesPanel.Visibility = showSettings ? Visibility.Collapsed : Visibility.Visible;
        Overlay.Visibility = Visibility.Visible;
        MainScene.IsEnabled = false;
        ApplyPlayback();
        if (AnimationsEnabled)
        {
            Fade(Overlay, 0, 1, .20);
            PanelMotion.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromSeconds(.25))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
        PanelClose.Focus();
    }

    private void ClosePanel()
    {
        Overlay.BeginAnimation(OpacityProperty, null);
        PanelMotion.BeginAnimation(TranslateTransform.YProperty, null);
        Overlay.Visibility = Visibility.Collapsed;
        MainScene.IsEnabled = true;
        ApplyPlayback();
        panelOrigin?.Focus();
        panelOrigin = null;
    }

    private static void Fade(UIElement element, double from, double to, double seconds)
    {
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Escape && Overlay.Visibility == Visibility.Visible)
        {
            ClosePanel();
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else
            DragMove();
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void PanelClose_Click(object sender, RoutedEventArgs e) => ClosePanel();

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Overlay))
            ClosePanel();
    }
}
