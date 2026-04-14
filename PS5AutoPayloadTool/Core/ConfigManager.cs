using System;
using System.IO;
using System.Text.Json;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions _readOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigFile))
                return new AppConfig();

            var json = File.ReadAllText(AppPaths.ConfigFile);
            return JsonSerializer.Deserialize<AppConfig>(json, _readOpts) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(config, _writeOpts);
            File.WriteAllText(AppPaths.ConfigFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] Save failed: {ex.Message}");
        }
    }

    public static string Export(AppConfig config)
    {
        return JsonSerializer.Serialize(config, _writeOpts);
    }

    public static AppConfig? Import(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(json, _readOpts);
        }
        catch
        {
            return null;
        }
    }
}
