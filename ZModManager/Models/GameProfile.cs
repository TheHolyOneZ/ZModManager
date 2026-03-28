using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace ZModManager.Models;

public class GameProfile
{
    public string      Id           { get; set; } = Guid.NewGuid().ToString();
    public string      Name         { get; set; } = string.Empty;
    public string      GameExePath  { get; set; } = string.Empty;
    public RuntimeType RuntimeType  { get; set; } = RuntimeType.Unknown;
    public List<ModEntry> Mods      { get; set; } = new();
    public string      LogFilePath  { get; set; } = string.Empty;
    public string      LaunchArgs   { get; set; } = string.Empty;

    [JsonIgnore]
    public string GameDirectory =>
        string.IsNullOrEmpty(GameExePath) ? string.Empty
        : Path.GetDirectoryName(GameExePath) ?? string.Empty;

    [JsonIgnore]
    public string ProcessName =>
        string.IsNullOrEmpty(GameExePath) ? string.Empty
        : Path.GetFileNameWithoutExtension(GameExePath);

    [JsonIgnore]
    public string RuntimeLabel => RuntimeType switch
    {
        RuntimeType.Mono   => "Mono",
        RuntimeType.IL2CPP => "IL2CPP",
        _                  => "Unknown"
    };
}
