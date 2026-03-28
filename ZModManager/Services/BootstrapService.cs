using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ZModManager.Models;

namespace ZModManager.Services;

/// <summary>
/// Manages the IL2CPP mod-framework lifecycle for a game:
/// <list type="bullet">
///   <item>Detecting which framework (MelonLoader / BepInEx / ZModBootstrap) is present.</item>
///   <item>Deploying / undeploying enabled mod DLLs to the framework's mod folder.</item>
///   <item>Installing / uninstalling ZModManager's own version.dll bootstrapper.</item>
/// </list>
/// </summary>
public class BootstrapService
{
    // ── Bootstrap filenames ────────────────────────────────────────────────────
    private const string BootstrapDll   = "version.dll";
    private const string ZModConfigDir  = "ZModManager";
    private const string ZModModsCfg    = "mods.cfg";
    private const string ZModMarkerFile = "zmodmanager.marker"; // distinguishes our dll from a real version.dll

    // ── Framework detection ───────────────────────────────────────────────────

    /// <summary>
    /// Inspects <paramref name="gameDirectory"/> and returns which IL2CPP framework
    /// is installed.
    /// <para>
    /// Priority: <b>MelonLoader → BepInEx → ZModBootstrap → None</b>.
    /// MelonLoader and BepInEx always win over our own bootstrap so that the
    /// user's existing framework is used instead of the fallback proxy.
    /// </para>
    /// </summary>
    public IL2CPPFramework Detect(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return IL2CPPFramework.None;

        // ── MelonLoader ───────────────────────────────────────────────────────
        // Presence of a non-empty MelonLoader/ folder is enough — the exact
        // internal layout changed across versions (v0.5, v0.6, net6/net35 splits).
        var mlDir = Path.Combine(gameDirectory, "MelonLoader");
        if (Directory.Exists(mlDir) && Directory.EnumerateFileSystemEntries(mlDir).Any())
            return IL2CPPFramework.MelonLoader;

        // ── BepInEx ───────────────────────────────────────────────────────────
        var bepCore = Path.Combine(gameDirectory, "BepInEx", "core");
        if (Directory.Exists(bepCore) && Directory.GetFiles(bepCore, "BepInEx*.dll").Length > 0)
            return IL2CPPFramework.BepInEx;

        // ── ZModBootstrap (our own proxy — last resort) ───────────────────────
        if (File.Exists(Path.Combine(gameDirectory, ZModConfigDir, ZModMarkerFile)))
            return IL2CPPFramework.ZModBootstrap;

        return IL2CPPFramework.None;
    }

    // ── Mod deployment ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the folder where <paramref name="framework"/> expects mod DLLs to live.
    /// Returns <c>null</c> for <see cref="IL2CPPFramework.None"/>.
    /// </summary>
    public string? GetModsFolder(string gameDirectory, IL2CPPFramework framework) => framework switch
    {
        IL2CPPFramework.MelonLoader   => Path.Combine(gameDirectory, "Mods"),
        IL2CPPFramework.BepInEx       => Path.Combine(gameDirectory, "BepInEx", "plugins"),
        IL2CPPFramework.ZModBootstrap => null, // config-driven, not a folder
        _                             => null,
    };

    /// <summary>
    /// Synchronises the framework's mod folder with the profile's mod list:
    /// <list type="bullet">
    ///   <item>Enabled mods are copied in (overwriting stale copies).</item>
    ///   <item>Disabled mods are deleted from the folder.</item>
    ///   <item>Files not tracked by ZModManager are left untouched.</item>
    /// </list>
    /// This is the primary deployment method — call it before every game launch.
    /// </summary>
    public void SyncMods(string gameDirectory, IL2CPPFramework framework,
        System.Collections.Generic.IEnumerable<ModEntry> allMods)
    {
        var folder = GetModsFolder(gameDirectory, framework);
        if (folder == null) return;

        foreach (var mod in allMods.Where(m => m.RuntimeType == RuntimeType.IL2CPP))
        {
            var src = mod.IL2CPPConfig?.NativeDllPath;
            if (string.IsNullOrWhiteSpace(src)) continue;

            var dest = Path.Combine(folder, Path.GetFileName(src));

            if (mod.IsEnabled)
            {
                if (!File.Exists(src)) continue;   // source missing — skip
                Directory.CreateDirectory(folder);
                File.Copy(src, dest, overwrite: true);
            }
            else
            {
                if (File.Exists(dest)) File.Delete(dest);
            }
        }
    }

    /// <summary>
    /// Removes every mod that ZModManager placed in the framework folder,
    /// regardless of enabled/disabled state. Used by "Launch without mods".
    /// Files not in <paramref name="allMods"/> are never touched.
    /// </summary>
    public void RemoveDeployedMods(string gameDirectory, IL2CPPFramework framework,
        System.Collections.Generic.IEnumerable<ModEntry> allMods)
    {
        var folder = GetModsFolder(gameDirectory, framework);
        if (folder == null || !Directory.Exists(folder)) return;

        foreach (var mod in allMods.Where(m => m.RuntimeType == RuntimeType.IL2CPP))
        {
            var src = mod.IL2CPPConfig?.NativeDllPath;
            if (string.IsNullOrWhiteSpace(src)) continue;

            var dest = Path.Combine(folder, Path.GetFileName(src));
            if (File.Exists(dest)) File.Delete(dest);
        }
    }

    /// <summary>
    /// Copies <paramref name="sourceDllPath"/> into the framework's mod folder.
    /// Prefer <see cref="SyncMods"/> for launch-time deployment.
    /// </summary>
    public string? DeployMod(string gameDirectory, IL2CPPFramework framework, string sourceDllPath)
    {
        var folder = GetModsFolder(gameDirectory, framework);
        if (folder == null || string.IsNullOrWhiteSpace(sourceDllPath) || !File.Exists(sourceDllPath))
            return null;

        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, Path.GetFileName(sourceDllPath));
        File.Copy(sourceDllPath, dest, overwrite: true);
        return dest;
    }

    /// <summary>Removes a single mod DLL from the framework folder if present.</summary>
    public void UndeployMod(string gameDirectory, IL2CPPFramework framework, string sourceDllPath)
    {
        var folder = GetModsFolder(gameDirectory, framework);
        if (folder == null || string.IsNullOrWhiteSpace(sourceDllPath)) return;

        var dest = Path.Combine(folder, Path.GetFileName(sourceDllPath));
        if (File.Exists(dest)) File.Delete(dest);
    }

    // ── ZModBootstrap config ──────────────────────────────────────────────────

    /// <summary>
    /// Writes <c>ZModManager/mods.cfg</c> with absolute paths to every enabled
    /// mod DLL.  Called before each game launch when the ZModBootstrap framework
    /// is in use.
    /// </summary>
    public void WriteBootstrapConfig(string gameDirectory, GameProfile profile)
    {
        var cfgDir = Path.Combine(gameDirectory, ZModConfigDir);
        Directory.CreateDirectory(cfgDir);

        var lines = profile.Mods
            .Where(m => m.IsEnabled
                     && m.RuntimeType == RuntimeType.IL2CPP
                     && !string.IsNullOrWhiteSpace(m.IL2CPPConfig?.NativeDllPath)
                     && File.Exists(m.IL2CPPConfig!.NativeDllPath))
            .Select(m => Path.GetFullPath(m.IL2CPPConfig!.NativeDllPath));

        File.WriteAllLines(Path.Combine(cfgDir, ZModModsCfg), lines);
    }

    // ── Bootstrap install / uninstall ─────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if our version.dll bootstrapper is already installed
    /// in <paramref name="gameDirectory"/>.
    /// </summary>
    public bool IsInstalled(string gameDirectory)
        => File.Exists(Path.Combine(gameDirectory, ZModConfigDir, ZModMarkerFile));

    /// <summary>
    /// Installs the ZModManager version.dll bootstrapper into <paramref name="gameDirectory"/>.
    /// The compiled bootstrapper DLL is loaded from the embedded resource
    /// <c>ZModManager.Resources.Bootstrap.version.dll</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the embedded bootstrapper resource is not found (the native project
    /// has not been compiled yet — see ZModManager.Bootstrap/).
    /// </exception>
    public void Install(string gameDirectory)
    {
        var bootstrapBytes = GetEmbeddedBootstrap();

        // Back up any existing version.dll that isn't ours
        var dllDest = Path.Combine(gameDirectory, BootstrapDll);
        if (File.Exists(dllDest) && !IsInstalled(gameDirectory))
        {
            // Rename the original so it can be restored on uninstall
            File.Move(dllDest, dllDest + ".zmm_backup", overwrite: true);
        }

        File.WriteAllBytes(dllDest, bootstrapBytes);

        // Create the config directory + marker
        var cfgDir = Path.Combine(gameDirectory, ZModConfigDir);
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, ZModMarkerFile),
            $"ZModManager bootstrap — installed {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // Write an empty config so the game doesn't fail to open it
        var cfgPath = Path.Combine(cfgDir, ZModModsCfg);
        if (!File.Exists(cfgPath))
            File.WriteAllText(cfgPath, "# ZModManager mod list — one absolute DLL path per line\n");
    }

    /// <summary>
    /// Removes the ZModManager bootstrapper from <paramref name="gameDirectory"/> and
    /// restores any original version.dll that was backed up during install.
    /// </summary>
    public void Uninstall(string gameDirectory)
    {
        var dllDest  = Path.Combine(gameDirectory, BootstrapDll);
        var cfgDir   = Path.Combine(gameDirectory, ZModConfigDir);
        var marker   = Path.Combine(cfgDir, ZModMarkerFile);
        var backup   = dllDest + ".zmm_backup";
        var cfgFile  = Path.Combine(cfgDir, ZModModsCfg);

        // Remove our DLL
        if (File.Exists(dllDest)) File.Delete(dllDest);

        // Restore backup if present
        if (File.Exists(backup)) File.Move(backup, dllDest, overwrite: false);

        // Remove our files
        if (File.Exists(marker))  File.Delete(marker);
        if (File.Exists(cfgFile)) File.Delete(cfgFile);

        // Remove the config directory only if now empty
        if (Directory.Exists(cfgDir) && !Directory.EnumerateFileSystemEntries(cfgDir).Any())
            Directory.Delete(cfgDir);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static byte[] GetEmbeddedBootstrap()
    {
        var asm  = Assembly.GetExecutingAssembly();
        // Resource name mirrors the folder path: ZModManager.Resources.Bootstrap.version.dll
        const string resourceName = "ZModManager.Resources.Bootstrap.version.dll";

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException(
                "Bootstrapper binary not found.\n\n" +
                "Build the ZModManager.Bootstrap C++ project first:\n" +
                "  1. Open the solution in Visual Studio with the C++ workload installed.\n" +
                "  2. Build ZModManager.Bootstrap (Release | x64).\n" +
                "  3. Rebuild ZModManager to embed the compiled version.dll.\n\n" +
                "The compiled DLL should end up at:\n" +
                "  ZModManager/Resources/Bootstrap/version.dll");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
