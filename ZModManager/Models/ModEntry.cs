using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ZModManager.Models;

public class ModEntry : INotifyPropertyChanged
{
    private string _name      = "New Mod";
    private bool   _isEnabled = true;

    private string _notes = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNotes)); }
    }

    [JsonIgnore]
    public bool HasNotes => !string.IsNullOrWhiteSpace(_notes);

    // Runtime of the game this mod targets (set when creating the mod)
    public RuntimeType RuntimeType { get; set; } = RuntimeType.Unknown;

    // Only one is populated depending on RuntimeType
    public MonoInjectionConfig?   MonoConfig   { get; set; }
    public IL2CPPInjectionConfig? IL2CPPConfig { get; set; }

    [JsonIgnore]
    public string ActiveDllPath => RuntimeType switch
    {
        RuntimeType.Mono   => MonoConfig?.AssemblyPath    ?? string.Empty,
        RuntimeType.IL2CPP => IL2CPPConfig?.NativeDllPath ?? string.Empty,
        _                  => string.Empty
    };

    [JsonIgnore]
    public string DllFileName
    {
        get
        {
            var p = ActiveDllPath;
            return p.Length > 0 ? Path.GetFileName(p) : "not set";
        }
    }

    [JsonIgnore]
    public string DllVersionTooltip
    {
        get
        {
            var p = ActiveDllPath;
            if (string.IsNullOrEmpty(p)) return "No DLL path set";
            if (!File.Exists(p)) return $"{p}\n(file not found)";
            try
            {
                var v = FileVersionInfo.GetVersionInfo(p);
                var sb = new System.Text.StringBuilder(p);
                if (!string.IsNullOrWhiteSpace(v.ProductName))  sb.Append($"\nProduct: {v.ProductName}");
                if (!string.IsNullOrWhiteSpace(v.FileVersion))  sb.Append($"\nVersion: {v.FileVersion}");
                if (!string.IsNullOrWhiteSpace(v.CompanyName))  sb.Append($"\nBy: {v.CompanyName}");
                return sb.ToString();
            }
            catch { return p; }
        }
    }

    [JsonIgnore]
    public string EntryPointLabel => RuntimeType == RuntimeType.Mono && MonoConfig != null
        ? $"{MonoConfig.Namespace}.{MonoConfig.ClassName}.{MonoConfig.MethodName}()"
        : RuntimeType == RuntimeType.IL2CPP
            ? "native DllMain"
            : "—";

    // ── Framework compatibility (runtime-only, never persisted) ──────────────

    private DetectedModFramework _frameworkTarget = DetectedModFramework.Unknown;
    private string _compatibilityWarning = string.Empty;

    [JsonIgnore]
    public DetectedModFramework FrameworkTarget
    {
        get => _frameworkTarget;
        set
        {
            if (_frameworkTarget == value) return;
            _frameworkTarget = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrameworkTargetLabel));
        }
    }

    [JsonIgnore]
    public string CompatibilityWarning
    {
        get => _compatibilityWarning;
        set
        {
            if (_compatibilityWarning == value) return;
            _compatibilityWarning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCompatibilityWarning));
        }
    }

    [JsonIgnore]
    public bool HasCompatibilityWarning => !string.IsNullOrEmpty(_compatibilityWarning);

    [JsonIgnore]
    public string FrameworkTargetLabel => _frameworkTarget switch
    {
        DetectedModFramework.MelonLoader => "MelonLoader mod",
        DetectedModFramework.BepInEx     => "BepInEx plugin",
        DetectedModFramework.Managed     => "Managed .NET DLL",
        DetectedModFramework.Native      => "Native DLL",
        _                                => string.Empty
    };

    /// <summary>Fire display-only computed properties after externally mutating configs.</summary>
    public void NotifyDisplay()
    {
        OnPropertyChanged(nameof(DllFileName));
        OnPropertyChanged(nameof(EntryPointLabel));
        OnPropertyChanged(nameof(ActiveDllPath));
        OnPropertyChanged(nameof(DllVersionTooltip));
        OnPropertyChanged(nameof(FrameworkTargetLabel));
        OnPropertyChanged(nameof(HasCompatibilityWarning));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
