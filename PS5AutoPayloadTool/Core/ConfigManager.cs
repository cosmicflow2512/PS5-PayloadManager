using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public static class ConfigManager
{
    // Write options: compact, null fields omitted
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Read options: lenient — case-insensitive, numbers from strings
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
            var json = File.ReadAllText(AppPaths.ConfigFile);
            var config = ParseConfig(json) ?? new AppConfig();
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
            SyncProfilesFromDisk(config);
            var json = JsonSerializer.Serialize(config, WriteOpts);
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
        return JsonSerializer.Serialize(config, WriteOpts);
    }

    /// <summary>
    /// Import a config JSON string. Returns (config, null) on success or
    /// (null, errorMessage) on failure so callers can show a meaningful message.
    /// </summary>
    public static (AppConfig? Config, string? Error) Import(string json)
    {
        try
        {
            var config = ParseConfig(json);
            if (config == null) return (null, "JSON parsed to null — file may be empty.");
            SyncProfilesToDisk(config);
            return (config, null);
        }
        catch (JsonException ex)
        {
            return (null, $"JSON error at line {ex.LineNumber}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses JSON into AppConfig. Handles two layouts:
    ///   • Nested:  { "state": { "ps5_ip": "...", ... }, "sources": [...], ... }
    ///   • Flat HA: { "ps5_ip": "...", "builder_steps": [...], "sources": [...], ... }
    /// </summary>
    private static AppConfig? ParseConfig(string json)
    {
        // First attempt: strict nested format (our native format)
        var config = JsonSerializer.Deserialize<AppConfig>(json, ReadOpts);
        if (config == null) return null;

        // If state is empty but we can find flat-level fields, migrate them.
        // This handles HA backups that don't use the "state" wrapper.
        if (string.IsNullOrEmpty(config.State.PS5Ip) || config.State.PS5Ip == "192.168.1.100")
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("ps5_ip", out var ip) && ip.ValueKind == JsonValueKind.String)
                config.State.PS5Ip = ip.GetString()!;

            if (root.TryGetProperty("github_token", out var tok) && tok.ValueKind == JsonValueKind.String)
                config.State.GitHubToken = tok.GetString()!;

            if (root.TryGetProperty("builder_steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                var stepsJson = steps.GetRawText();
                var parsed = JsonSerializer.Deserialize<List<PS5AutoPayloadTool.Models.BuilderStep>>(stepsJson, ReadOpts);
                if (parsed != null) config.State.BuilderSteps = parsed;
            }
        }

        // Keep devices[0] in sync with state.PS5Ip
        if (config.Devices.Count == 0 && !string.IsNullOrEmpty(config.State.PS5Ip))
            config.Devices.Add(new DeviceConfig { Ip = config.State.PS5Ip });

        return config;
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
