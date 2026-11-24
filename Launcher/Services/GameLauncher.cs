using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace Pereulki.Launcher.Services;

public sealed record GameLaunchResult(bool Success, string? Error = null);

public sealed class GameLauncher
{
    private const string PlayerName = "DuninaGorkavicha.exe";
    private const string BuildName = "StreetWindowsHDRPAutumnMenu";

    public string? DiscoverGamePath()
    {
        var launcherDirectory = AppContext.BaseDirectory;
        var bundled = Path.Combine(launcherDirectory, "Game", PlayerName);
        if (File.Exists(bundled))
            return bundled;

        var colocated = Path.Combine(launcherDirectory, PlayerName);
        if (File.Exists(colocated))
            return colocated;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            var currentProject = Path.Combine(documents, "Games", "opd-hdrp-texture-experiment");
            var currentPlayer = GetProjectPlayerPath(currentProject);
            if (File.Exists(currentPlayer))
                return currentPlayer;
        }

        for (var directory = new DirectoryInfo(launcherDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = GetProjectPlayerPath(directory.FullName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public string? ValidatePath(string? path)
    {
        return ResolvePath(path, out _);
    }

    public GameLaunchResult Launch(string? path)
    {
        var validationError = ResolvePath(path, out var executable);
        if (validationError is not null || executable is null)
            return new(false, validationError);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = false
            });
            return process is null
                ? new(false, "Windows не запустила игру.")
                : new(true);
        }
        catch (Win32Exception ex)
        {
            return new(false, ex.NativeErrorCode switch
            {
                2 or 3 => "Исполняемый файл игры больше недоступен. Укажите новый путь в настройках.",
                5 or 740 => "Windows не разрешила запустить выбранный файл. Проверьте разрешения файла.",
                193 or 216 => "Формат выбранного файла несовместим с этой версией Windows.",
                _ => $"Windows не смогла запустить игру (код {ex.NativeErrorCode})."
            });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return new(false, "Не удалось открыть исполняемый файл игры. Проверьте путь и права доступа.");
        }
    }

    private static string GetProjectPlayerPath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Unity_Walk", "Build", BuildName, PlayerName);
    }

    private static string? ResolvePath(string? path, out string? executable)
    {
        executable = null;
        if (string.IsNullOrWhiteSpace(path))
            return "Укажите путь к исполняемому файлу игры в настройках.";

        try
        {
            var candidate = path.Trim();
            if (!Path.IsPathFullyQualified(candidate) ||
                !string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase))
                return "Укажите полный путь к файлу игры с расширением .exe.";

            var fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
                return "Файл игры недоступен. Укажите действующий путь в настройках.";

            executable = fullPath;
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or SecurityException)
        {
            return "Проверьте формат пути к игре и выберите её исполняемый файл.";
        }
    }
}
