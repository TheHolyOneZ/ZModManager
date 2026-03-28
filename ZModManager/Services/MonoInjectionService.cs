using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ZModManager.Injection;
using ZModManager.Models;

namespace ZModManager.Services;

public class MonoInjectionService
{
    private readonly LogService _log;
    private readonly MonoInjector _injector = new();

    public MonoInjectionService(LogService log) => _log = log;

    public async Task<InjectionResult> InjectAsync(GameProfile profile, ModEntry mod)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (mod.MonoConfig == null)
                    return InjectionResult.Fail("Mono config is not set on this mod.", RuntimeType.Mono);

                var cfg = mod.MonoConfig;
                if (string.IsNullOrWhiteSpace(cfg.AssemblyPath))
                    return InjectionResult.Fail($"[{mod.Name}] Assembly path is empty.", RuntimeType.Mono);
                if (string.IsNullOrWhiteSpace(cfg.ClassName))
                    return InjectionResult.Fail($"[{mod.Name}] Class name is empty.", RuntimeType.Mono);
                if (string.IsNullOrWhiteSpace(cfg.MethodName))
                    return InjectionResult.Fail($"[{mod.Name}] Method name is empty.", RuntimeType.Mono);

                var process = FindProcess(profile.ProcessName);
                if (process == null)
                    return InjectionResult.Fail(
                        $"Process '{profile.ProcessName}' not found. Launch the game first.",
                        RuntimeType.Mono);

                _log.Info($"[Mono] Injecting '{mod.Name}' into PID {process.Id}…");
                _injector.Inject(process.Id, profile.GameDirectory, cfg);

                var msg = $"[Mono] '{mod.Name}' → {cfg.Namespace}.{cfg.ClassName}.{cfg.MethodName}()";
                _log.Success(msg);
                return InjectionResult.Ok(msg, RuntimeType.Mono);
            }
            catch (InjectionException ex)
            {
                _log.Error($"[Mono] {ex}");
                return InjectionResult.Fail(ex.ToString(), RuntimeType.Mono);
            }
            catch (Exception ex)
            {
                _log.Error($"[Mono] {ex.Message}");
                return InjectionResult.Fail(ex.Message, RuntimeType.Mono);
            }
        });
    }

    private static Process? FindProcess(string name)
        => Process.GetProcessesByName(name).FirstOrDefault();
}
