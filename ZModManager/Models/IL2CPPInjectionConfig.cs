namespace ZModManager.Models;

public class IL2CPPInjectionConfig
{
    /// <summary>Path to the native x64 mod DLL that will be injected via LoadLibrary.</summary>
    public string NativeDllPath { get; set; } = string.Empty;
}
