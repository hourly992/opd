using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pereulki.Launcher.Models;

namespace Pereulki.Launcher.Views;

public partial class SettingsView : UserControl
{
    public event EventHandler<LauncherSettings>? SaveRequested;

    public SettingsView()
    {
        InitializeComponent();
    }

    public void LoadSettings(LauncherSettings settings, string? automaticPath)
    {
        GamePathBox.Text = settings.GamePath;
        AnimationsBox.IsChecked = settings.AnimationsEnabled;
        QualityBox.SelectedIndex = settings.EffectQuality == EffectQuality.Low ? 1 : 0;
        BehaviorBox.SelectedIndex = settings.LaunchBehavior switch
        {
            LaunchBehavior.KeepOpen => 1,
            LaunchBehavior.Close => 2,
            _ => 0
        };
        PathHint.Text = automaticPath is null
            ? "Выберите .exe. Пустое поле включает автоматический поиск игры."
            : $"Пустое поле включает автоматический поиск. Найден {Path.GetFileName(automaticPath)}.";
        ShowIssue(null);
    }

    public void ShowIssue(string? message)
    {
        Issue.Text = message;
        Issue.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            CheckPathExists = true,
            DereferenceLinks = true,
            Multiselect = false,
            DefaultExt = ".exe",
            Filter = "Исполняемые файлы (*.exe)|*.exe",
            Title = "Выберите исполняемый файл игры"
        };
        if (File.Exists(GamePathBox.Text.Trim()))
            dialog.InitialDirectory = Path.GetDirectoryName(GamePathBox.Text.Trim());
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            GamePathBox.Text = dialog.FileName;
            ShowIssue(null);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = GamePathBox.Text.Trim();
        var quality = QualityBox.SelectedIndex == 1 ? EffectQuality.Low : EffectQuality.High;
        var behavior = BehaviorBox.SelectedIndex switch
        {
            1 => LaunchBehavior.KeepOpen,
            2 => LaunchBehavior.Close,
            _ => LaunchBehavior.Minimize
        };
        SaveRequested?.Invoke(this, new LauncherSettings
        {
            GamePath = path,
            AnimationsEnabled = AnimationsBox.IsChecked == true,
            EffectQuality = quality,
            LaunchBehavior = behavior
        });
    }
}
