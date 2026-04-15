using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
            var json = File.ReadAllText(AppPaths.ConfigFile);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Opts) ?? new AppConfig();
            // Sync embedded profiles to disk
            SyncProfilesToDisk(config);
            return config;
        }
        catch { return new AppConfig(); }
    }

    public static void Save(AppConfig config)
    {
        try
        {
            AppPaths.EnsureDirectories();
            // Before saving, embed profiles from disk into the profiles dict
            SyncProfilesFromDisk(config);
            var json = JsonSerializer.Serialize(config, Opts);
            File.WriteAllText(AppPaths.ConfigFile, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ConfigManager] Save failed: {ex.Message}");
        }
    }

    public static string Export(AppConfig config)
    {
        SyncProfilesFromDisk(config);
        return JsonSerializer.Serialize(config, Opts);
    }

    public static AppConfig? Import(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, Opts);
            if (config != null) SyncProfilesToDisk(config);
            return config;
        }
        catch { return null; }
    }

    /// <summary>Write profiles dict entries to disk as .txt files.</summary>
    private static void SyncProfilesToDisk(AppConfig config)
    {
        AppPaths.EnsureDirectories();
        foreach (var kv in config.Profiles)
        {
            var path = Path.Combine(AppPaths.ProfilesDir, kv.Key);
            File.WriteAllText(path, kv.Value);
        }
    }

    /// <summary>Read .txt files from profiles dir into the profiles dict.</summary>
    private static void SyncProfilesFromDisk(AppConfig config)
    {
        AppPaths.EnsureDirectories();
        if (!Directory.Exists(AppPaths.ProfilesDir)) return;
        foreach (var file in Directory.GetFiles(AppPaths.ProfilesDir, "*.txt"))
        {
            var name = Path.GetFileName(file);
            config.Profiles[name] = File.ReadAllText(file);
        }
    }
}
