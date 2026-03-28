// =============================================================================
// ZModManager — Mono Bootstrapper DLL
// =============================================================================
// This DLL is injected into a Mono/Unity process by ZModManager's
// MonoInjectionService.  Once loaded it:
//
//   1. Opens the named shared-memory region "ZModManager_BootstrapParams"
//      that the host process wrote before injection.
//   2. Resolves the Mono C API exports from the already-loaded mono DLL.
//   3. Loads the user's managed assembly and invokes the specified method.
//
// Build: MSVC, x64, Release, /MT (static CRT), exports Bootstrap.
// =============================================================================

#include "bootstrapper.h"
#include "mono_api.h"
#include "utils.h"
#include <cstring>

// ─────────────────────────────────────────────────────────────────────────────
// Resolve all required Mono exports from an already-loaded mono module.
// ─────────────────────────────────────────────────────────────────────────────

static bool ResolveMonoAPI(HMODULE hMono, MonoAPI& api)
{
#define RESOLVE(name)                                            \
    api.name = (mono_##name##_fn)GetProcAddress(hMono, "mono_" #name); \
    if (!api.name) return false;

    RESOLVE(get_root_domain)
    RESOLVE(thread_attach)
    RESOLVE(domain_assembly_open)
    RESOLVE(assembly_get_image)
    RESOLVE(class_from_name)
    RESOLVE(class_get_method_from_name)
    RESOLVE(runtime_invoke)
    RESOLVE(thread_detach)
#undef RESOLVE
    return true;
}

// ─────────────────────────────────────────────────────────────────────────────
// Main bootstrap logic — runs on a dedicated worker thread.
// ─────────────────────────────────────────────────────────────────────────────

extern "C" __declspec(dllexport)
DWORD WINAPI Bootstrap(LPVOID /*lpParam*/)
{
    // ── 1. Read params from shared memory ────────────────────────────────────
    HANDLE hMap = OpenFileMappingW(FILE_MAP_READ, FALSE, ZMM_SHARED_NAME);
    if (!hMap) return 1;

    const BootstrapParams* p =
        reinterpret_cast<const BootstrapParams*>(MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, 0));
    if (!p) { CloseHandle(hMap); return 2; }

    BootstrapParams params;
    memcpy(&params, p, sizeof(BootstrapParams));
    UnmapViewOfFile(p);
    CloseHandle(hMap);

    // ── 2. Find the loaded mono module ───────────────────────────────────────
    HMODULE hMono = FindMonoModule();   // utils.cpp
    if (!hMono) return 3;

    // ── 3. Resolve Mono C API ────────────────────────────────────────────────
    MonoAPI api{};
    if (!ResolveMonoAPI(hMono, api)) return 4;

    // ── 4. Attach thread to Mono domain ──────────────────────────────────────
    MonoDomain* domain = api.get_root_domain();
    if (!domain) return 5;

    MonoThread* thread = api.thread_attach(domain);

    // ── 5. Open the user's assembly ──────────────────────────────────────────
    MonoAssembly* assembly = api.domain_assembly_open(domain, params.assemblyPath);
    if (!assembly) { api.thread_detach(thread); return 6; }

    // ── 6. Get the image ─────────────────────────────────────────────────────
    MonoImage* image = api.assembly_get_image(assembly);
    if (!image) { api.thread_detach(thread); return 7; }

    // ── 7. Find the class ────────────────────────────────────────────────────
    MonoClass* klass = api.class_from_name(image,
        params.namespaceName, params.className);
    if (!klass) { api.thread_detach(thread); return 8; }

    // ── 8. Find the method ───────────────────────────────────────────────────
    MonoMethod* method = api.class_get_method_from_name(klass, params.methodName, 0);
    if (!method) { api.thread_detach(thread); return 9; }

    // ── 9. Invoke it ─────────────────────────────────────────────────────────
    MonoException* exc = nullptr;
    api.runtime_invoke(method, nullptr, nullptr, &exc);

    api.thread_detach(thread);
    return exc ? 10u : 0u;
}
