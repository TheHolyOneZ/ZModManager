using System;
using System.Runtime.InteropServices;

namespace ZModManager.Injection;

internal static class NativeMethods
{
    // ── Process access ────────────────────────────────────────────────────────
    [Flags]
    public enum ProcessAccessFlags : uint
    {
        All                     = 0x001F0FFF,
        CreateThread            = 0x00000002,
        VmOperation             = 0x00000008,
        VmRead                  = 0x00000010,
        VmWrite                 = 0x00000020,
        QueryInformation        = 0x00000400,
        QueryLimitedInformation = 0x00001000
    }

    // ── Memory allocation types ───────────────────────────────────────────────
    [Flags]
    public enum AllocationType : uint
    {
        Commit  = 0x1000,
        Reserve = 0x2000,
        Release = 0x8000
    }

    // ── Memory protection ─────────────────────────────────────────────────────
    [Flags]
    public enum MemoryProtection : uint
    {
        NoAccess         = 0x01,
        ReadOnly         = 0x02,
        ReadWrite        = 0x04,
        WriteCopy        = 0x08,
        Execute          = 0x10,
        ExecuteRead      = 0x20,
        ExecuteReadWrite = 0x40,  // ← needed for shellcode pages
        PageGuard        = 0x100
    }

    // ── Core P/Invoke ─────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(ProcessAccessFlags dwDesiredAccess,
        bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
        nuint dwSize, AllocationType flAllocationType, MemoryProtection flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress,
        nuint dwSize, AllocationType dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, nuint nSize, out nuint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateRemoteThread(IntPtr hProcess,
        IntPtr lpThreadAttributes, nuint dwStackSize, IntPtr lpStartAddress,
        IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ── Toolhelp32 — most reliable cross-scenario module enumeration ──────────

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    public const uint TH32CS_SNAPMODULE   = 0x00000008;
    public const uint TH32CS_SNAPMODULE32 = 0x00000010; // also include 32-bit modules

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MODULEENTRY32W
    {
        public uint   dwSize;
        public uint   th32ModuleID;
        public uint   th32ProcessID;
        public uint   GlblcntUsage;
        public uint   ProccntUsage;
        public IntPtr modBaseAddr;
        public uint   modBaseSize;
        public IntPtr hModule;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    // ── Psapi fallback ────────────────────────────────────────────────────────
    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EnumProcessModulesEx(IntPtr hProcess,
        [Out] IntPtr[] lphModule, uint cb, out uint lpcbNeeded, uint dwFilterFlag);

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern uint GetModuleFileNameExW(IntPtr hProcess, IntPtr hModule,
        [Out] char[] lpFilename, uint nSize);

    public const uint WAIT_OBJECT_0    = 0x00000000;
    public const uint WAIT_TIMEOUT     = 0x00000102;
    public const uint INFINITE         = 0xFFFFFFFF;
    public const uint LIST_MODULES_ALL = 0x03;
    public const IntPtr INVALID_HANDLE = -1;

    // ── Window detection (user32.dll) ─────────────────────────────────────────
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width  => Right  - Left;
        public int Height => Bottom - Top;
    }

    // ── Process creation ──────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        public uint   cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public uint   dwX, dwY, dwXSize, dwYSize;
        public uint   dwXCountChars, dwYCountChars;
        public uint   dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint   dwProcessId;
        public uint   dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(
        string?  lpApplicationName,
        string?  lpCommandLine,
        IntPtr   lpProcessAttributes,
        IntPtr   lpThreadAttributes,
        bool     bInheritHandles,
        uint     dwCreationFlags,
        IntPtr   lpEnvironment,
        string?  lpCurrentDirectory,
        ref STARTUPINFOW          lpStartupInfo,
        out PROCESS_INFORMATION   lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(IntPtr hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    public const uint CREATE_SUSPENDED = 0x00000004;
}
