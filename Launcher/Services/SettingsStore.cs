using System;
using System.IO;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pereulki.Launcher.Models;

namespace Pereulki.Launcher.Services;

public sealed record SettingsLoadResult(LauncherSettings Settings, string? Warning = null);

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PereulkiLauncher", "settings.json");
    }

    public string FilePath { get; }

    public SettingsLoadResult Load()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            var settings = JsonSerializer.Deserialize<LauncherSettings>(stream, JsonOptions);
            return settings is null
                ? new(new LauncherSettings(), "Файл настроек пуст. Загружены значения по умолчанию.")
                : new(settings with { GamePath = settings.GamePath ?? string.Empty });
        }
        catch (FileNotFoundException)
        {
            return new(new LauncherSettings());
        }
        catch (DirectoryNotFoundException)
        {
            return new(new LauncherSettings());
        }
        catch (JsonException)
        {
            return new(new LauncherSettings(), "Файл настроек повреждён. Загружены значения по умолчанию.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return new(new LauncherSettings(), "Не удалось прочитать настройки. Проверьте доступ к файлу настроек.");
        }
    }

    public string? Save(LauncherSettings settings)
    {
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException or JsonException)
        {
            return "Не удалось сохранить настройки. Проверьте доступ к папке настроек и свободное место на диске.";
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                // A locked temporary file must not mask the save error.
            }
        }
    }
}
