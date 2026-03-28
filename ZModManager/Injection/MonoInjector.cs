using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ZModManager.Models;

namespace ZModManager.Injection;

/// <summary>
/// Injects a managed assembly into a Mono/Unity game process without any
/// external bootstrapper DLL.
///
/// Shellcode chain (x64 Windows calling convention):
///   mono_get_root_domain → mono_thread_attach → mono_domain_assembly_open
///   → mono_assembly_get_image → mono_class_from_name
///   → mono_class_get_method_from_name → mono_runtime_invoke
///
/// Each step that returns a pointer has a NULL check.  On failure the
/// shellcode returns a specific error code so we can surface a useful message.
/// Error codes:
///   1 = mono_get_root_domain() returned NULL   (Mono not yet initialised)
///   2 = mono_domain_assembly_open() returned NULL (bad path / domain not ready)
///   3 = mono_assembly_get_image() returned NULL
///   4 = mono_class_from_name() returned NULL   (wrong namespace / class)
///   5 = mono_class_get_method_from_name() returned NULL (wrong method name)
///   6 = mono_runtime_invoke() raised a managed exception
/// </summary>
public class MonoInjector
{
    private static readonly string[] MonoCandidates =
    {
        "mono.dll",
        "mono-2.0-bdwgc.dll",
        "mono-2.0.dll",
        "mono-2.0-boehm.dll"
    };

    public void Inject(int processId, string gameDirectory, MonoInjectionConfig config)
    {
        if (!File.Exists(config.AssemblyPath))
            throw new InjectionException($"Mod assembly not found: {config.AssemblyPath}");

        // ── 1. Locate mono in the running process ─────────────────────────────
        var (remoteMonoBase, remoteMonoPath) = ModuleScanner.FindModule(processId, MonoCandidates);

        if (remoteMonoBase == IntPtr.Zero)
            throw new InjectionException(
                "Could not find mono.dll in the target process. " +
                "Ensure the game is fully loaded (past the splash screen) before injecting.");

        // ── 2. Find the local copy to resolve export offsets ──────────────────
        var localMonoPath = (remoteMonoPath != null && File.Exists(remoteMonoPath))
            ? remoteMonoPath
            : FindLocalMono(gameDirectory)
              ?? throw new InjectionException(
                  "Mono module found in process but could not locate mono.dll on disk " +
                  "to resolve export addresses. Ensure the game directory is correct.");

        // ── 3. Resolve Mono C API addresses in the remote process ─────────────
        IntPtr Remote(string name)
            => ModuleScanner.GetRemoteProcAddress(remoteMonoBase, localMonoPath, name);

        var fnGetRootDomain = Remote("mono_get_root_domain");
        var fnThreadAttach  = Remote("mono_thread_attach");
        var fnDomainAsmOpen = Remote("mono_domain_assembly_open");
        var fnAsmGetImage   = Remote("mono_assembly_get_image");
        var fnClassFromName = Remote("mono_class_from_name");
        var fnGetMethod     = Remote("mono_class_get_method_from_name");
        var fnRuntimeInvoke = Remote("mono_runtime_invoke");

        // ── 4. Open remote process and write data + shellcode ─────────────────
        var hProcess = NativeMethods.OpenProcess(
            NativeMethods.ProcessAccessFlags.All, false, processId);

        if (hProcess == IntPtr.Zero)
            throw new InjectionException(
                $"OpenProcess({processId}) failed. Run ZModManager as Administrator.");

        try
        {
            using var mem = new ProcessMemory(hProcess);

            // Write string parameters as UTF-8 null-terminated into RW memory
            var pAssembly   = mem.AllocateUtf8String(Path.GetFullPath(config.AssemblyPath));
            var pNamespace  = mem.AllocateUtf8String(config.Namespace);
            var pClassName  = mem.AllocateUtf8String(config.ClassName);
            var pMethodName = mem.AllocateUtf8String(config.MethodName);

            // Build shellcode with full NULL checks and exception capture
            var code = BuildShellcode(
                fnGetRootDomain, fnThreadAttach, fnDomainAsmOpen,
                fnAsmGetImage, fnClassFromName, fnGetMethod, fnRuntimeInvoke,
                pAssembly, pNamespace, pClassName, pMethodName);

            // Write shellcode into RWX (ExecuteReadWrite) memory — critical!
            var pCode = mem.AllocateShellcode(code);

            // ── 5. Execute and wait ───────────────────────────────────────────
            var hThread = NativeMethods.CreateRemoteThread(
                hProcess, IntPtr.Zero, 0, pCode, IntPtr.Zero, 0, out _);

            if (hThread == IntPtr.Zero)
                throw new InjectionException(
                    $"CreateRemoteThread failed (error {Marshal.GetLastWin32Error()}). " +
                    "Try running as Administrator.");

            uint wait = NativeMethods.WaitForSingleObject(hThread, 20_000);
            NativeMethods.GetExitCodeThread(hThread, out uint exitCode);
            NativeMethods.CloseHandle(hThread);

            if (wait == NativeMethods.WAIT_TIMEOUT)
                throw new InjectionException("Mono injection timed out (shellcode hung).");

            if (exitCode != 0)
            {
                var reason = exitCode switch
                {
                    1 => "mono_get_root_domain() returned NULL.\n" +
                         "  Mono is not yet fully initialised. Wait a few more seconds after the " +
                         "game loads, then try again.",
                    2 => "mono_domain_assembly_open() returned NULL.\n" +
                         "  The assembly path may be wrong, or the Mono domain is not yet ready.\n" +
                         $"  Path used: {Path.GetFullPath(config.AssemblyPath)}",
                    3 => "mono_assembly_get_image() returned NULL.\n" +
                         "  The assembly opened but has no image (may be corrupt or invalid).",
                    4 => "mono_class_from_name() returned NULL.\n" +
                         $"  Class '{config.Namespace}.{config.ClassName}' not found in the assembly.\n" +
                         "  Verify namespace and class name are correct.",
                    5 => "mono_class_get_method_from_name() returned NULL.\n" +
                         $"  Method '{config.MethodName}' not found in class '{config.ClassName}'.\n" +
                         "  The method must be public static void with no parameters.",
                    6 => "mono_runtime_invoke() threw a managed exception.\n" +
                         $"  {config.Namespace}.{config.ClassName}.{config.MethodName}() threw — " +
                         "check the mod's Init() code for unhandled exceptions.",
                    _ => $"Unexpected raw exit code 0x{exitCode:X8} ({exitCode}).\n" +
                         "  The shellcode likely crashed — check namespace/class/method names are correct."
                };
                throw new InjectionException($"[Mono] Injection failed: {reason}");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(hProcess);
        }
    }

    // ── Mono DLL file-system search ───────────────────────────────────────────

    private static string? FindLocalMono(string gameDirectory)
    {
        string[] patterns = { "mono.dll", "mono-2.0-bdwgc.dll", "mono-2.0.dll", "mono-2.0-boehm.dll" };

        var roots = new List<string>
        {
            gameDirectory,
            Path.Combine(gameDirectory, "MonoBleedingEdge", "EmbedRuntime"),
            Path.Combine(gameDirectory, "Mono",             "EmbedRuntime"),
            Path.Combine(gameDirectory, "Mono"),
        };

        if (Directory.Exists(gameDirectory))
        {
            foreach (var d in Directory.GetDirectories(gameDirectory))
            {
                if (!d.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) continue;
                roots.Add(Path.Combine(d, "MonoBleedingEdge", "EmbedRuntime"));
                roots.Add(Path.Combine(d, "Mono",             "EmbedRuntime"));
                roots.Add(Path.Combine(d, "Mono"));
            }
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var pat in patterns)
            {
                var full = Path.Combine(root, pat);
                if (File.Exists(full)) return full;
            }
        }

        try
        {
            foreach (var f in Directory.GetFiles(gameDirectory, "mono*.dll", SearchOption.AllDirectories))
                return f;
        }
        catch { /* ignore access errors */ }

        return null;
    }

    // ── x64 shellcode builder ─────────────────────────────────────────────────
    //
    // Stack layout (RSP relative, after SUB RSP,0x58):
    //   RSP+0x00..0x1F  = shadow space for our calls
    //   RSP+0x20        = domain   (MonoDomain*)
    //   RSP+0x28        = assembly (MonoAssembly*)
    //   RSP+0x30        = image    (MonoImage*)
    //   RSP+0x38        = class    (MonoClass*)
    //   RSP+0x40        = method   (MonoMethod*)
    //   RSP+0x48        = exception slot (MonoObject** — passed as R9 to invoke)
    //
    // NULL-check pattern:
    //   TEST RAX, RAX          ; 3 bytes
    //   JNZ  +0x0A             ; 2 bytes  — non-NULL → skip error epilogue
    //   ADD  RSP, 0x58         ; 4 bytes  ─┐
    //   MOV  EAX, <code>       ; 5 bytes   │ error epilogue (10 bytes total)
    //   RET                    ; 1 byte   ─┘
    //   <continuation>
    //
    // For the exception check after invoke the condition is inverted (JZ skips
    // the error block because NULL exception = success).

    private static byte[] BuildShellcode(
        IntPtr fnGetRootDomain, IntPtr fnThreadAttach,
        IntPtr fnDomainAsmOpen, IntPtr fnAsmGetImage,
        IntPtr fnClassFromName, IntPtr fnGetMethod, IntPtr fnRuntimeInvoke,
        IntPtr pAssembly, IntPtr pNamespace, IntPtr pClassName, IntPtr pMethodName)
    {
        var sc = new List<byte>();

        // SUB RSP, 0x58
        sc.Add3(0x48, 0x83, 0xEC); sc.Add(0x58);

        // ── domain = mono_get_root_domain() ──────────────────────────────────
        sc.Mov_RAX(fnGetRootDomain);
        sc.Add2(0xFF, 0xD0);                 // CALL RAX
        sc.Mov_RSPoff_RAX(0x20);             // MOV [RSP+0x20], RAX
        sc.NullCheck(1);                     // if NULL → return 1

        // ── mono_thread_attach(domain) ────────────────────────────────────────
        sc.Mov_RCX_RSPoff(0x20);
        sc.Mov_RAX(fnThreadAttach);
        sc.Add2(0xFF, 0xD0);                 // result ignored (already-attached is fine)

        // ── assembly = mono_domain_assembly_open(domain, assemblyPath) ────────
        sc.Mov_RCX_RSPoff(0x20);
        sc.Mov_RDX(pAssembly);
        sc.Mov_RAX(fnDomainAsmOpen);
        sc.Add2(0xFF, 0xD0);
        sc.Mov_RSPoff_RAX(0x28);
        sc.NullCheck(2);

        // ── image = mono_assembly_get_image(assembly) ─────────────────────────
        sc.Mov_RCX_RSPoff(0x28);
        sc.Mov_RAX(fnAsmGetImage);
        sc.Add2(0xFF, 0xD0);
        sc.Mov_RSPoff_RAX(0x30);
        sc.NullCheck(3);

        // ── class = mono_class_from_name(image, ns, className) ───────────────
        sc.Mov_RCX_RSPoff(0x30);
        sc.Mov_RDX(pNamespace);
        sc.Mov_R8(pClassName);
        sc.Mov_RAX(fnClassFromName);
        sc.Add2(0xFF, 0xD0);
        sc.Mov_RSPoff_RAX(0x38);
        sc.NullCheck(4);

        // ── method = mono_class_get_method_from_name(cls, name, 0) ───────────
        sc.Mov_RCX_RSPoff(0x38);
        sc.Mov_RDX(pMethodName);
        sc.Add3(0x45, 0x33, 0xC0);           // XOR R8D, R8D  (param_count=0)
        sc.Mov_RAX(fnGetMethod);
        sc.Add2(0xFF, 0xD0);
        sc.Mov_RSPoff_RAX(0x40);
        sc.NullCheck(5);

        // ── mono_runtime_invoke(method, NULL, NULL, &exception) ───────────────
        // Clear exception slot at RSP+0x48
        // MOV QWORD PTR [RSP+0x48], 0
        sc.Add4(0x48, 0xC7, 0x44, 0x24); sc.Add(0x48);
        sc.Add4(0x00, 0x00, 0x00, 0x00);
        // LEA R9, [RSP+0x48]  — pass address of exception slot as 4th arg
        sc.Add4(0x4C, 0x8D, 0x4C, 0x24); sc.Add(0x48);
        sc.Mov_RCX_RSPoff(0x40);             // method
        sc.Add3(0x48, 0x33, 0xD2);           // XOR RDX, RDX  (obj = NULL → static)
        sc.Add3(0x4D, 0x33, 0xC0);           // XOR R8,  R8   (args = NULL)
        sc.Mov_RAX(fnRuntimeInvoke);
        sc.Add2(0xFF, 0xD0);

        // ── check for managed exception ───────────────────────────────────────
        // MOV RAX, [RSP+0x48]
        sc.Add4(0x48, 0x8B, 0x44, 0x24); sc.Add(0x48);
        // TEST RAX, RAX
        sc.Add3(0x48, 0x85, 0xC0);
        // JZ +0x0A  — exception is NULL (no exception) → skip error, go to success
        sc.Add2(0x74, 0x0A);
        sc.ErrorEpilogue(6);

        // ── success ───────────────────────────────────────────────────────────
        sc.Add3(0x48, 0x83, 0xC4); sc.Add(0x58); // ADD RSP, 0x58
        sc.Add2(0x33, 0xC0);                      // XOR EAX, EAX
        sc.Add(0xC3);                             // RET

        return sc.ToArray();
    }
}

// ── Emit helpers (extension methods on List<byte>) ────────────────────────────
file static class Emit
{
    public static void Add2(this List<byte> l, byte a, byte b)
        { l.Add(a); l.Add(b); }

    public static void Add3(this List<byte> l, byte a, byte b, byte c)
        { l.Add(a); l.Add(b); l.Add(c); }

    public static void Add4(this List<byte> l, byte a, byte b, byte c, byte d)
        { l.Add(a); l.Add(b); l.Add(c); l.Add(d); }

    // MOV RAX, imm64
    public static void Mov_RAX(this List<byte> l, IntPtr v)
        { l.Add(0x48); l.Add(0xB8); l.AddRange(BitConverter.GetBytes(v.ToInt64())); }

    // MOV RDX, imm64
    public static void Mov_RDX(this List<byte> l, IntPtr v)
        { l.Add(0x48); l.Add(0xBA); l.AddRange(BitConverter.GetBytes(v.ToInt64())); }

    // MOV R8, imm64
    public static void Mov_R8(this List<byte> l, IntPtr v)
        { l.Add(0x49); l.Add(0xB8); l.AddRange(BitConverter.GetBytes(v.ToInt64())); }

    // MOV [RSP+off8], RAX
    public static void Mov_RSPoff_RAX(this List<byte> l, byte off)
        { l.Add(0x48); l.Add(0x89); l.Add(0x44); l.Add(0x24); l.Add(off); }

    // MOV RCX, [RSP+off8]
    public static void Mov_RCX_RSPoff(this List<byte> l, byte off)
        { l.Add(0x48); l.Add(0x8B); l.Add(0x4C); l.Add(0x24); l.Add(off); }

    /// <summary>
    /// Emit: TEST RAX,RAX / JNZ +0x0A / ErrorEpilogue(code).
    /// If RAX is NULL the shellcode returns <paramref name="code"/>.
    /// If RAX is non-NULL execution continues at the instruction after the epilogue.
    /// The epilogue is exactly 10 bytes so JNZ +0x0A lands precisely after it.
    /// </summary>
    public static void NullCheck(this List<byte> l, byte code)
    {
        l.Add3(0x48, 0x85, 0xC0); // TEST RAX, RAX    (3 bytes)
        l.Add2(0x75, 0x0A);        // JNZ  +0x0A       (2 bytes) — non-null → skip epilogue
        l.ErrorEpilogue(code);     //                  (10 bytes)
    }

    /// <summary>
    /// Emit: ADD RSP,0x58 / MOV EAX,code / RET  (exactly 10 bytes).
    /// </summary>
    public static void ErrorEpilogue(this List<byte> l, byte code)
    {
        l.Add3(0x48, 0x83, 0xC4); l.Add(0x58); // ADD RSP, 0x58  (4 bytes)
        l.Add(0xB8); l.Add(code); l.Add(0x00); l.Add(0x00); l.Add(0x00); // MOV EAX, code (5 bytes)
        l.Add(0xC3);                            // RET             (1 byte)
        // total = 10 bytes = 0x0A ✓ (matches JNZ/JZ skip offset)
    }
}
