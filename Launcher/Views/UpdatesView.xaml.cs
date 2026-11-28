using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Pereulki.Launcher.Services;

namespace Pereulki.Launcher.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
    }

    public void ShowLoading(string launcherVersion)
    {
        VersionText.Text = $"v{launcherVersion}";
        UpdateMessage.Text = "Загружаем список версий игры…";
    }

    public void ShowCatalog(GameCatalog catalog)
    {
        UpdateMessage.Text = catalog.Message;
        GameVersions.ItemsSource = catalog.Releases;
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not GameRelease release)
            return;

        try
        {
            using var browser = Process.Start(new ProcessStartInfo(release.Download.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            UpdateMessage.Text = "Windows не удалось открыть ссылку на архив.";
        }
    }
}
