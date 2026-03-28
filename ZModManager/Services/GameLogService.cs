using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ZModManager.Services;

/// <summary>
/// Tails a Unity Player.log file in real-time and fires <see cref="LineAppended"/>
/// for every new line that Unity writes.
/// </summary>
public sealed class GameLogService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private string  _watchPath = string.Empty;
    private long    _lastPos   = 0;
    private bool    _disposed;

    public event Action<string>? LineAppended;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <param name="logFilePath">Path to the log file to tail.</param>
    /// <param name="fromBeginning">
    ///   When <c>true</c> the watcher reads from byte 0 so the full log is
    ///   replayed (use after a fresh game launch).  When <c>false</c> (default)
    ///   it starts at the current end of the file so only new lines are shown.
    /// </param>
    public void StartWatching(string logFilePath, bool fromBeginning = false)
    {
        StopWatching();

        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
            return;

        _watchPath = logFilePath;
        _lastPos   = fromBeginning ? 0 : new FileInfo(logFilePath).Length;

        var dir  = Path.GetDirectoryName(logFilePath)!;
        var file = Path.GetFileName(logFilePath);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter    = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFileChanged;
    }

    public void StopWatching()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
        _lastPos = 0;
    }

    /// <summary>
    /// Checks common mod-framework log locations inside <paramref name="gameDirectory"/>
    /// before falling back to the Unity Player.log in LocalLow.
    /// Priority: MelonLoader → BepInEx → output_log.txt → Player.log (LocalLow).
    /// Returns null if nothing is found.
    /// </summary>
    public static string? AutoDetectForGame(string gameDirectory)
    {
        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            var candidates = new[]
            {
                Path.Combine(gameDirectory, "MelonLoader", "Latest.log"),
                Path.Combine(gameDirectory, "BepInEx",     "LogOutput.log"),
                Path.Combine(gameDirectory, "output_log.txt"),
            };

            foreach (var path in candidates)
                if (File.Exists(path)) return path;
        }

        return AutoDetectLogPath();
    }

    /// <summary>
    /// Scans %USERPROFILE%\AppData\LocalLow\ for the most-recently-modified Player.log.
    /// Returns null if none found.
    /// </summary>
    public static string? AutoDetectLogPath()
    {
        try
        {
            var localLow = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow");

            if (!Directory.Exists(localLow)) return null;

            return Directory.EnumerateFiles(localLow, "Player.log", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Small debounce — Unity can fire several events in a burst
        Thread.Sleep(40);

        try
        {
            using var fs = new FileStream(_watchPath,
                FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (fs.Length <= _lastPos) return; // file was truncated or no new data

            fs.Seek(_lastPos, SeekOrigin.Begin);

            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);

            string? line;
            while ((line = reader.ReadLine()) != null)
                LineAppended?.Invoke(line);

            _lastPos = fs.Position;
        }
        catch
        {
            // File may be locked momentarily — next event will catch up
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopWatching();
        _disposed = true;
    }
}
