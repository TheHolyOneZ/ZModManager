#pragma once
#include <Windows.h>

// ─────────────────────────────────────────────────────────────────────────────
// Mono C API type forward declarations
// Matches the Mono Bleeding Edge (mono-2.0-bdwgc.dll) ABI used by Unity.
// ─────────────────────────────────────────────────────────────────────────────

typedef struct _MonoDomain   MonoDomain;
typedef struct _MonoAssembly MonoAssembly;
typedef struct _MonoImage    MonoImage;
typedef struct _MonoClass    MonoClass;
typedef struct _MonoMethod   MonoMethod;
typedef struct _MonoObject   MonoObject;
typedef struct _MonoThread   MonoThread;
typedef struct _MonoException MonoException;

typedef enum {
    MONO_IMAGE_OK,
    MONO_IMAGE_ERROR_ERRNO,
    MONO_IMAGE_MISSING_ASSEMBLYREF,
    MONO_IMAGE_IMAGE_INVALID
} MonoImageOpenStatus;

// ─────────────────────────────────────────────────────────────────────────────
// Function pointer typedefs for every Mono export we need
// ─────────────────────────────────────────────────────────────────────────────

typedef MonoDomain*   (*mono_get_root_domain_fn)   ();
typedef MonoThread*   (*mono_thread_attach_fn)      (MonoDomain*);
typedef MonoAssembly* (*mono_domain_assembly_open_fn)(MonoDomain*, const char* name);
typedef MonoImage*    (*mono_assembly_get_image_fn) (MonoAssembly*);
typedef MonoClass*    (*mono_class_from_name_fn)    (MonoImage*, const char* ns, const char* name);
typedef MonoMethod*   (*mono_class_get_method_from_name_fn)(MonoClass*, const char* name, int param_count);
typedef MonoObject*   (*mono_runtime_invoke_fn)     (MonoMethod*, void* obj, void** params, MonoException**);
typedef void          (*mono_assembly_close_fn)     (MonoAssembly*);
typedef void          (*mono_thread_detach_fn)      (MonoThread*);

// ─────────────────────────────────────────────────────────────────────────────
// Struct holding all resolved Mono function pointers
// ─────────────────────────────────────────────────────────────────────────────

struct MonoAPI {
    mono_get_root_domain_fn          get_root_domain;
    mono_thread_attach_fn            thread_attach;
    mono_domain_assembly_open_fn     domain_assembly_open;
    mono_assembly_get_image_fn       assembly_get_image;
    mono_class_from_name_fn          class_from_name;
    mono_class_get_method_from_name_fn class_get_method_from_name;
    mono_runtime_invoke_fn           runtime_invoke;
    mono_thread_detach_fn            thread_detach;
};
