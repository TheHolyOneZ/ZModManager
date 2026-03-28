using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ZModManager.Injection;

/// <summary>
/// Scans a remote process's module list.
/// Uses CreateToolhelp32Snapshot (most reliable) with an EnumProcessModulesEx fallback.
/// </summary>
public static class ModuleScanner
{
    /// <summary>
    /// Find a module in <paramref name="processId"/> whose filename (case-insensitive)
    /// matches any of <paramref name="candidates"/>.
    /// Returns (baseAddress, fullPath), or (Zero, null) if not found.
    /// </summary>
    public static (IntPtr Base, string? Path) FindModule(int processId, params string[] candidates)
    {
        // ── Primary: Toolhelp32 snapshot ─────────────────────────────────────
        var result = FindModuleToolhelp(processId, candidates);
        if (result.Base != IntPtr.Zero) return result;

        // ── Fallback: psapi EnumProcessModulesEx ─────────────────────────────
        return FindModulePsapi(processId, candidates);
    }

    private static (IntPtr Base, string? Path) FindModuleToolhelp(int processId, string[] candidates)
    {
        // TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32 to catch both bitnesses
        var hSnap = NativeMethods.CreateToolhelp32Snapshot(
            NativeMethods.TH32CS_SNAPMODULE | NativeMethods.TH32CS_SNAPMODULE32,
            (uint)processId);

        if (hSnap == IntPtr.Zero || hSnap == new IntPtr(-1))
            return (IntPtr.Zero, null);

        try
        {
            var entry = new NativeMethods.MODULEENTRY32W
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.MODULEENTRY32W>()
            };

            if (!NativeMethods.Module32FirstW(hSnap, ref entry))
                return (IntPtr.Zero, null);

            do
            {
                foreach (var c in candidates)
                {
                    if (entry.szModule.Equals(c, StringComparison.OrdinalIgnoreCase))
                        return (entry.modBaseAddr, entry.szExePath);
                }
            } while (NativeMethods.Module32NextW(hSnap, ref entry));

            return (IntPtr.Zero, null);
        }
        finally
        {
            NativeMethods.CloseHandle(hSnap);
        }
    }

    private static (IntPtr Base, string? Path) FindModulePsapi(int processId, string[] candidates)
    {
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessFlags.QueryInformation | NativeMethods.ProcessAccessFlags.VmRead,
            false, processId);

        if (hProcess == IntPtr.Zero) return (IntPtr.Zero, null);

        try
        {
            const int MAX = 1024;
            var handles = new IntPtr[MAX];

            if (!NativeMethods.EnumProcessModulesEx(hProcess, handles,
                    (uint)(IntPtr.Size * MAX), out uint needed, NativeMethods.LIST_MODULES_ALL))
                return (IntPtr.Zero, null);

            int count     = (int)(needed / (uint)IntPtr.Size);
            var nameBuf   = new char[512];

            for (int i = 0; i < count; i++)
            {
                uint len = NativeMethods.GetModuleFileNameExW(hProcess, handles[i], nameBuf, (uint)nameBuf.Length);
                if (len == 0) continue;

                var fullPath = new string(nameBuf, 0, (int)len);
                var fileName = Path.GetFileName(fullPath);

                foreach (var c in candidates)
                {
                    if (fileName.Equals(c, StringComparison.OrdinalIgnoreCase))
                        return (handles[i], fullPath);
                }
            }

            return (IntPtr.Zero, null);
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Check if a specific DLL filename is present in the target process's module list.
    /// Used to verify successful injection.
    /// </summary>
    public static bool IsModuleLoaded(int processId, string dllFileName)
    {
        var (b, _) = FindModule(processId, dllFileName);
        return b != IntPtr.Zero;
    }

    /// <summary>
    /// Resolve the remote address of an exported function.
    /// Loads <paramref name="localDllPath"/> into THIS process, reads the export offset,
    /// then applies it to <paramref name="remoteModuleBase"/>.
    /// </summary>
    public static IntPtr GetRemoteProcAddress(IntPtr remoteModuleBase,
        string localDllPath, string exportName)
    {
        // Try GetModuleHandle first (already loaded locally)
        var hLocal = NativeMethods.GetModuleHandle(localDllPath);
        if (hLocal == IntPtr.Zero)
            hLocal = LoadLibraryW(localDllPath);

        if (hLocal == IntPtr.Zero)
            throw new InjectionException(
                $"Could not load '{Path.GetFileName(localDllPath)}' locally to resolve exports. " +
                "Ensure the mono DLL path is correct.");

        var localProc = NativeMethods.GetProcAddress(hLocal, exportName);
        if (localProc == IntPtr.Zero)
            throw new InjectionException(
                $"Export '{exportName}' not found in '{Path.GetFileName(localDllPath)}'.");

        long offset = localProc.ToInt64() - hLocal.ToInt64();
        return new IntPtr(remoteModuleBase.ToInt64() + offset);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpLibFileName);
}
