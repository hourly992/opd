using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pereulki.Launcher.Services;

public sealed record GameRelease(string Title, string Tag, string Date, Uri Download);
public sealed record GameCatalog(IReadOnlyList<GameRelease> Releases, string Message);

public sealed class UpdateService
{
    private const string Repository = "hourly992/opd";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private IReadOnlyList<GameRelease> lastCatalog =
    [
        new("Трава и асфальт двора №7", "hdrp-court7-grass-asphalt-2026-11-27", "27.11.2026",
            new Uri("https://github.com/hourly992/opd/releases/download/hdrp-court7-grass-asphalt-2026-11-27/Pereulki-Court7-20261127-Windows-x64.zip"))
    ];

    static UpdateService()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("PereulkiLauncher/0.2.2");
        Client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string LauncherVersion =>
        typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+', 2)[0] ?? "0.2.2";

    public async Task<GameCatalog> GetGameCatalogAsync()
    {
        try
        {
            var releases = new List<GameRelease>();
            for (int page = 1; ; page++)
            {
                using var stream = await Client.GetStreamAsync(
                    $"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}");
                using var document = await JsonDocument.ParseAsync(stream);
                foreach (var release in document.RootElement.EnumerateArray())
                {
                    string tag = release.GetProperty("tag_name").GetString() ?? "";
                    if (release.GetProperty("draft").GetBoolean() ||
                        tag.StartsWith("pereulki-launcher-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    foreach (var asset in release.GetProperty("assets").EnumerateArray())
                    {
                        string file = asset.GetProperty("name").GetString() ?? "";
                        if (!(file.StartsWith("Pereulki-", StringComparison.OrdinalIgnoreCase) ||
                              file.StartsWith("DuninaGorkavicha-", StringComparison.OrdinalIgnoreCase)) ||
                            !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            !file.Contains("windows", StringComparison.OrdinalIgnoreCase) ||
                            file.Contains("launcher", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string title = release.GetProperty("name").GetString() ?? tag;
                        string date = release.GetProperty("published_at").TryGetDateTimeOffset(out var published)
                            ? published.ToLocalTime().ToString("dd.MM.yyyy") : "";
                        var url = new Uri($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(file)}");
                        releases.Add(new(title, tag, date, url));
                        break;
                    }
                }

                if (document.RootElement.GetArrayLength() < 100)
                    break;
            }
            if (releases.Count == 0)
                return new(lastCatalog, "В GitHub пока нет доступных Windows-архивов. Показан сохранённый список.");
            lastCatalog = releases;
            return new(lastCatalog, "Выберите версию Windows, чтобы скачать её с GitHub.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException or System.IO.IOException)
        {
            return new(lastCatalog, "GitHub сейчас недоступен. Показан сохранённый список.");
        }
    }
}
