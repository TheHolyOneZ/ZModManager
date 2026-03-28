#pragma once
#include <Windows.h>
#include "mono_api.h"

// Find mono.dll / mono-2.0-bdwgc.dll already loaded in this process.
HMODULE FindMonoModule();
