// =============================================================================
// ZModManager.Bootstrap — version.dll proxy for IL2CPP Unity games
// =============================================================================
//
// Placed in the game directory as "version.dll". Windows loads it before any
// game code runs. It:
//   1. Forwards every version.dll export to the real System32\version.dll.
//   2. Reads <gameDir>\ZModManager\mods.cfg and LoadLibraryW's each listed
//      DLL so mods land in memory before a single IL2CPP instruction executes.
// =============================================================================

#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <fstream>
#include <string>

// Windows.h / winver.h already declared all the version.dll functions with
// extern "C" linkage. We define them here — the linker uses our definitions
// instead of version.lib, forwarding each call to the real system DLL.
// Do NOT wrap in an extra extern "C" { } — that causes C2733 in MSVC because
// it looks like an overload attempt on an already-declared C symbol.

static HMODULE g_real = nullptr;

// Resolve a proc from the real system version.dll (loaded in DllMain)
#define REAL(name)  reinterpret_cast<decltype(&name)>(GetProcAddress(g_real, #name))

// ─────────────────────────────────────────────────────────────────────────────
// version.dll export stubs
// Each function looks up the real export and forwards the call.
// ─────────────────────────────────────────────────────────────────────────────

BOOL APIENTRY GetFileVersionInfoA(LPCSTR l, DWORD h, DWORD n, LPVOID p) {
    auto fn = REAL(GetFileVersionInfoA); return fn ? fn(l,h,n,p) : FALSE;
}
BOOL APIENTRY GetFileVersionInfoW(LPCWSTR l, DWORD h, DWORD n, LPVOID p) {
    auto fn = REAL(GetFileVersionInfoW); return fn ? fn(l,h,n,p) : FALSE;
}
DWORD APIENTRY GetFileVersionInfoSizeA(LPCSTR l, LPDWORD lh) {
    auto fn = REAL(GetFileVersionInfoSizeA); return fn ? fn(l,lh) : 0;
}
DWORD APIENTRY GetFileVersionInfoSizeW(LPCWSTR l, LPDWORD lh) {
    auto fn = REAL(GetFileVersionInfoSizeW); return fn ? fn(l,lh) : 0;
}
BOOL APIENTRY VerQueryValueA(LPCVOID b, LPCSTR s, LPVOID* pb, PUINT pl) {
    auto fn = REAL(VerQueryValueA); return fn ? fn(b,s,pb,pl) : FALSE;
}
BOOL APIENTRY VerQueryValueW(LPCVOID b, LPCWSTR s, LPVOID* pb, PUINT pl) {
    auto fn = REAL(VerQueryValueW); return fn ? fn(b,s,pb,pl) : FALSE;
}
DWORD APIENTRY VerLanguageNameA(DWORD w, LPSTR sz, DWORD n) {
    auto fn = REAL(VerLanguageNameA); return fn ? fn(w,sz,n) : 0;
}
DWORD APIENTRY VerLanguageNameW(DWORD w, LPWSTR sz, DWORD n) {
    auto fn = REAL(VerLanguageNameW); return fn ? fn(w,sz,n) : 0;
}

// VerFindFile / VerInstallFile have different signatures in winver.h vs our
// earlier stubs — use function pointers to avoid signature mismatch warnings.

DWORD APIENTRY VerFindFileA(DWORD f, LPSTR fn_, LPSTR windir, LPSTR appdir,
    LPSTR cb, PUINT lcb, LPSTR nb, PUINT lnb)
{
    typedef DWORD(APIENTRY* PFN)(DWORD,LPSTR,LPSTR,LPSTR,LPSTR,PUINT,LPSTR,PUINT);
    auto fn = (PFN)GetProcAddress(g_real, "VerFindFileA");
    return fn ? fn(f,fn_,windir,appdir,cb,lcb,nb,lnb) : 0;
}
DWORD APIENTRY VerFindFileW(DWORD f, LPWSTR fn_, LPWSTR windir, LPWSTR appdir,
    LPWSTR cb, PUINT lcb, LPWSTR nb, PUINT lnb)
{
    typedef DWORD(APIENTRY* PFN)(DWORD,LPWSTR,LPWSTR,LPWSTR,LPWSTR,PUINT,LPWSTR,PUINT);
    auto fn = (PFN)GetProcAddress(g_real, "VerFindFileW");
    return fn ? fn(f,fn_,windir,appdir,cb,lcb,nb,lnb) : 0;
}
DWORD APIENTRY VerInstallFileA(DWORD f, LPSTR s, LPSTR d, LPSTR sp, LPSTR dp,
    LPSTR cp, LPSTR r, PUINT rl)
{
    typedef DWORD(APIENTRY* PFN)(DWORD,LPSTR,LPSTR,LPSTR,LPSTR,LPSTR,LPSTR,PUINT);
    auto fn = (PFN)GetProcAddress(g_real, "VerInstallFileA");
    return fn ? fn(f,s,d,sp,dp,cp,r,rl) : 0;
}
DWORD APIENTRY VerInstallFileW(DWORD f, LPWSTR s, LPWSTR d, LPWSTR sp, LPWSTR dp,
    LPWSTR cp, LPWSTR r, PUINT rl)
{
    typedef DWORD(APIENTRY* PFN)(DWORD,LPWSTR,LPWSTR,LPWSTR,LPWSTR,LPWSTR,LPWSTR,PUINT);
    auto fn = (PFN)GetProcAddress(g_real, "VerInstallFileW");
    return fn ? fn(f,s,d,sp,dp,cp,r,rl) : 0;
}

// Optional exports — present on Windows 10+ but gracefully no-op if absent.
BOOL APIENTRY GetFileVersionInfoByHandle(DWORD s, HANDLE h, DWORD off, LPVOID p) {
    typedef BOOL(APIENTRY*PFN)(DWORD,HANDLE,DWORD,LPVOID);
    auto fn=(PFN)GetProcAddress(g_real,"GetFileVersionInfoByHandle");
    return fn?fn(s,h,off,p):FALSE;
}
DWORD APIENTRY GetFileVersionInfoSizeExA(DWORD f, LPCSTR n, LPDWORD h) {
    typedef DWORD(APIENTRY*PFN)(DWORD,LPCSTR,LPDWORD);
    auto fn=(PFN)GetProcAddress(g_real,"GetFileVersionInfoSizeExA");
    return fn?fn(f,n,h):0;
}
DWORD APIENTRY GetFileVersionInfoSizeExW(DWORD f, LPCWSTR n, LPDWORD h) {
    typedef DWORD(APIENTRY*PFN)(DWORD,LPCWSTR,LPDWORD);
    auto fn=(PFN)GetProcAddress(g_real,"GetFileVersionInfoSizeExW");
    return fn?fn(f,n,h):0;
}
BOOL APIENTRY GetFileVersionInfoExA(DWORD f, LPCSTR n, DWORD h, DWORD l, LPVOID p) {
    typedef BOOL(APIENTRY*PFN)(DWORD,LPCSTR,DWORD,DWORD,LPVOID);
    auto fn=(PFN)GetProcAddress(g_real,"GetFileVersionInfoExA");
    return fn?fn(f,n,h,l,p):FALSE;
}
BOOL APIENTRY GetFileVersionInfoExW(DWORD f, LPCWSTR n, DWORD h, DWORD l, LPVOID p) {
    typedef BOOL(APIENTRY*PFN)(DWORD,LPCWSTR,DWORD,DWORD,LPVOID);
    auto fn=(PFN)GetProcAddress(g_real,"GetFileVersionInfoExW");
    return fn?fn(f,n,h,l,p):FALSE;
}

// ─────────────────────────────────────────────────────────────────────────────
// Mod loading
// ─────────────────────────────────────────────────────────────────────────────

static void LoadMods()
{
    // Resolve the game directory from the main EXE path
    WCHAR exePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    WCHAR* lastSlash = wcsrchr(exePath, L'\\');
    if (!lastSlash) return;
    *lastSlash = L'\0'; // now points to game directory

    WCHAR cfgPath[MAX_PATH] = {};
    swprintf_s(cfgPath, L"%s\\ZModManager\\mods.cfg", exePath);

    std::wifstream file(cfgPath);
    if (!file.is_open()) return;

    std::wstring line;
    while (std::getline(file, line))
    {
        auto start = line.find_first_not_of(L" \t\r\n");
        if (start == std::wstring::npos) continue;
        auto end = line.find_last_not_of(L" \t\r\n");
        line = line.substr(start, end - start + 1);
        if (line.empty() || line[0] == L'#') continue;
        LoadLibraryW(line.c_str());
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DllMain
// ─────────────────────────────────────────────────────────────────────────────

BOOL WINAPI DllMain(HMODULE hModule, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        {
            WCHAR sysDir[MAX_PATH] = {};
            GetSystemDirectoryW(sysDir, MAX_PATH);
            wcscat_s(sysDir, L"\\version.dll");
            g_real = LoadLibraryW(sysDir);
        }
        LoadMods();
        break;

    case DLL_PROCESS_DETACH:
        if (g_real) { FreeLibrary(g_real); g_real = nullptr; }
        break;
    }
    return TRUE;
}
