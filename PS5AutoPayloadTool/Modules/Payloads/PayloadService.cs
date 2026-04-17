using System.IO;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;

namespace PS5AutoPayloadTool.Modules.Payloads;

/// <summary>
/// Manages payload downloads, update detection, and local file operations.
/// Views delegate all business logic here — no direct filesystem or network
/// access happens inside UI code-behind.
/// </summary>
public class PayloadService(PayloadManager manager)
{
    // ── Local payload management ─────────────────────────────────────────────

    /// <summary>
    /// Copies a local file into PayloadsDir and registers it in config.
    /// Returns the payload filename.
    /// </summary>
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

    /// <summary>Deletes the payload file and removes it from config.</summary>
    public void Delete(AppConfig config, string name)
    {
        var path = Path.Combine(AppPaths.PayloadsDir, name);
        if (File.Exists(path)) try { File.Delete(path); } catch { }
        config.PayloadMeta.Remove(name);
    }

    // ── Update detection ─────────────────────────────────────────────────────

    /// <summary>
    /// Scans all sources in parallel to detect version and hash changes.
    /// Updates <see cref="PayloadMeta.HasUpdateAvailable"/> for each payload.
    /// Reports per-source status via <paramref name="progress"/>.
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
                foreach (var (name, _, version, size, remoteHash) in found)
                {
                    if (!config.PayloadMeta.ContainsKey(name))
                    {
                        config.PayloadMeta[name] = new PayloadMeta
                            { SourceUrl = source.Url, Versions = new() { version }, Version = version, Size = size };
                    }
                    else
                    {
                        var meta = config.PayloadMeta[name];
                        if (!meta.Versions.Contains(version)) meta.Versions.Add(version);

                        bool versionChanged = version != "folder" && meta.Version != version;
                        bool hashChanged    = remoteHash != null
                                           && !string.IsNullOrEmpty(meta.FileHash)
                                           && remoteHash != meta.FileHash;
                        meta.HasUpdateAvailable = versionChanged || hashChanged;
                    }
                }
                progress?.Report($"Checked {source.DisplayName}.");
            }
            catch (Exception ex)
            {
                progress?.Report($"Error checking {source.DisplayName}: {ex.Message}");
            }
        }, ct)).ToList();

        await Task.WhenAll(tasks);
    }

    // ── Targeted download ────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a specific version of a payload from its registered source.
    /// Returns null on success, or an error message on failure.
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
            if (source == null) return "Source not found.";

            var found = await manager.ScanSourceAsync(source, null, ct);
            var match = found.FirstOrDefault(f => f.Name == name && f.Version == version);
            if (match == default) return "Version not found in source.";

            await manager.DownloadAsync(
                config, name, match.DownloadUrl, version, sourceUrl, progress, ct);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ── Bulk update ──────────────────────────────────────────────────────────

    /// <summary>
    /// Scans each payload's source for the latest version and downloads it.
    /// Reports (payloadName, successFlag, message) per payload via the return value.
    /// Progress callback receives (payloadName, percentComplete).
    /// </summary>
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
                if (latest == default) continue;

                await manager.DownloadAsync(
                    config, name, latest.DownloadUrl, latest.Version, source.Url, null, ct);

                meta.Version           = latest.Version;
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

    /// <summary>
    /// After a config import, downloads any payloads whose local files are absent.
    /// Skips entries in <paramref name="alreadyRestored"/> (extracted from backup ZIP).
    /// Progress reports (payloadName, logMessage, percentComplete).
    /// </summary>
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

            // Re-check: may have been populated as a sibling of another ZIP payload
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
                progress?.Report((name, $"{name}: source not in config — skipped.", pct));
                continue;
            }

            try
            {
                var found = await manager.ScanSourceAsync(source, null, ct);
                var match = found.FirstOrDefault(f => f.Name == name && f.Version == meta.Version);
                if (match == default)
                    match = found.FirstOrDefault(f => f.Name == name);

                if (match == default)
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
