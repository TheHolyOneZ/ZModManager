using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZModManager.Injection;
using ZModManager.Models;

namespace ZModManager.Services;

public class LaunchAndInjectService
{
    private readonly MonoInjectionService _monoSvc;
    private readonly BootstrapService     _bootstrap;
    private readonly LogService           _log;

    // ── Timeouts ──────────────────────────────────────────────────────────────
    private const int PollIntervalMs         = 500;

    // How long to wait for the OS process to appear after clicking Launch
    private const int ProcessTimeoutMs       = 30_000;

    // How long to wait for a visible, non-trivial game window (≥ 200 × 150 px)
    // Unity games show a window only once the engine is past early boot
    private const int WindowTimeoutMs        = 120_000;

    // Minimum window size that counts as "the real game window" (not a tiny
    // splash, config dialog or invisible helper window)
    private const int MinWindowWidth         = 200;
    private const int MinWindowHeight        = 150;

    // Mono: how long to wait for mono.dll to appear after window is visible
    private const int MonoModuleTimeoutMs    = 30_000;

    // Mono: extra stabilisation delay after mono.dll is detected so the domain
    // and game assemblies finish loading before we call mono_domain_assembly_open
    private const int MonoStabilizeMs        = 4_000;

    public LaunchAndInjectService(MonoInjectionService mono, IL2CPPInjectionService il2,
        LogService log, BootstrapService bootstrap)
    {
        _monoSvc   = mono;
        _bootstrap = bootstrap;
        _log       = log;
    }

    // ── Main entry point ──────────────────────────────────────────────────────

    public async Task<List<InjectionResult>> LaunchAndInjectAsync(
        GameProfile profile, CancellationToken ct = default)
    {
        var results = new List<InjectionResult>();
        var enabled = profile.Mods.Where(m => m.IsEnabled).ToList();

        if (enabled.Count == 0)
        {
            _log.Warn("No enabled mods — nothing to inject.");
            return results;
        }

        // ── IL2CPP path ───────────────────────────────────────────────────────
        //
        // IL2CPP mods are NOT native DLLs that can be LoadLibraryW'd at runtime.
        // They are .NET assemblies that need a host framework (MelonLoader, BepInEx,
        // or our own bootstrapper) to load them.  The correct approach is:
        //   • Detect which framework is in the game directory.
        //   • Deploy each mod to the framework's mod folder (or write the config
        //     for ZModBootstrap).
        //   • Launch the game normally — the framework picks them up on startup.
        if (profile.RuntimeType == RuntimeType.IL2CPP)
        {
            var gameDir   = profile.GameDirectory;
            var framework = _bootstrap.Detect(gameDir);

            _log.Info($"[IL2CPP] Game directory : {gameDir}");
            _log.Info($"[IL2CPP] Framework      : {framework}");

            if (framework == IL2CPPFramework.None)
            {
                _log.Error("[IL2CPP] No framework detected. " +
                    "Install MelonLoader/BepInEx, or click 'Install Bootstrap'.");
                foreach (var mod in enabled)
                    results.Add(InjectionResult.Fail(
                        $"[{mod.Name}] No IL2CPP framework found in game directory.", RuntimeType.IL2CPP));
                return results;
            }

            // ── Sync mods to the framework folder ─────────────────────────────
            // Enabled mods are copied in; disabled mods are removed.
            // This ensures the framework folder exactly matches the enabled set.
            if (framework == IL2CPPFramework.ZModBootstrap)
            {
                // Bootstrap is config-driven — write the list of enabled mods.
                try
                {
                    _bootstrap.WriteBootstrapConfig(gameDir, profile);
                    _log.Info("[IL2CPP] Bootstrap config written.");
                }
                catch (Exception ex)
                {
                    _log.Error($"[IL2CPP] Failed to write bootstrap config: {ex.Message}");
                }

                foreach (var mod in enabled)
                {
                    _log.Info($"[IL2CPP] '{mod.Name}' queued in bootstrap config.");
                    results.Add(InjectionResult.Ok(
                        $"[{mod.Name}] Queued for ZModBootstrap.", RuntimeType.IL2CPP));
                }
            }
            else
            {
                // MelonLoader / BepInEx: sync the mods folder.
                // Enabled → copy; disabled → delete. Only files ZModManager owns are touched.
                _log.Info($"[IL2CPP] Syncing mods folder for {framework}…");
                foreach (var mod in profile.Mods.Where(m => m.RuntimeType == RuntimeType.IL2CPP))
                {
                    var dllPath = mod.IL2CPPConfig?.NativeDllPath;
                    if (string.IsNullOrWhiteSpace(dllPath)) continue;

                    var folder   = _bootstrap.GetModsFolder(gameDir, framework)!;
                    var destName = Path.GetFileName(dllPath);
                    var dest     = Path.Combine(folder, destName);

                    if (mod.IsEnabled)
                    {
                        if (!File.Exists(dllPath))
                        {
                            _log.Error($"[IL2CPP] '{mod.Name}' — file not found: {dllPath}");
                            results.Add(InjectionResult.Fail(
                                $"[{mod.Name}] File not found: {dllPath}", RuntimeType.IL2CPP));
                        }
                        else
                        {
                            // ── Compatibility gate ─────────────────────────────────────────────
                            var modTarget     = ModFrameworkDetector.Detect(dllPath);
                            var incompatReason = GetIncompatibilityReason(modTarget, framework);
                            if (incompatReason != null)
                            {
                                _log.Warn($"[IL2CPP] Skipping '{mod.Name}': {incompatReason}");
                                results.Add(InjectionResult.Fail(
                                    $"[{mod.Name}] Skipped — {incompatReason}", RuntimeType.IL2CPP));
                            }
                            else
                            {
                                try
                                {
                                    Directory.CreateDirectory(folder);
                                    File.Copy(dllPath, dest, overwrite: true);
                                    _log.Success($"[IL2CPP] '{mod.Name}' → {dest}");
                                    results.Add(InjectionResult.Ok(
                                        $"[{mod.Name}] Deployed to {framework} folder.", RuntimeType.IL2CPP));
                                }
                                catch (Exception ex)
                                {
                                    _log.Error($"[IL2CPP] Deploy failed for '{mod.Name}': {ex.Message}");
                                    results.Add(InjectionResult.Fail(
                                        $"[{mod.Name}] {ex.Message}", RuntimeType.IL2CPP));
                                }
                            }
                        }
                    }
                    else
                    {
                        // Disabled — remove from folder if it was previously deployed
                        if (File.Exists(dest))
                        {
                            try
                            {
                                File.Delete(dest);
                                _log.Info($"[IL2CPP] Removed disabled mod '{mod.Name}' from {framework} folder.");
                            }
                            catch (Exception ex)
                            {
                                _log.Warn($"[IL2CPP] Could not remove '{destName}': {ex.Message}");
                            }
                        }
                    }
                }
            }

            // Launch the game — the framework handles loading on startup
            _log.Info($"[IL2CPP] Launching '{profile.GameExePath}'…");
            _log.Info($"[IL2CPP] {framework} will load mods on startup.");
            try
            {
                Process.Start(new ProcessStartInfo(profile.GameExePath)
                {
                    UseShellExecute  = true,
                    WorkingDirectory = gameDir,
                    Arguments        = profile.LaunchArgs
                });
                _log.Info($"[IL2CPP] Game process started.");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to launch: {ex.Message}");
            }

            int okIl2 = results.Count(r => r.Success);
            _log.Info($"Done — {okIl2}/{enabled.Count} mods deployed.");
            return results;
        }

        // ── Mono path: launch normally, wait for window + runtime ─────────────

        // 1. Launch
        _log.Info($"Launching '{profile.Name}'…");
        try
        {
            Process.Start(new ProcessStartInfo(profile.GameExePath)
            {
                UseShellExecute  = true,
                WorkingDirectory = profile.GameDirectory,
                Arguments        = profile.LaunchArgs
            });
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to start process: {ex.Message}");
            return results;
        }

        // 2. Find the process
        _log.Info("Waiting for game process…");
        var process = await WaitForProcessAsync(profile.ProcessName, ProcessTimeoutMs, ct);
        if (process == null)
        {
            _log.Error("Game process not found within 30 s. Aborting.");
            return results;
        }
        int monoP = process.Id;
        _log.Info($"Process found — PID {monoP}.");

        // 3. Wait for a real game window
        // The PID exists long before Unity's window is rendered.  Injecting
        // before the window is visible means the engine is still in its C++
        // bootstrap — far too early for the Mono domain to be ready.
        _log.Info("Waiting for game window to appear…");
        bool windowReady = await WaitForWindowAsync(monoP, WindowTimeoutMs, ct);
        if (ct.IsCancellationRequested) return results;

        if (!windowReady)
            _log.Warn("Game window not detected within timeout — continuing anyway.");
        else
            _log.Info("Game window visible.");

        // 4. Wait for mono.dll, then stabilise so the domain + Assembly-CSharp finish loading
        _log.Info("Waiting for Mono runtime module…");
        bool monoReady = await WaitForMonoAsync(monoP, MonoModuleTimeoutMs, ct);
        if (ct.IsCancellationRequested) return results;

        if (!monoReady)
        {
            _log.Warn("mono.dll not detected — injecting anyway.");
        }
        else
        {
            _log.Info($"mono.dll loaded — stabilising for {MonoStabilizeMs / 1000}s…");
            try { await Task.Delay(MonoStabilizeMs, ct); }
            catch (OperationCanceledException) { return results; }
            _log.Info("Mono domain ready. Injecting…");
        }

        // 5. Inject all enabled Mono mods
        _log.Info($"Injecting {enabled.Count} mod(s)…");
        foreach (var mod in enabled)
        {
            if (ct.IsCancellationRequested) break;
            var result = await _monoSvc.InjectAsync(profile, mod);
            results.Add(result);
        }

        int ok = results.Count(r => r.Success);
        _log.Info($"Done — {ok}/{enabled.Count} mods injected successfully.");
        return results;
    }

    // ── Wait helpers ──────────────────────────────────────────────────────────

    /// <summary>Polls until a process with <paramref name="processName"/> appears.</summary>
    private static async Task<Process?> WaitForProcessAsync(
        string processName, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length > 0) return procs[0];
            try { await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }
        }
        return null;
    }

    /// <summary>
    /// Polls until the process has at least one visible window with a client
    /// area ≥ <see cref="MinWindowWidth"/> × <see cref="MinWindowHeight"/>.
    /// This ensures the Unity engine has finished its early-boot C++ phase and
    /// is actually rendering frames — a far more reliable injection gate than
    /// any fixed time delay.
    /// </summary>
    private static async Task<bool> WaitForWindowAsync(
        int pid, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            bool found = false;

            NativeMethods.EnumWindows((hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint windowPid);
                if ((int)windowPid != pid) return true; // not our process

                if (!NativeMethods.IsWindowVisible(hwnd)) return true;

                NativeMethods.GetWindowRect(hwnd, out var rect);
                if (rect.Width >= MinWindowWidth && rect.Height >= MinWindowHeight)
                {
                    found = true;
                    return false; // stop enumeration
                }
                return true;
            }, IntPtr.Zero);

            if (found) return true;

            try { await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    /// <summary>Polls until mono.dll (or mono-2.0-bdwgc.dll) appears in <paramref name="pid"/>.</summary>
    private static async Task<bool> WaitForMonoAsync(
        int pid, int timeoutMs, CancellationToken ct)
    {
        string[] candidates = { "mono.dll", "mono-2.0-bdwgc.dll", "mono-2.0.dll" };
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var (b, _) = ModuleScanner.FindModule(pid, candidates);
            if (b != IntPtr.Zero) return true;
            try { await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return false; }
        }
        return false;
    }

    // ── Compatibility helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns a human-readable reason string when <paramref name="modTarget"/> is
    /// incompatible with <paramref name="framework"/>, or <c>null</c> when the mod
    /// can be deployed.
    /// </summary>
    private static string? GetIncompatibilityReason(
        DetectedModFramework modTarget, IL2CPPFramework framework)
        => (modTarget, framework) switch
        {
            (DetectedModFramework.MelonLoader, IL2CPPFramework.BepInEx)
                => "mod requires MelonLoader but BepInEx is installed",

            (DetectedModFramework.BepInEx, IL2CPPFramework.MelonLoader)
                => "mod is a BepInEx plugin but MelonLoader is installed",

            (DetectedModFramework.Native, IL2CPPFramework.MelonLoader)
                => "native DLL cannot be loaded by MelonLoader — use ZMod Bootstrap instead",

            (DetectedModFramework.Native, IL2CPPFramework.BepInEx)
                => "native DLL cannot be loaded by BepInEx — use ZMod Bootstrap instead",

            (DetectedModFramework.Managed, IL2CPPFramework.ZModBootstrap)
                => "managed .NET mod requires MelonLoader or BepInEx, not ZMod Bootstrap",

            _ => null   // compatible or unknown — allow deploy
        };

}
