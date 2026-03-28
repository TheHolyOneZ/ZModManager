using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace ZModManager.Injection;

/// <summary>
/// Injects a native DLL into a remote process via LoadLibraryW + CreateRemoteThread.
/// Verifies success by confirming the DLL appears in the module list post-injection.
///
/// Notes on exit-code verification:
///   LoadLibraryW returns HMODULE (64-bit pointer). When used as a thread function,
///   only the low 32 bits are captured by GetExitCodeThread. A high-address module
///   (>4 GB) would have low 32 bits = 0 even on success, so we do NOT use the exit
///   code as the primary success indicator. We use IsModuleLoaded instead, which
///   scans the real module list.  The exit code is logged as a diagnostic hint only.
/// </summary>
public static class DllInjector
{
    /// <summary>
    /// Inject <paramref name="dllPath"/> into <paramref name="processId"/>.
    /// Throws <see cref="InjectionException"/> on failure.
    /// </summary>
    public static void Inject(int processId, string dllPath, uint timeoutMs = 15_000)
    {
        if (!File.Exists(dllPath))
            throw new InjectionException($"DLL not found: {dllPath}");

        dllPath = Path.GetFullPath(dllPath);
        var dllName = Path.GetFileName(dllPath);

        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessFlags.All, false, processId);

        if (hProcess == IntPtr.Zero)
            throw new InjectionException(
                $"OpenProcess({processId}) failed (error {Marshal.GetLastWin32Error()}). " +
                "Try running ZModManager as Administrator.");

        uint exitCode = 0;
        try
        {
            using var mem = new ProcessMemory(hProcess);

            var remotePathPtr = mem.AllocateWideString(dllPath);

            var kernel32 = NativeMethods.GetModuleHandle("kernel32.dll");
            var loadLibW = NativeMethods.GetProcAddress(kernel32, "LoadLibraryW");

            if (loadLibW == IntPtr.Zero)
                throw new InjectionException("GetProcAddress(LoadLibraryW) failed.");

            var hThread = NativeMethods.CreateRemoteThread(
                hProcess, IntPtr.Zero, 0, loadLibW, remotePathPtr, 0, out _);

            if (hThread == IntPtr.Zero)
                throw new InjectionException(
                    $"CreateRemoteThread failed (error {Marshal.GetLastWin32Error()}). " +
                    "The game may be protected (anti-cheat) or ZModManager needs to run as Administrator.");

            try
            {
                uint wait = NativeMethods.WaitForSingleObject(hThread, timeoutMs);
                if (wait == NativeMethods.WAIT_TIMEOUT)
                    throw new InjectionException("Remote LoadLibraryW thread timed out.");

                NativeMethods.GetExitCodeThread(hThread, out exitCode);
            }
            finally
            {
                NativeMethods.CloseHandle(hThread);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }

        // Give Windows time to update the module table (retry up to ~1.5 s)
        bool loaded = false;
        for (int retry = 0; retry < 5 && !loaded; retry++)
        {
            Thread.Sleep(300);
            loaded = ModuleScanner.IsModuleLoaded(processId, dllName);
        }

        if (!loaded)
        {
            // exitCode low-32 == 0 is the strongest hint that LoadLibraryW returned NULL
            var hint = exitCode == 0
                ? "LoadLibraryW returned NULL — the DLL could not be loaded.\n" +
                  "  Common causes:\n" +
                  "  • The DLL or one of its dependencies is missing (use Dependency Walker / Dependencies tool to check)\n" +
                  "  • Architecture mismatch — the game is 64-bit but the DLL is 32-bit (or vice-versa)\n" +
                  "  • The game's anti-cheat blocked the load\n"
                : $"LoadLibraryW returned 0x{exitCode:X8} (low 32 bits) but the DLL was not found in module list.\n" +
                  "  The DLL may have loaded then immediately unloaded (DllMain returned FALSE or threw).\n";

            throw new InjectionException(
                $"[IL2CPP] '{dllName}' not found in module list after injection.\n{hint}");
        }
    }

    // ── Early injection (CREATE_SUSPENDED) ────────────────────────────────────

    /// <summary>
    /// Launches <paramref name="exePath"/> with its main thread suspended, injects each DLL
    /// in <paramref name="dllPaths"/> via <c>LoadLibraryW + CreateRemoteThread</c>, then
    /// resumes the main thread.  Because the game's own code has not yet executed a single
    /// instruction, the mod DLLs are present before any IL2CPP initialisation happens.
    /// </summary>
    /// <returns>The PID of the newly created process.</returns>
    /// <exception cref="InjectionException">Thrown if process creation or any injection fails.
    /// If a mid-injection failure occurs the process is terminated before the exception propagates.</exception>
    public static int InjectAtStart(string exePath, string workingDirectory, string[] dllPaths)
    {
        if (!File.Exists(exePath))
            throw new InjectionException($"Executable not found: {exePath}");

        var si = new NativeMethods.STARTUPINFOW
        {
            cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOW>()
        };

        bool created = NativeMethods.CreateProcessW(
            exePath, null,
            IntPtr.Zero, IntPtr.Zero,
            false,
            NativeMethods.CREATE_SUSPENDED,
            IntPtr.Zero,
            workingDirectory,
            ref si,
            out var pi);

        if (!created)
            throw new InjectionException(
                $"CreateProcessW failed (error {Marshal.GetLastWin32Error()}). " +
                "Try running ZModManager as Administrator.");

        try
        {
            foreach (var dllPath in dllPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
                InjectIntoHandle(pi.hProcess, dllPath);
        }
        catch
        {
            // Leave no zombie — kill the suspended process before re-throwing
            NativeMethods.TerminateProcess(pi.hProcess, 1);
            NativeMethods.CloseHandle(pi.hThread);
            NativeMethods.CloseHandle(pi.hProcess);
            throw;
        }

        NativeMethods.ResumeThread(pi.hThread);
        NativeMethods.CloseHandle(pi.hThread);
        NativeMethods.CloseHandle(pi.hProcess);

        return (int)pi.dwProcessId;
    }

    /// <summary>
    /// Injects <paramref name="dllPath"/> into an already-open process handle using
    /// <c>LoadLibraryW + CreateRemoteThread</c>.  Used internally by both
    /// <see cref="Inject"/> (post-launch) and <see cref="InjectAtStart"/> (pre-resume).
    /// </summary>
    private static void InjectIntoHandle(IntPtr hProcess, string dllPath)
    {
        if (!File.Exists(dllPath))
            throw new InjectionException($"DLL not found: {dllPath}");

        dllPath = Path.GetFullPath(dllPath);

        using var mem = new ProcessMemory(hProcess);
        var remotePathPtr = mem.AllocateWideString(dllPath);

        var kernel32 = NativeMethods.GetModuleHandle("kernel32.dll");
        var loadLibW  = NativeMethods.GetProcAddress(kernel32, "LoadLibraryW");

        if (loadLibW == IntPtr.Zero)
            throw new InjectionException("GetProcAddress(LoadLibraryW) failed.");

        var hThread = NativeMethods.CreateRemoteThread(
            hProcess, IntPtr.Zero, 0, loadLibW, remotePathPtr, 0, out _);

        if (hThread == IntPtr.Zero)
            throw new InjectionException(
                $"CreateRemoteThread failed (error {Marshal.GetLastWin32Error()}). " +
                "The game may be protected or ZModManager needs Administrator.");

        try
        {
            uint wait = NativeMethods.WaitForSingleObject(hThread, 15_000);
            if (wait == NativeMethods.WAIT_TIMEOUT)
                throw new InjectionException("Remote LoadLibraryW thread timed out.");
        }
        finally
        {
            NativeMethods.CloseHandle(hThread);
        }
    }
}
