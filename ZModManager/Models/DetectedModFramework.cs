namespace ZModManager.Models;

public enum DetectedModFramework
{
    Unknown,      // path missing, error reading DLL, or mixed-mode assembly
    MelonLoader,  // assembly references MelonLoader.*
    BepInEx,      // assembly references BepInEx.*
    Managed,      // valid .NET metadata but no recognised framework reference
    Native,       // no .NET metadata — unmanaged/native DLL
}
