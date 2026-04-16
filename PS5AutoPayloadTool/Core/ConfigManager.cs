using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public static class ConfigManager
{
    // Write options: pretty, null fields omitted
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Read options for our own format: case-insensitive, numbers from strings
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppConfig Load()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.ConfigFile)) return new AppConfig();
            var json = File.ReadAllText(AppPaths.ConfigFile);
            var config = JsonSerializer.Deserialize<AppConfig>(json, ReadOpts) ?? new AppConfig();
            // JSON null can override C# field initializers — guard every collection
            config.Sources      ??= new();
            config.PayloadMeta  ??= new();
            config.Profiles     ??= new();
            config.Devices      ??= new();
            config.State        ??= new();
            config.Ports        ??= new();
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
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(config, WriteOpts));
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
    /// Creates a portable backup ZIP at <paramref name="destPath"/>.
    /// Always contains <c>config.json</c>.
    /// When <paramref name="includePayloads"/> is true, every file in
    /// <see cref="AppPaths.PayloadsDir"/> is added under <c>payloads/</c>.
    /// </summary>
    public static void ExportBackupZip(AppConfig config, bool includePayloads, string destPath)
    {
        SyncProfilesFromDisk(config);
        var json = JsonSerializer.Serialize(config, WriteOpts);

        using var zip = ZipFile.Open(destPath, ZipArchiveMode.Create);

        // config.json
        var cfgEntry = zip.CreateEntry("config.json");
        using (var w = new StreamWriter(cfgEntry.Open()))
            w.Write(json);

        if (includePayloads && Directory.Exists(AppPaths.PayloadsDir))
        {
            foreach (var file in Directory.GetFiles(AppPaths.PayloadsDir))
            {
                var name  = Path.GetFileName(file);
                var entry = zip.CreateEntry($"payloads/{name}");
                using var dst = entry.Open();
                using var src = File.OpenRead(file);
                src.CopyTo(dst);
            }
        }
    }

    /// <summary>
    /// Imports a backup ZIP created by <see cref="ExportBackupZip"/>.
    /// Extracts payload files to <see cref="AppPaths.PayloadsDir"/> and
    /// updates <c>LocalPath</c> in each restored <see cref="PayloadMeta"/>.
    /// Returns the parsed config, the list of restored payload names, and an
    /// optional error string.
    /// </summary>
    public static (AppConfig? Config, string[] Restored, string? Error) ImportFromBackupZip(string zipPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            var cfgEntry = zip.GetEntry("config.json");
            if (cfgEntry == null)
                return (null, Array.Empty<string>(), "No config.json found in this ZIP.");

            string json;
            using (var r = new StreamReader(cfgEntry.Open()))
                json = r.ReadToEnd();

            var (config, err) = Import(json);
            if (config == null) return (null, Array.Empty<string>(), err);

            // Restore payload files
            var restored = new List<string>();
            Directory.CreateDirectory(AppPaths.PayloadsDir);

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("payloads/") || entry.Length == 0) continue;
                var name = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(name)) continue;

                var dest = Path.Combine(AppPaths.PayloadsDir, name);
                entry.ExtractToFile(dest, overwrite: true);
                restored.Add(name);

                if (config.PayloadMeta.TryGetValue(name, out var meta))
                    meta.LocalPath = dest;
            }

            return (config, restored.ToArray(), null);
        }
        catch (Exception ex)
        {
            return (null, Array.Empty<string>(), ex.Message);
        }
    }

    /// <summary>
    /// Import a config JSON (our native format OR PS5AutopayloadHA backup).
    /// Returns (config, null) on success or (null, errorMessage) on failure.
    /// </summary>
    public static (AppConfig? Config, string? Error) Import(string json)
    {
        try
        {
            var config = ParseAnyFormat(json);
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

    // ── Format-agnostic parser ────────────────────────────────────────────────

    /// <summary>
    /// Parses both our native format and PS5AutopayloadHA backup format.
    ///
    /// Differences handled:
    ///   sources[].repo        "owner/repo"     → url  "https://github.com/owner/repo"
    ///   sources[].source_type "releases"/"folder" → type "release"/"folder"
    ///   sources[].folder      "path"           → folder_path "path"
    ///   payload_meta[].versions [{tag:"v1"}]   → ["v1"]  (HA uses tag objects)
    /// </summary>
    private static AppConfig? ParseAnyFormat(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var config = new AppConfig();

        // ── version ──────────────────────────────────────────────────────────
        if (root.TryGetProperty("version", out var vEl))
            config.Version = vEl.ValueKind == JsonValueKind.String
                ? (int.TryParse(vEl.GetString(), out var vi) ? vi : 1)
                : vEl.GetInt32();

        // ── state ────────────────────────────────────────────────────────────
        if (root.TryGetProperty("state", out var stateEl) && stateEl.ValueKind == JsonValueKind.Object)
        {
            if (stateEl.TryGetProperty("ps5_ip", out var ip))         config.State.PS5Ip             = ip.GetString()  ?? "";
            if (stateEl.TryGetProperty("github_token", out var tok))  config.State.GitHubToken       = tok.GetString() ?? "";
            if (stateEl.TryGetProperty("builder_profile_name", out var bpn)) config.State.BuilderProfileName = bpn.GetString() ?? "";
            if (stateEl.TryGetProperty("payload_filter", out var pf)) config.State.PayloadFilter     = pf.GetString()  ?? "";
            if (stateEl.TryGetProperty("advanced_mode", out var am) && am.ValueKind == JsonValueKind.True)
                config.State.AdvancedMode = true;
            if (stateEl.TryGetProperty("favorites", out var fav))
                config.State.Favorites = ParseStringList(fav);
            if (stateEl.TryGetProperty("builder_steps", out var bsEl))
                config.State.BuilderSteps = ParseBuilderSteps(bsEl);
        }
        else
        {
            // flat HA format: ps5_ip / github_token / builder_steps at root
            if (root.TryGetProperty("ps5_ip", out var ip))           config.State.PS5Ip       = ip.GetString()  ?? "";
            if (root.TryGetProperty("github_token", out var tok))    config.State.GitHubToken = tok.GetString() ?? "";
            if (root.TryGetProperty("builder_steps", out var bsEl))  config.State.BuilderSteps = ParseBuilderSteps(bsEl);
        }

        // ── devices ──────────────────────────────────────────────────────────
        if (root.TryGetProperty("devices", out var devicesEl) && devicesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in devicesEl.EnumerateArray())
            {
                var dev = new DeviceConfig();
                if (d.TryGetProperty("ip", out var dip)) dev.Ip = dip.GetString() ?? "";
                config.Devices.Add(dev);
            }
        }

        // Keep devices[0] in sync with state.PS5Ip
        if (config.Devices.Count == 0 && !string.IsNullOrWhiteSpace(config.State.PS5Ip))
            config.Devices.Add(new DeviceConfig { Ip = config.State.PS5Ip });

        // ── sources ──────────────────────────────────────────────────────────
        if (root.TryGetProperty("sources", out var srcEl) && srcEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in srcEl.EnumerateArray())
            {
                var src = new SourceConfig();

                // url (native) or repo (HA: "owner/repo")
                if (s.TryGetProperty("url", out var url))
                    src.Url = url.GetString() ?? "";
                else if (s.TryGetProperty("repo", out var repo))
                    src.Url = $"https://github.com/{repo.GetString()}";

                // type (native: "release"/"folder") or source_type (HA: "releases"/"folder"/"auto")
                if (s.TryGetProperty("type", out var typ))
                    src.Type = typ.GetString() ?? "release";
                else if (s.TryGetProperty("source_type", out var st))
                    src.Type = (st.GetString() ?? "releases") == "releases" ? "release" : "folder";

                if (s.TryGetProperty("filter", out var flt))     src.Filter     = flt.GetString() ?? "";
                if (s.TryGetProperty("folder_path", out var fp)) src.FolderPath = fp.GetString()  ?? "";
                else if (s.TryGetProperty("folder", out var fo)) src.FolderPath = fo.GetString()  ?? "";

                if (!string.IsNullOrEmpty(src.Url))
                    config.Sources.Add(src);
            }
        }

        // ── payload_meta ─────────────────────────────────────────────────────
        if (root.TryGetProperty("payload_meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in metaEl.EnumerateObject())
            {
                var pm = new PayloadMeta();

                if (entry.Value.TryGetProperty("version", out var v))    pm.Version   = v.GetString() ?? "";
                if (entry.Value.TryGetProperty("source_url", out var su)) pm.SourceUrl = su.GetString() ?? "";
                if (entry.Value.TryGetProperty("local_path", out var lp)) pm.LocalPath = lp.GetString() ?? "";
                if (entry.Value.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
                    pm.Size = sz.GetInt64();

                if (entry.Value.TryGetProperty("versions", out var vers) && vers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ver in vers.EnumerateArray())
                    {
                        // Native format: "v2.4.0"
                        // HA format:     {"tag": "v2.4.0"}
                        if (ver.ValueKind == JsonValueKind.String)
                            pm.Versions.Add(ver.GetString()!);
                        else if (ver.ValueKind == JsonValueKind.Object &&
                                 ver.TryGetProperty("tag", out var tag))
                            pm.Versions.Add(tag.GetString()!);
                    }
                }

                config.PayloadMeta[entry.Name] = pm;
            }
        }

        // ── profiles ─────────────────────────────────────────────────────────
        if (root.TryGetProperty("profiles", out var profEl) && profEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in profEl.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    config.Profiles[p.Name] = p.Value.GetString() ?? "";
        }

        // ── ports ─────────────────────────────────────────────────────────────
        if (root.TryGetProperty("ports", out var portsEl) && portsEl.ValueKind == JsonValueKind.Object)
        {
            if (portsEl.TryGetProperty("elf_port", out var ep) && ep.ValueKind == JsonValueKind.Number)
                config.Ports.ElfPort = ep.GetInt32();
            if (portsEl.TryGetProperty("lua_port", out var lp) && lp.ValueKind == JsonValueKind.Number)
                config.Ports.LuaPort = lp.GetInt32();
            if (portsEl.TryGetProperty("bin_port", out var bp) && bp.ValueKind == JsonValueKind.Number)
                config.Ports.BinPort = bp.GetInt32();
        }

        return config;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<string> ParseStringList(JsonElement el)
    {
        var list = new List<string>();
        if (el.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in el.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        return list;
    }

    private static List<BuilderStep> ParseBuilderSteps(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return new();
        try { return JsonSerializer.Deserialize<List<BuilderStep>>(el.GetRawText(), ReadOpts) ?? new(); }
        catch { return new(); }
    }

    // ── Profile sync ─────────────────────────────────────────────────────────

    private static void SyncProfilesToDisk(AppConfig config)
    {
        AppPaths.EnsureDirectories();
        foreach (var kv in config.Profiles)
            File.WriteAllText(Path.Combine(AppPaths.ProfilesDir, kv.Key), kv.Value);
    }

    private static void SyncProfilesFromDisk(AppConfig config)
    {
        AppPaths.EnsureDirectories();
        if (!Directory.Exists(AppPaths.ProfilesDir)) return;
        foreach (var file in Directory.GetFiles(AppPaths.ProfilesDir, "*.txt"))
            config.Profiles[Path.GetFileName(file)] = File.ReadAllText(file);
    }
}
