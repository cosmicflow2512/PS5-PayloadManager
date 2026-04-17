using System.IO;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using Log = PS5AutoPayloadTool.Modules.Core.LogService;

namespace PS5AutoPayloadTool.Modules.Payloads;

/// <summary>
/// Manages payload downloads, update detection, and local file operations.
/// Views delegate all business logic here.
/// </summary>
public class PayloadService(PayloadManager manager)
{
    // ── Local payload management ─────────────────────────────────────────────

    public string AddLocal(AppConfig config, string filePath)
    {
        var name = Path.GetFileName(filePath);
        var dest = Path.Combine(AppPaths.PayloadsDir, name);
        Directory.CreateDirectory(AppPaths.PayloadsDir);
        File.Copy(filePath, dest, overwrite: true);

        if (!config.PayloadMeta.ContainsKey(name))
            config.PayloadMeta[name] = new PayloadMeta
            {
                Version   = "local",
                Versions  = new() { "local" },
                LocalPath = dest,
                Size      = new FileInfo(dest).Length
            };
        else
        {
            var m = config.PayloadMeta[name];
            m.LocalPath = dest;
            m.Size      = new FileInfo(dest).Length;
            if (!m.Versions.Contains("local")) m.Versions.Insert(0, "local");
        }

        return name;
    }

    public void Delete(AppConfig config, string name)
    {
        var path = Path.Combine(AppPaths.PayloadsDir, name);
        if (File.Exists(path)) try { File.Delete(path); } catch { }
        config.PayloadMeta.Remove(name);
    }

    // ── Update detection ─────────────────────────────────────────────────────

    /// <summary>
    /// Scans all sources in parallel.  Detects updates via (in priority order):
    /// 1. version tag change,
    /// 2. git blob SHA change (folder sources),
    /// 3. file size change,
    /// 4. published_at timestamp newer than local file mtime (release sources).
    /// Sets HasUpdateAvailable and SourceNotAvailable on each PayloadMeta.
    /// </summary>
    public async Task CheckUpdatesAsync(
        AppConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var tasks = config.Sources.Select(source => Task.Run(async () =>
        {
            try
            {
                var found = await manager.ScanSourceAsync(source, null, ct);
                foreach (var r in found)
                {
                    if (!config.PayloadMeta.ContainsKey(r.Name))
                    {
                        config.PayloadMeta[r.Name] = new PayloadMeta
                        {
                            SourceUrl   = source.Url,
                            Versions    = new() { r.Version },
                            Version     = r.Version,
                            Size        = r.Size,
                            PublishedAt = r.PublishedAt
                        };
                        continue;
                    }

                    var meta = config.PayloadMeta[r.Name];
                    if (!meta.Versions.Contains(r.Version)) meta.Versions.Add(r.Version);
                    if (!string.IsNullOrEmpty(r.PublishedAt)) meta.PublishedAt = r.PublishedAt;

                    bool versionChanged = r.Version != "folder" && meta.Version != r.Version;
                    bool hashChanged    = r.Hash != null
                                      && !string.IsNullOrEmpty(meta.FileHash)
                                      && r.Hash != meta.FileHash;
                    bool sizeChanged    = !versionChanged
                                      && r.Size > 0 && meta.Size > 0
                                      && r.Size != meta.Size;
                    bool timestampNewer = false;

                    if (!versionChanged && !hashChanged && !sizeChanged
                        && !string.IsNullOrEmpty(r.PublishedAt)
                        && File.Exists(meta.LocalPath))
                    {
                        if (DateTime.TryParse(r.PublishedAt, null,
                                System.Globalization.DateTimeStyles.RoundtripKind,
                                out var remoteDate))
                        {
                            var localMtime = File.GetLastWriteTimeUtc(meta.LocalPath);
                            // 60-second tolerance to ignore minor clock drift
                            timestampNewer = remoteDate.ToUniversalTime() > localMtime.AddSeconds(60);
                        }
                    }

                    meta.SourceNotAvailable = false;
                    meta.HasUpdateAvailable = versionChanged || hashChanged || sizeChanged || timestampNewer;
                    Log.Debug("PayloadService",
                        $"{r.Name}: local={meta.Version} remote={r.Version} " +
                        $"update={meta.HasUpdateAvailable}" +
                        (hashChanged    ? " [hash changed]"    : "") +
                        (sizeChanged    ? " [size changed]"    : "") +
                        (timestampNewer ? " [newer timestamp]" : ""));
                }
                progress?.Report($"Checked {source.DisplayName}.");
            }
            catch (Exception ex)
            {
                Log.Error("PayloadService", $"Update check failed for {source.DisplayName}: {ex.Message}");
                progress?.Report($"Error checking {source.DisplayName}: {ex.Message}");
            }
        }, ct)).ToList();

        await Task.WhenAll(tasks);

        // Mark payloads whose source URL is no longer in config
        foreach (var (name, meta) in config.PayloadMeta)
        {
            if (!string.IsNullOrEmpty(meta.SourceUrl)
                && !config.Sources.Any(s => s.Url == meta.SourceUrl))
            {
                meta.SourceNotAvailable = true;
                meta.HasUpdateAvailable = false;
                Log.Warn("PayloadService", $"{name}: source not in config ({meta.SourceUrl})");
            }
        }
    }

    // ── Targeted download ────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a specific version ("Latest" picks the newest) from the
    /// payload's registered source.  Returns null on success, error string on failure.
    /// </summary>
    public async Task<string?> DownloadVersionAsync(
        AppConfig config,
        string name,
        string version,
        string sourceUrl,
        IProgress<(long, long)>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var source = config.Sources.FirstOrDefault(s => s.Url == sourceUrl);
            if (source == null)
            {
                Log.Error("PayloadService", $"Download failed for {name}: source not available ({sourceUrl})");
                if (config.PayloadMeta.TryGetValue(name, out var m))
                {
                    m.SourceNotAvailable = true;
                    m.HasUpdateAvailable = false;
                }
                return "Source not available.";
            }
            Log.Info("PayloadService", $"Downloading {name} — version={version}  source={source.DisplayName}");

            var found = await manager.ScanSourceAsync(source, null, ct);

            bool wantLatest = version is "Latest" or "latest";
            var match = wantLatest
                ? found.FirstOrDefault(f => f.Name == name)
                : found.FirstOrDefault(f => f.Name == name && f.Version == version)
                  ?? found.FirstOrDefault(f => f.Name == name);

            if (match == null) return "No valid payload found in release.";

            await manager.DownloadAsync(
                config, name, match.DownloadUrl, match.Version, sourceUrl, progress, ct);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ── Bulk update ──────────────────────────────────────────────────────────

    public async Task<List<(string Name, bool Success, string Message)>> UpdateAllAsync(
        AppConfig config,
        IProgress<(string Name, double Pct)>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<(string, bool, string)>();
        var names   = config.PayloadMeta.Keys.ToList();

        for (int i = 0; i < names.Count; i++)
        {
            var name = names[i];
            progress?.Report((name, (double)i / names.Count * 100));

            if (!config.PayloadMeta.TryGetValue(name, out var meta)) continue;
            if (string.IsNullOrEmpty(meta.SourceUrl)) continue;

            var source = config.Sources.FirstOrDefault(s => s.Url == meta.SourceUrl);
            if (source == null) continue;

            try
            {
                var found  = await manager.ScanSourceAsync(source, null, ct);
                var latest = found.FirstOrDefault(f => f.Name == name);
                if (latest == null) continue;

                await manager.DownloadAsync(
                    config, name, latest.DownloadUrl, latest.Version, source.Url, null, ct);

                meta.Version            = latest.Version;
                meta.HasUpdateAvailable = false;
                results.Add((name, true, $"Updated to {latest.Version}"));
            }
            catch (Exception ex)
            {
                results.Add((name, false, ex.Message));
            }
        }

        progress?.Report(("", 100));
        return results;
    }

    // ── Post-import resolution ───────────────────────────────────────────────

    public async Task ResolveAfterImportAsync(
        AppConfig config,
        HashSet<string> alreadyRestored,
        IProgress<(string Name, string Message, double Pct)>? progress = null,
        CancellationToken ct = default)
    {
        var missing = config.PayloadMeta
            .Where(kv => !alreadyRestored.Contains(kv.Key)
                      && !File.Exists(Path.Combine(AppPaths.PayloadsDir, kv.Key)))
            .ToList();

        if (missing.Count == 0) return;

        for (int i = 0; i < missing.Count; i++)
        {
            var (name, meta) = missing[i];
            double pct = (double)(i + 1) / missing.Count * 100;

            var localFile = Path.Combine(AppPaths.PayloadsDir, name);
            if (File.Exists(localFile))
            {
                meta.LocalPath = localFile;
                progress?.Report((name, $"{name}: found locally.", pct));
                continue;
            }

            if (string.IsNullOrEmpty(meta.SourceUrl))
            {
                progress?.Report((name, $"{name}: no source — skipped.", pct));
                continue;
            }

            var source = config.Sources.FirstOrDefault(s => s.Url == meta.SourceUrl);
            if (source == null)
            {
                meta.SourceNotAvailable = true;
                progress?.Report((name, $"{name}: source not in config — skipped.", pct));
                continue;
            }

            try
            {
                var found = await manager.ScanSourceAsync(source, null, ct);
                var match = found.FirstOrDefault(f => f.Name == name && f.Version == meta.Version)
                         ?? found.FirstOrDefault(f => f.Name == name);

                if (match == null)
                {
                    progress?.Report((name, $"{name}: not found in source — skipped.", pct));
                    continue;
                }

                await manager.DownloadAsync(
                    config, name, match.DownloadUrl, match.Version, source.Url, null, ct);

                var msg = match.Version != meta.Version && !string.IsNullOrEmpty(meta.Version)
                    ? $"{name}: updated to {match.Version} (requested {meta.Version})."
                    : $"{name}: restored ({match.Version}).";

                progress?.Report((name, msg, pct));
            }
            catch (Exception ex)
            {
                progress?.Report((name, $"{name}: download failed — {ex.Message}", pct));
            }
        }
    }
}
