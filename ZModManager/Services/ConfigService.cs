using System;
using System.IO;
using System.Text.Json;
using ZModManager.Models;

namespace ZModManager.Services;

public class ConfigService
{
    private static readonly string ConfigDir  =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZModManager");

    private static readonly string ConfigPath =
        Path.Combine(ConfigDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented          = true,
        PropertyNameCaseInsensitive = true,
        Converters             = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new AppSettings();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, Options));
    }
}
