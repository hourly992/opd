using System;
using System.Reflection;

namespace Pereulki.Launcher.Services;

public sealed record UpdateStatus(string LauncherVersion, bool IsConfigured, string Message);

public sealed class UpdateService
{
    public UpdateStatus GetStatus()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var productVersion = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString() ?? "1.0.0"
            : informationalVersion.Split('+', 2, StringSplitOptions.None)[0];

        return new(productVersion, false, "Сервер обновлений игры пока не настроен.");
    }
}
