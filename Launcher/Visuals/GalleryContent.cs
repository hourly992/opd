using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pereulki.Launcher.Visuals;

public enum GlowDirection
{
    Left,
    Right
}

public sealed record GalleryFrame(string Path, string Caption, Color Color, double Intensity, GlowDirection Direction);

internal static class GalleryContent
{
    internal const string DirectoryName = "GalleryContent";
    internal const string ScreenshotsDirectoryName = "Screenshots";

    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp"];
    private static readonly Color[] DefaultColors =
    [
        Color.FromRgb(79, 103, 238),
        Color.FromRgb(132, 78, 216),
        Color.FromRgb(236, 105, 51)
    ];

    internal static IReadOnlyList<GalleryFrame> ReadFrames()
    {
        string contentFolder = Path.Combine(AppContext.BaseDirectory, DirectoryName);
        string screenshotsFolder = Path.Combine(contentFolder, ScreenshotsDirectoryName);
        string[] files;

        try
        {
            files = Directory.EnumerateFiles(screenshotsFolder)
                .Where(file => SupportedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Не удалось открыть папку игровых кадров: {error.Message}");
            return [];
        }

        var remaining = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        var frames = new List<GalleryFrame>(files.Length);
        foreach (FrameEntry? entry in ReadEntries(Path.Combine(contentFolder, "frames.json")))
        {
            if (entry?.File is not { Length: > 0 } fileName || Path.GetFileName(fileName) != fileName)
            {
                continue;
            }

            string file = Path.Combine(screenshotsFolder, fileName);
            if (remaining.Remove(file))
            {
                frames.Add(MakeFrame(file, entry, frames.Count));
            }
        }

        foreach (string file in files)
        {
            if (remaining.Remove(file))
            {
                frames.Add(MakeFrame(file, null, frames.Count));
            }
        }

        return frames;
    }

    internal static BitmapSource? DecodeFrame(string path, int maximumWidth, int maximumHeight)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var metadata = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if (metadata.PixelWidth < 1 || metadata.PixelHeight < 1
                || metadata.PixelWidth * (long)metadata.PixelHeight > 64_000_000)
            {
                Trace.TraceWarning($"Игровой кадр «{Path.GetFileName(path)}» превышает допустимый размер.");
                return null;
            }

            double widthRatio = (double)maximumWidth / metadata.PixelWidth;
            double heightRatio = (double)maximumHeight / metadata.PixelHeight;
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (widthRatio < 1 && widthRatio <= heightRatio)
            {
                image.DecodePixelWidth = maximumWidth;
            }
            else if (heightRatio < 1)
            {
                image.DecodePixelHeight = maximumHeight;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or InvalidOperationException or FormatException or COMException)
        {
            Trace.TraceWarning($"Не удалось открыть игровой кадр «{Path.GetFileName(path)}»: {error.Message}");
            return null;
        }
    }

    private static FrameEntry?[] ReadEntries(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<FrameEntry?[]>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            }) ?? [];
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning($"Не удалось прочитать настройки галереи: {error.Message}");
            return [];
        }
    }

    private static GalleryFrame MakeFrame(string path, FrameEntry? entry, int index)
    {
        Color color = DefaultColors[index % DefaultColors.Length];
        if (entry?.Color is { Length: 7 } hex && hex[0] == '#'
            && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        double intensity = entry?.Intensity is double configuredIntensity && double.IsFinite(configuredIntensity)
            ? Math.Clamp(configuredIntensity, 0, 1)
            : 0.75;
        GlowDirection direction = entry?.Direction?.ToLowerInvariant() switch
        {
            "left" => GlowDirection.Left,
            "right" => GlowDirection.Right,
            _ => index % 2 == 0 ? GlowDirection.Left : GlowDirection.Right
        };

        return new GalleryFrame(path, entry?.Caption ?? "", color, intensity, direction);
    }

    private sealed class FrameEntry
    {
        public string? File { get; init; }
        public string? Caption { get; init; }
        public string? Color { get; init; }
        public double? Intensity { get; init; }
        public string? Direction { get; init; }
    }
}
