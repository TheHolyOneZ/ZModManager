using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZModManager.Injection;
using ZModManager.Models;

namespace ZModManager.Services;

public class IL2CPPInjectionService
{
    private readonly LogService _log;

    public IL2CPPInjectionService(LogService log) => _log = log;

    public async Task<InjectionResult> InjectAsync(GameProfile profile, ModEntry mod)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (mod.IL2CPPConfig == null)
                    return InjectionResult.Fail("IL2CPP config not set on this mod.", RuntimeType.IL2CPP);

                var dllPath = mod.IL2CPPConfig.NativeDllPath;
                if (string.IsNullOrWhiteSpace(dllPath))
                    return InjectionResult.Fail($"[{mod.Name}] DLL path is empty.", RuntimeType.IL2CPP);
                if (!File.Exists(dllPath))
                    return InjectionResult.Fail($"[{mod.Name}] DLL not found: {dllPath}", RuntimeType.IL2CPP);

                var process = FindProcess(profile.ProcessName);
                if (process == null)
                    return InjectionResult.Fail(
                        $"Process '{profile.ProcessName}' not found. Launch the game first.",
                        RuntimeType.IL2CPP);

                _log.Info($"[IL2CPP] Injecting '{mod.Name}' ({Path.GetFileName(dllPath)}) into PID {process.Id}…");
                DllInjector.Inject(process.Id, Path.GetFullPath(dllPath));

                var msg = $"[IL2CPP] '{mod.Name}' — {Path.GetFileName(dllPath)} loaded.";
                _log.Success(msg);
                return InjectionResult.Ok(msg, RuntimeType.IL2CPP);
            }
            catch (InjectionException ex)
            {
                _log.Error($"[IL2CPP] {ex}");
                return InjectionResult.Fail(ex.ToString(), RuntimeType.IL2CPP);
            }
            catch (Exception ex)
            {
                _log.Error($"[IL2CPP] {ex.Message}");
                return InjectionResult.Fail(ex.Message, RuntimeType.IL2CPP);
            }
        });
    }

    private static Process? FindProcess(string name)
        => Process.GetProcessesByName(name).FirstOrDefault();
}
