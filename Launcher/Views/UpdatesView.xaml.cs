using System.Windows.Controls;
using Pereulki.Launcher.Services;

namespace Pereulki.Launcher.Views;

public partial class UpdatesView : UserControl
{
    public UpdatesView()
    {
        InitializeComponent();
    }

    public void ShowStatus(UpdateStatus status)
    {
        VersionText.Text = $"v{status.LauncherVersion}";
        UpdateMessage.Text = status.Message;
    }
}
