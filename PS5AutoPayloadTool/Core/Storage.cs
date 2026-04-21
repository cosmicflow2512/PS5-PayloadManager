using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PS5AutoPayloadTool.Core;

public static class Storage
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    // ── Generic helpers ──────────────────────────────────────────────────────

    public static T Load<T>(string path, T fallback) where T : new()
    {
        try
        {
            if (!File.Exists(path)) return fallback;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json) ?? fallback;
        }
        catch { return fallback; }
    }

    public static void Save<T>(string path, T value)
    {
        try { File.WriteAllText(path, JsonSerializer.Serialize(value, _opts)); }
        catch { }
    }

    public static JsonNode? LoadRaw(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonNode.Parse(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public static void SaveRaw(string path, JsonNode node)
    {
        try { File.WriteAllText(path, node.ToJsonString(_opts)); }
        catch { }
    }

    // ── Favorites ────────────────────────────────────────────────────────────

    public static List<string> LoadPayloadFavs()
    {
        var state = LoadUiState();
        return state["payloadFavs"]?.Deserialize<List<string>>() ?? [];
    }

    public static void SavePayloadFavs(List<string> favs)
    {
        var state = LoadUiState();
        state["payloadFavs"] = JsonNode.Parse(JsonSerializer.Serialize(favs));
        SaveUiState(state);
    }

    public static List<string> LoadProfileFavs()
    {
        var state = LoadUiState();
        return state["profileFavs"]?.Deserialize<List<string>>() ?? [];
    }

    public static void SaveProfileFavs(List<string> favs)
    {
        var state = LoadUiState();
        state["profileFavs"] = JsonNode.Parse(JsonSerializer.Serialize(favs));
        SaveUiState(state);
    }

    public static string LoadPs5Ip()
        => LoadUiState()["ps5_ip"]?.GetValue<string>() ?? "";

    public static void SavePs5Ip(string ip)
    {
        var state = LoadUiState();
        state["ps5_ip"] = ip;
        SaveUiState(state);
    }

    // ── Sources update ────────────────────────────────────────────────────────

    public static void UpdateSource(SourceEntry updated)
    {
        var sources = LoadSources();
        var idx = sources.FindIndex(s => s.Repo == updated.Repo);
        if (idx >= 0) sources[idx] = updated;
        SaveSources(sources);
    }

    // ── Devices ──────────────────────────────────────────────────────────────

    public static List<DeviceEntry> LoadDevices()
        => Load<List<DeviceEntry>>(AppPaths.DevicesJson, []);

    public static void SaveDevices(List<DeviceEntry> devices)
        => Save(AppPaths.DevicesJson, devices);

    // ── Sources ──────────────────────────────────────────────────────────────

    public static List<SourceEntry> LoadSources()
        => Load<List<SourceEntry>>(AppPaths.SourcesJson, []);

    public static void SaveSources(List<SourceEntry> sources)
        => Save(AppPaths.SourcesJson, sources);

    // ── Payload metadata ─────────────────────────────────────────────────────

    public static Dictionary<string, PayloadMeta> LoadPayloadMeta()
        => Load<Dictionary<string, PayloadMeta>>(AppPaths.PayloadsJson, []);

    public static void SavePayloadMeta(Dictionary<string, PayloadMeta> meta)
        => Save(AppPaths.PayloadsJson, meta);

    // ── UI state ─────────────────────────────────────────────────────────────

    public static JsonObject LoadUiState()
        => LoadRaw(AppPaths.StateJson) as JsonObject ?? [];

    public static void SaveUiState(JsonObject state)
        => SaveRaw(AppPaths.StateJson, state);

    // ── Profiles ─────────────────────────────────────────────────────────────

    public static List<string> ListProfiles()
    {
        if (!Directory.Exists(AppPaths.ProfilesDir)) return [];
        return Directory.GetFiles(AppPaths.ProfilesDir, "*.txt")
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Select(f => f!)
            .OrderBy(f => f)
            .ToList();
    }

    public static string? ReadProfile(string name)
    {
        var path = SafeProfilePath(name);
        return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static void WriteProfile(string name, string content)
    {
        var path = SafeProfilePath(name);
        if (path != null) File.WriteAllText(path, content);
    }

    public static void DeleteProfile(string name)
    {
        var path = SafeProfilePath(name);
        if (path != null && File.Exists(path)) File.Delete(path);
    }

    // ── Payload file helpers ─────────────────────────────────────────────────

    public static List<PayloadFile> ListPayloads()
    {
        var meta = LoadPayloadMeta();
        var result = new List<PayloadFile>();
        foreach (var file in Directory.GetFiles(AppPaths.PayloadsDir))
        {
            var name = Path.GetFileName(file);
            var ext  = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".elf" && ext != ".lua" && ext != ".bin") continue;
            var info = new FileInfo(file);
            meta.TryGetValue(name, out var m);
            result.Add(new PayloadFile
            {
                Name       = name,
                Size       = info.Length,
                Modified   = info.LastWriteTimeUtc,
                Repo       = m?.Repo ?? "",
                Version    = m?.Version ?? "",
                AllVersions = m?.AllVersions ?? [],
                DownloadUrl = m?.DownloadUrl ?? "",
                ReleaseId  = m?.ReleaseId ?? 0,
                AssetSize  = m?.AssetSize ?? 0,
                PublishedAt = m?.PublishedAt ?? "",
                AssetUpdatedAt = m?.AssetUpdatedAt ?? "",
            });
        }
        return result.OrderBy(p => p.Name).ToList();
    }

    // ── Backup / restore ─────────────────────────────────────────────────────

    public static JsonObject BuildBackup()
    {
        var obj = new JsonObject();
        obj["devices"]  = JsonNode.Parse(JsonSerializer.Serialize(LoadDevices()));
        obj["sources"]  = JsonNode.Parse(JsonSerializer.Serialize(LoadSources()));
        obj["payloads"] = JsonNode.Parse(JsonSerializer.Serialize(LoadPayloadMeta()));
        obj["state"]    = LoadUiState();

        var profiles = new JsonObject();
        foreach (var p in ListProfiles())
        {
            var content = ReadProfile(p);
            if (content != null) profiles[p] = content;
        }
        obj["profiles"] = profiles;
        return obj;
    }

    public static void RestoreSelective(JsonObject backup, bool sources, bool payloads,
        bool profiles, bool settings, bool flows, string mode)
    {
        if (sources && backup["sources"] is JsonArray srcArr)
        {
            var list = srcArr.Deserialize<List<SourceEntry>>() ?? [];
            if (mode == "replace") SaveSources(list);
            else
            {
                var existing = LoadSources();
                foreach (var s in list)
                    if (!existing.Any(e => e.Repo == s.Repo))
                        existing.Add(s);
                SaveSources(existing);
            }
        }

        if (payloads && backup["payloads"] is JsonObject pmObj)
        {
            var imported = pmObj.Deserialize<Dictionary<string, PayloadMeta>>() ?? [];
            if (mode == "replace") SavePayloadMeta(imported);
            else
            {
                var existing = LoadPayloadMeta();
                foreach (var kv in imported)
                    existing[kv.Key] = kv.Value;
                SavePayloadMeta(existing);
            }
        }

        if (profiles && backup["profiles"] is JsonObject profObj)
        {
            foreach (var kv in profObj.AsObject())
                if (kv.Value?.GetValueKind() == System.Text.Json.JsonValueKind.String)
                    WriteProfile(kv.Key, kv.Value.GetValue<string>());
        }

        if (settings && backup["state"] is JsonObject stateObj)
            SaveUiState(stateObj);
    }

    public static void FactoryReset()
    {
        var backup = BuildBackup();
        var ts     = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        SaveRaw(Path.Combine(AppPaths.DataDir, $"backup_{ts}.json"), backup);

        File.Delete(AppPaths.DevicesJson);
        File.Delete(AppPaths.SourcesJson);
        File.Delete(AppPaths.PayloadsJson);
        File.Delete(AppPaths.StateJson);
        foreach (var p in ListProfiles()) DeleteProfile(p);
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static string? SafeProfilePath(string name)
    {
        var clean = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(clean)) return null;
        if (!clean.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) clean += ".txt";
        return Path.Combine(AppPaths.ProfilesDir, clean);
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public record DeviceEntry(string Id, string Name, string Ip);

public class SourceEntry
{
    public string Repo        { get; set; } = "";
    public string Filter      { get; set; } = "";
    public string SourceType  { get; set; } = "auto";
    public string Folder      { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public class PayloadMeta
{
    public string Repo           { get; set; } = "";
    public string Version        { get; set; } = "";
    public string DownloadUrl    { get; set; } = "";
    public string PublishedAt    { get; set; } = "";
    public string AssetUpdatedAt { get; set; } = "";
    public long   AssetSize      { get; set; }
    public long   ReleaseId      { get; set; }
    public List<VersionEntry> AllVersions { get; set; } = [];
}

public class VersionEntry
{
    public string Tag         { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public long   AssetSize   { get; set; }
}

public class PayloadFile
{
    public string Name          { get; set; } = "";
    public long   Size          { get; set; }
    public DateTime Modified    { get; set; }
    public string Repo          { get; set; } = "";
    public string Version       { get; set; } = "";
    public string DownloadUrl   { get; set; } = "";
    public string PublishedAt   { get; set; } = "";
    public string AssetUpdatedAt { get; set; } = "";
    public long   AssetSize     { get; set; }
    public long   ReleaseId     { get; set; }
    public List<VersionEntry> AllVersions { get; set; } = [];
}
