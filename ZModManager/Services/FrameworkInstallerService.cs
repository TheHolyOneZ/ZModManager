using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZModManager.Models;

namespace ZModManager.Services;

/// <summary>
/// Downloads and installs MelonLoader or BepInEx into a Unity IL2CPP game directory
/// straight from their official GitHub releases.
/// </summary>
public class FrameworkInstallerService : IDisposable
{
    private readonly LogService _log;
    private readonly HttpClient _http;
    private bool _disposed;

    public FrameworkInstallerService(LogService log)
    {
        _log = log;
        _http = new HttpClient();
        // GitHub API requires a User-Agent header
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ZModManager/1.0 (github.com/zmodmanager)");
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    // ── MelonLoader ───────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the latest MelonLoader x64 release from GitHub and extracts it
    /// into <paramref name="gameDirectory"/>.
    /// </summary>
    public async Task InstallMelonLoaderAsync(
        string gameDirectory, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Fetching latest MelonLoader release…");
        _log.Info("[MelonLoader] Querying latest release…");

        var url = await GetLatestAssetUrlAsync(
            "LavaGang", "MelonLoader",
            a => a.EndsWith("MelonLoader.x64.zip", StringComparison.OrdinalIgnoreCase),
            includePreRelease: false,
            ct);

        if (url == null)
            throw new InvalidOperationException(
                "Could not find MelonLoader.x64.zip in the latest GitHub release.\n" +
                "Check your internet connection or download manually from:\n" +
                "https://github.com/LavaGang/MelonLoader/releases/latest");

        _log.Info($"[MelonLoader] Downloading from: {url}");
        progress?.Report("Downloading MelonLoader…");

        var zipBytes = await DownloadAsync(url, progress, ct);

        progress?.Report("Extracting MelonLoader…");
        _log.Info("[MelonLoader] Extracting to game directory…");
        ExtractZip(zipBytes, gameDirectory);

        _log.Success($"[MelonLoader] Installed in: {gameDirectory}");
        progress?.Report("MelonLoader installed.");
    }

    /// <summary>
    /// Removes MelonLoader from the game directory (MelonLoader/ folder,
    /// winhttp.dll / version.dll proxy, UserData/, UserLibs/).
    /// </summary>
    public void UninstallMelonLoader(string gameDirectory)
    {
        RemoveIfExists(Path.Combine(gameDirectory, "MelonLoader"));
        RemoveIfExists(Path.Combine(gameDirectory, "Mods"));
        RemoveIfExists(Path.Combine(gameDirectory, "UserData"));
        RemoveIfExists(Path.Combine(gameDirectory, "UserLibs"));
        // Remove the proxy DLL (MelonLoader ships winhttp.dll)
        foreach (var proxy in new[] { "winhttp.dll", "version.dll", "dobby.dll" })
        {
            var f = Path.Combine(gameDirectory, proxy);
            if (File.Exists(f)) File.Delete(f);
        }
        _log.Info("[MelonLoader] Uninstalled.");
    }

    // ── BepInEx ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the latest BepInEx 6 (Unity IL2CPP, win x64) release from GitHub
    /// and extracts it into <paramref name="gameDirectory"/>.
    /// </summary>
    public async Task InstallBepInExAsync(
        string gameDirectory, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Fetching latest BepInEx release…");
        _log.Info("[BepInEx] Querying latest release…");

        // BepInEx 6 pre-releases are the IL2CPP-capable ones.
        // Asset pattern: BepInEx-Unity.IL2CPP-win-x64-*.zip
        var url = await GetLatestAssetUrlAsync(
            "BepInEx", "BepInEx",
            a => a.Contains("Unity.IL2CPP", StringComparison.OrdinalIgnoreCase)
              && a.Contains("win-x64",      StringComparison.OrdinalIgnoreCase)
              && a.EndsWith(".zip",         StringComparison.OrdinalIgnoreCase),
            includePreRelease: true, // BepInEx 6 is still pre-release
            ct);

        if (url == null)
            throw new InvalidOperationException(
                "Could not find a BepInEx Unity.IL2CPP win-x64 release on GitHub.\n" +
                "Check your internet connection or download manually from:\n" +
                "https://github.com/BepInEx/BepInEx/releases");

        _log.Info($"[BepInEx] Downloading from: {url}");
        progress?.Report("Downloading BepInEx…");

        var zipBytes = await DownloadAsync(url, progress, ct);

        progress?.Report("Extracting BepInEx…");
        _log.Info("[BepInEx] Extracting to game directory…");
        ExtractZip(zipBytes, gameDirectory);

        _log.Success($"[BepInEx] Installed in: {gameDirectory}");
        progress?.Report("BepInEx installed.");
    }

    /// <summary>Removes BepInEx from the game directory.</summary>
    public void UninstallBepInEx(string gameDirectory)
    {
        RemoveIfExists(Path.Combine(gameDirectory, "BepInEx"));
        foreach (var proxy in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
        {
            var f = Path.Combine(gameDirectory, proxy);
            if (File.Exists(f)) File.Delete(f);
        }
        _log.Info("[BepInEx] Uninstalled.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries the GitHub Releases API for <paramref name="owner"/>/<paramref name="repo"/>
    /// and returns the browser_download_url of the first asset whose filename
    /// satisfies <paramref name="assetPredicate"/>.
    /// When <paramref name="includePreRelease"/> is true, pre-releases are included.
    /// </summary>
    private async Task<string?> GetLatestAssetUrlAsync(
        string owner, string repo,
        Func<string, bool> assetPredicate,
        bool includePreRelease,
        CancellationToken ct)
    {
        var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases";
        var json   = await _http.GetStringAsync(apiUrl, ct);

        using var doc = JsonDocument.Parse(json);
        var releases  = doc.RootElement.EnumerateArray();

        foreach (var release in releases)
        {
            bool isPreRelease = release.GetProperty("prerelease").GetBoolean();
            if (isPreRelease && !includePreRelease) continue;

            if (!release.TryGetProperty("assets", out var assets)) continue;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (!assetPredicate(name)) continue;

                return asset.GetProperty("browser_download_url").GetString();
            }
        }
        return null;
    }

    private async Task<byte[]> DownloadAsync(
        string url, IProgress<string>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total  = response.Content.Headers.ContentLength;
        using var  stream = await response.Content.ReadAsStreamAsync(ct);
        using var  ms     = new MemoryStream();
        var        buf    = new byte[81920];
        long       read   = 0;
        int        n;

        while ((n = await stream.ReadAsync(buf, ct)) > 0)
        {
            await ms.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total.HasValue)
                progress?.Report($"Downloading… {read / 1024:N0} KB / {total.Value / 1024:N0} KB");
        }
        return ms.ToArray();
    }

    private static void ExtractZip(byte[] zipBytes, string targetDirectory)
    {
        using var ms      = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry

            var destPath = Path.Combine(targetDirectory,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using var src  = entry.Open();
            using var dest = File.Create(destPath);
            src.CopyTo(dest);
        }
    }

    private static void RemoveIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}
