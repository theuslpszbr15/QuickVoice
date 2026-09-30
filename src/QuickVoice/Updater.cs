using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace QuickVoice;

/// <summary>Asks GitHub for the latest release and, when it is newer, installs it quietly and restarts.</summary>
internal static class Updater
{
    public sealed record Release(Version Version, string Tag, string Page, string? Setup);

    private const string Api = "https://api.github.com/repos/theuslpszbr15/QuickVoice/releases/latest";
    private const string Downloads = "https://github.com/theuslpszbr15/QuickVoice/releases/download/";
    private const string Pages = "https://github.com/theuslpszbr15/QuickVoice/releases/";

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>Installed with the setup (it leaves an uninstaller next to the exe), not unzipped by hand.</summary>
    public static bool Installed => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"QuickVoice/{Current}");
        return client;
    }

    /// <summary>The latest release when it is newer than this one; null when up to date or GitHub cannot be reached.</summary>
    public static async Task<Release?> CheckAsync()
    {
        try
        {
            using var client = Client();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var document = JsonDocument.Parse(await client.GetStringAsync(Api));
            var root = document.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || Normalize(version) <= Normalize(Current)) return null;
            var page = root.TryGetProperty("html_url", out var url) && url.GetString() is { } html && html.StartsWith(Pages, StringComparison.Ordinal) ? html : Pages + "latest";
            var setup = root.GetProperty("assets").EnumerateArray()
                .Select(a => a.GetProperty("browser_download_url").GetString())
                .FirstOrDefault(u => u is not null && u.StartsWith(Downloads, StringComparison.Ordinal)
                                     && u.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && u.Contains("Setup", StringComparison.OrdinalIgnoreCase));
            return new Release(version, tag, page, setup);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the setup and runs it silently; it closes this app, installs over it and starts it again.
    /// Unzipped copies (no uninstaller) open the release page instead. Returns false when nothing was started.
    /// </summary>
    public static async Task<bool> InstallAsync(Release release)
    {
        if (!Installed || release.Setup is null)
        {
            Process.Start(new ProcessStartInfo(release.Page) { UseShellExecute = true })?.Dispose();
            return false;
        }
        var file = Path.Combine(Path.GetTempPath(), $"QuickVoice-Setup-{release.Version}.exe");
        using (var client = Client())
        await using (var download = await client.GetStreamAsync(release.Setup))
        await using (var output = File.Create(file))
        {
            await download.CopyToAsync(output);
        }
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var argument in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS" }) start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
        return true;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
