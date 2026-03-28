using System.IO;
using ZModManager.Models;

namespace ZModManager.Services;

/// <summary>
/// Detects whether a Unity game uses the Mono or IL2CPP scripting backend
/// by inspecting the game directory layout.
/// </summary>
public class RuntimeDetector
{
    public RuntimeType Detect(string gameDirectory)
    {
        if (string.IsNullOrEmpty(gameDirectory) || !Directory.Exists(gameDirectory))
            return RuntimeType.Unknown;

        // ── IL2CPP: has GameAssembly.dll ──────────────────────────────────────
        if (File.Exists(Path.Combine(gameDirectory, "GameAssembly.dll")))
            return RuntimeType.IL2CPP;

        // Check one level up (e.g. game lives in x64/ sub-folder)
        var parent = Directory.GetParent(gameDirectory)?.FullName;
        if (parent != null && File.Exists(Path.Combine(parent, "GameAssembly.dll")))
            return RuntimeType.IL2CPP;

        // ── Mono: has *_Data/Managed/ directory ───────────────────────────────
        foreach (var dir in Directory.GetDirectories(gameDirectory))
        {
            if (!dir.EndsWith("_Data", System.StringComparison.OrdinalIgnoreCase)) continue;

            var managed = Path.Combine(dir, "Managed");
            if (Directory.Exists(managed))
                return RuntimeType.Mono;
        }

        // ── Mono: mono*.dll present in game root or known sub-paths ──────────
        var monoPaths = new[]
        {
            gameDirectory,
            Path.Combine(gameDirectory, "MonoBleedingEdge", "EmbedRuntime"),
            Path.Combine(gameDirectory, "Mono",             "EmbedRuntime"),
        };

        foreach (var p in monoPaths)
        {
            if (!Directory.Exists(p)) continue;
            foreach (var f in Directory.GetFiles(p, "mono*.dll"))
                return RuntimeType.Mono;
        }

        return RuntimeType.Unknown;
    }
}
