using System.Collections.Generic;

namespace ZModManager.Models;

public class AppSettings
{
    public List<GameProfile> Profiles               { get; set; } = new();
    public string            LastSelectedId         { get; set; } = string.Empty;
    public bool              AutoDetectRuntime      { get; set; } = true;
    public double            WindowLeft             { get; set; } = 100;
    public double            WindowTop              { get; set; } = 100;
    public double            WindowWidth            { get; set; } = 1060;
    public double            WindowHeight           { get; set; } = 680;
    public bool              IsMaximized            { get; set; } = false;

    // ── User preferences ──────────────────────────────────────────────────────
    public IL2CPPFramework   DefaultIL2CPPFramework { get; set; } = IL2CPPFramework.MelonLoader;
    public bool              ConfirmBeforeLaunch    { get; set; } = false;
    public bool              MinimizeToTray         { get; set; } = true;
    public bool              AutoDisableIncompat    { get; set; } = true;
}
