using System;
using System.Runtime.InteropServices;

namespace ZModManager.Injection;

public class InjectionException : Exception
{
    public int Win32Error { get; }

    public InjectionException(string reason)
        : base(reason)
    {
        Win32Error = Marshal.GetLastWin32Error();
    }

    public InjectionException(string reason, Exception inner)
        : base(reason, inner)
    {
        Win32Error = Marshal.GetLastWin32Error();
    }

    public override string ToString()
        => Win32Error != 0
            ? $"{Message} (Win32={Win32Error}: {new System.ComponentModel.Win32Exception(Win32Error).Message})"
            : Message;
}
