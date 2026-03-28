using System.IO;
using System.Linq;
using ZModManager.Models;

namespace ZModManager.Services;

/// <summary>
/// Identifies which game engine a game executable belongs to by inspecting
/// the game directory layout, and returns a human-friendly recommendation.
/// </summary>
public class EngineAnalyzer
{
    public EngineInfo Analyze(string gameExePath)
    {
        var dir = Path.GetDirectoryName(gameExePath) ?? gameExePath;

        // ── Unity IL2CPP ────────────────────────────────────────────────────
        if (File.Exists(Path.Combine(dir, "GameAssembly.dll")))
        {
            // Also check parent in case EXE is in a sub-folder
            return new EngineInfo(
                EngineType.UnityIL2CPP,
                "Unity (IL2CPP)",
                "This is a Unity IL2CPP game.\n\n" +
                "IL2CPP converts .NET code to native C++ at build time, so mods need a framework to hook into the runtime:\n\n" +
                "• MelonLoader — the most popular choice for IL2CPP games. Most mods you find online are made for MelonLoader.\n" +
                "• BepInEx 6 — a powerful alternative. Has a plugin system compatible with BepInEx Harmony patches.\n\n" +
                "⚠  MelonLoader mods (.dll) and BepInEx plugins (.dll) are NOT interchangeable — each mod is built for one specific framework. Check the mod's download page to see which one it requires.",
                RuntimeType.IL2CPP);
        }

        // ── Unity Mono ──────────────────────────────────────────────────────
        foreach (var dir2 in Directory.GetDirectories(dir))
        {
            if (!dir2.EndsWith("_Data", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (Directory.Exists(Path.Combine(dir2, "Managed")))
                return new EngineInfo(
                    EngineType.UnityMono,
                    "Unity (Mono)",
                    "This is a Unity Mono game.\n\n" +
                    "Mono games run .NET assemblies natively, which means ZModManager can inject mods directly " +
                    "into the running process — no framework installation needed.\n\n" +
                    "Just add your mod, enable it, and click Launch + Inject. ZModManager will inject it using " +
                    "the Mono C API (mono_domain_assembly_open → your mod's entry point runs).\n\n" +
                    "If your mod uses Harmony patches, make sure 0Harmony.dll is also present in the game's Managed folder.",
                    RuntimeType.Mono);
        }

        // mono*.dll fallback
        foreach (var f in Directory.GetFiles(dir, "mono*.dll"))
            return new EngineInfo(
                EngineType.UnityMono,
                "Unity (Mono)",
                "Unity Mono game detected. Direct injection supported — no framework required.",
                RuntimeType.Mono);

        // ── Unreal Engine ───────────────────────────────────────────────────
        var unrealMarkers = new[]
        {
            Path.Combine(dir, "Engine", "Binaries"),
            Path.Combine(dir, "Engine", "Content"),
        };
        if (unrealMarkers.Any(Directory.Exists) ||
            Directory.GetFiles(dir, "UE4Game*.exe").Length > 0 ||
            Directory.GetFiles(dir, "UE5Game*.exe").Length > 0 ||
            File.Exists(Path.Combine(dir, "UnrealPak.exe")))
        {
            return new EngineInfo(
                EngineType.UnrealEngine,
                "Unreal Engine",
                "This appears to be an Unreal Engine game.\n\n" +
                "ZModManager is designed for Unity games. Unreal Engine modding uses completely different approaches " +
                "(UE4SS, PAK mods, Blueprint mods) which are not supported by ZModManager.\n\n" +
                "You can still add this game to ZModManager to organise it, but injection will not work.",
                RuntimeType.Unknown);
        }

        // ── Godot ───────────────────────────────────────────────────────────
        if (Directory.GetFiles(dir, "*.pck").Length > 0 ||
            Directory.GetFiles(dir, "*.gdpc").Length > 0)
        {
            return new EngineInfo(
                EngineType.Godot,
                "Godot Engine",
                "This appears to be a Godot Engine game.\n\n" +
                "ZModManager is designed for Unity games. Godot modding is done differently " +
                "(modifying PCK files or using Godot's built-in mod loader) and is not supported here.",
                RuntimeType.Unknown);
        }

        // ── Unknown ─────────────────────────────────────────────────────────
        return new EngineInfo(
            EngineType.Unknown,
            "Unknown Engine",
            "ZModManager couldn't identify the engine for this game.\n\n" +
            "The game will be added as 'Unknown runtime'. You won't be able to inject mods until the runtime " +
            "is detected. Try clicking ⟳ Detect after pointing to the correct game executable.",
            RuntimeType.Unknown);
    }
}
