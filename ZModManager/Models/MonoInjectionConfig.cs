namespace ZModManager.Models;

public class MonoInjectionConfig
{
    public string AssemblyPath  { get; set; } = string.Empty;
    public string Namespace     { get; set; } = string.Empty;
    public string ClassName     { get; set; } = string.Empty;
    public string MethodName    { get; set; } = string.Empty;
}
