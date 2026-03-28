namespace ZModManager.Models;

/// <summary>
/// Identifies which IL2CPP mod-loading framework (if any) is installed
/// in the game directory.  ZModManager uses this to know where to deploy
/// mod DLLs so the right framework picks them up on launch.
/// </summary>
public enum IL2CPPFramework
{
    /// <summary>No recognised framework found.</summary>
    None,

    /// <summary>MelonLoader is installed — deploy mods to &lt;game&gt;/Mods/.</summary>
    MelonLoader,

    /// <summary>BepInEx is installed — deploy mods to &lt;game&gt;/BepInEx/plugins/.</summary>
    BepInEx,

    /// <summary>
    /// ZModManager's own version.dll bootstrapper is installed.
    /// Deploy path is written to &lt;game&gt;/ZModManager/mods.cfg; the native
    /// bootstrapper LoadLibraryW's each listed DLL before game code runs.
    /// </summary>
    ZModBootstrap,
}
