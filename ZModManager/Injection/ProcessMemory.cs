using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ZModManager.Injection;

/// <summary>
/// Managed wrapper around Windows process-memory APIs.
/// Tracks remote allocations and frees them on Dispose.
/// </summary>
public sealed class ProcessMemory : IDisposable
{
    private readonly IntPtr _hProcess;
    private readonly List<IntPtr> _allocations = new();
    private bool _disposed;

    public ProcessMemory(IntPtr hProcess) => _hProcess = hProcess;

    // ── Allocation helpers ────────────────────────────────────────────────────

    /// <summary>Allocate RW memory (for data — strings, structs).</summary>
    public IntPtr Allocate(int size) => AllocateWithProtect(size,
        NativeMethods.MemoryProtection.ReadWrite);

    /// <summary>Allocate RWX memory (for shellcode pages).</summary>
    public IntPtr AllocateExecutable(int size) => AllocateWithProtect(size,
        NativeMethods.MemoryProtection.ExecuteReadWrite);

    private IntPtr AllocateWithProtect(int size, NativeMethods.MemoryProtection protect)
    {
        var addr = NativeMethods.VirtualAllocEx(
            _hProcess, IntPtr.Zero, (nuint)size,
            NativeMethods.AllocationType.Commit | NativeMethods.AllocationType.Reserve,
            protect);

        if (addr == IntPtr.Zero)
            throw new InjectionException($"VirtualAllocEx({protect}) failed.");

        _allocations.Add(addr);
        return addr;
    }

    // ── Write helpers ─────────────────────────────────────────────────────────

    public void Write(IntPtr addr, byte[] data)
    {
        if (!NativeMethods.WriteProcessMemory(_hProcess, addr, data, (nuint)data.Length, out _))
            throw new InjectionException("WriteProcessMemory failed.");
    }

    /// <summary>Allocate RW + write a null-terminated UTF-16 (wide) string.</summary>
    public IntPtr AllocateWideString(string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value + "\0");
        var addr  = Allocate(bytes.Length);
        Write(addr, bytes);
        return addr;
    }

    /// <summary>Allocate RW + write a null-terminated UTF-8 string (for Mono C API).</summary>
    public IntPtr AllocateUtf8String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var addr  = Allocate(bytes.Length);
        Write(addr, bytes);
        return addr;
    }

    /// <summary>Allocate RW + write a null-terminated ANSI string.</summary>
    public IntPtr AllocateString(string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        var addr  = Allocate(bytes.Length);
        Write(addr, bytes);
        return addr;
    }

    /// <summary>Allocate RWX + write shellcode bytes.</summary>
    public IntPtr AllocateShellcode(byte[] code)
    {
        var addr = AllocateExecutable(code.Length + 16); // 16-byte pad
        Write(addr, code);
        return addr;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void FreeAll()
    {
        foreach (var addr in _allocations)
            NativeMethods.VirtualFreeEx(_hProcess, addr, 0,
                NativeMethods.AllocationType.Release);
        _allocations.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        FreeAll();
        _disposed = true;
    }
}
