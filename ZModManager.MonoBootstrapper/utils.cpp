#include "utils.h"
#include <Psapi.h>
#include <cstring>
#pragma comment(lib, "Psapi.lib")

HMODULE FindMonoModule()
{
    const char* targets[] = {
        "mono.dll",
        "mono-2.0-bdwgc.dll",
        "mono-2.0.dll",
        nullptr
    };

    HANDLE hProcess = GetCurrentProcess();
    HMODULE mods[1024];
    DWORD needed = 0;

    if (!EnumProcessModules(hProcess, mods, sizeof(mods), &needed))
        return nullptr;

    DWORD count = needed / sizeof(HMODULE);
    char name[MAX_PATH];

    for (DWORD i = 0; i < count; i++)
    {
        if (!GetModuleBaseNameA(hProcess, mods[i], name, sizeof(name)))
            continue;

        for (int j = 0; targets[j]; j++)
        {
            if (_stricmp(name, targets[j]) == 0)
                return mods[i];
        }
    }

    return nullptr;
}
