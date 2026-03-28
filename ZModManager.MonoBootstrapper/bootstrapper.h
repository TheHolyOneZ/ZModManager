#pragma once
#include <Windows.h>

// ─────────────────────────────────────────────────────────────────────────────
// Parameters passed from the C# manager into the bootstrapper.
// The manager allocates this struct in the target process before the
// bootstrapper DLL is loaded (or writes it immediately after load via a named
// shared memory region). The bootstrapper reads it from the shared region.
// ─────────────────────────────────────────────────────────────────────────────

#define ZMM_SHARED_NAME L"ZModManager_BootstrapParams"
#define ZMM_MAX_PATH    512
#define ZMM_MAX_IDENT   256

struct BootstrapParams {
    char assemblyPath[ZMM_MAX_PATH];  // Full path to the managed mod .dll
    char namespaceName[ZMM_MAX_IDENT];
    char className[ZMM_MAX_IDENT];
    char methodName[ZMM_MAX_IDENT];
};

// Exported entry point (also called from DllMain worker thread)
extern "C" __declspec(dllexport) DWORD WINAPI Bootstrap(LPVOID lpParam);
