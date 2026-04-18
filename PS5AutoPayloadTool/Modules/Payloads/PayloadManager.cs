using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Sources;
using Log = PS5AutoPayloadTool.Modules.Core.LogService;

namespace PS5AutoPayloadTool.Modules.Payloads;

/// <summary>Single payload entry returned by ScanSourceAsync.</summary>
public record ScanResult(
    string  Name,
    string  DownloadUrl,
    string  Version,
    long    Size,
    string? Hash,
    string  PublishedAt = "");

public class PayloadManager(GitHubClient github)
{
    // ZIP-sourced payloads use this URL convention:
    //   zip:{zipBrowserDownloadUrl}|{entryFullName}
    private const string ZipUrlPrefix    = "zip:";
    private const char   ZipUrlSeparator = '|';

    // ── Scan ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a source and returns a <see cref="ScanResult"/> for every payload
    /// file found. ZIP release assets are transparently unpacked: every
    /// .elf / .bin / .lua inside is returned as a separate entry with a
    /// <c>zip:</c>-prefixed download URL.
    /// </summary>
    public async Task<List<ScanResult>> ScanSourceAsync(
        SourceConfig source,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<ScanResult>();
        Log.Debug("PayloadManager", $"Scan started: {source.Owner}/{source.Repo} ({source.Type})");

        if (source.Type == "release")
        {
            var releases = (await github.GetReleasesAsync(source.Owner, source.Repo)).Take(3).ToList();
            Log.Debug("PayloadManager", $"{source.Owner}/{source.Repo}: {releases.Count} release(s) fetched (limited to 3)");
            foreach (var release in releases)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var asset in release.Assets)
                {
                    if (GitHubClient.IsPayloadFile(asset.Name))
                    {
                        if (!string.IsNullOrEmpty(source.Filter) &&
                            !MatchFilter(asset.Name, source.Filter)) continue;
                        results.Add(new ScanResult(
                            asset.Name, asset.BrowserDownloadUrl,
                            release.TagName, asset.Size, null, release.PublishedAt));
                    }
                    else if (GitHubClient.IsZipFile(asset.Name))
                    {
                        Log.Debug("PayloadManager", $"ZIP detected: {asset.Name} in {release.TagName}");
                        progress?.Report($"Scanning archive {asset.Name}…");
                        try
                        {
                            var entries = await PeekZipAsync(asset.BrowserDownloadUrl, ct);
                            bool anyPayload = false;
                            foreach (var (entryName, entryPath, entrySize) in entries)
                            {
                                if (!GitHubClient.IsPayloadFile(entryName)) continue;
                                if (!string.IsNullOrEmpty(source.Filter) &&
                                    !MatchFilter(entryName, source.Filter)) continue;
                                var zipUrl = $"{ZipUrlPrefix}{asset.BrowserDownloadUrl}{ZipUrlSeparator}{entryPath}";
                                results.Add(new ScanResult(
                                    entryName, zipUrl,
                                    release.TagName, entrySize, null, release.PublishedAt));
                                Log.Debug("PayloadManager", $"  ZIP entry: {entryName} ({entrySize} bytes)");
                                anyPayload = true;
                            }
                            if (!anyPayload)
                            {
                                Log.Warn("PayloadManager", $"No valid payloads found in {asset.Name}");
                                progress?.Report($"No valid payloads found in {asset.Name}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("PayloadManager", $"Could not scan {asset.Name}: {ex.Message}");
                            progress?.Report($"Warning: could not scan {asset.Name}: {ex.Message}");
                        }
                    }
                }
            }
        }
        else // folder
        {
            var contents = await github.GetFolderContentsAsync(
                source.Owner, source.Repo, source.FolderPath);
            foreach (var file in contents)
            {
                ct.ThrowIfCancellationRequested();
                if (file.DownloadUrl == null) continue;
                if (!GitHubClient.IsPayloadFile(file.Name)) continue;
                if (!string.IsNullOrEmpty(source.Filter) &&
                    !MatchFilter(file.Name, source.Filter)) continue;
                results.Add(new ScanResult(
                    file.Name, file.DownloadUrl, "folder", file.Size,
                    string.IsNullOrEmpty(file.Sha) ? null : file.Sha));
            }
        }

        Log.Info("PayloadManager", $"Scan complete: {results.Count} payload(s) in {source.DisplayName}");
        progress?.Report($"Found {results.Count} payload(s) in {source.DisplayName}");
        return results;
    }

    // ── Download ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a payload to cache and copies it to the active payloads directory.
    /// </summary>
    public async Task DownloadAsync(
        AppConfig config,
        string name,
        string downloadUrl,
        string version,
        string sourceUrl,
        string publishedAt = "",
        IProgress<(long, long)>? progress = null,
        CancellationToken ct = default)
    {
        var versionDir = Path.Combine(AppPaths.CacheDir, name, version);
        Directory.CreateDirectory(versionDir);
        var cachePath = Path.Combine(versionDir, name);

        Log.Info("PayloadManager", $"Downloading {name} v{version}");

        if (!File.Exists(cachePath))
        {
            bool isZip = downloadUrl.StartsWith(ZipUrlPrefix);
            Log.Debug("PayloadManager", $"  Source: {(isZip ? "ZIP archive" : "direct URL")}");
            if (isZip)
                await DownloadFromZipAsync(downloadUrl, version, cachePath, progress, ct);
            else
                await github.DownloadFileAsync(downloadUrl, cachePath, progress, ct);
        }
        else
        {
            Log.Debug("PayloadManager", $"  Cache hit: {cachePath}");
        }

        if (!File.Exists(cachePath))
        {
            Log.Error("PayloadManager", $"Download failed: '{name}' not in cache after download");
            throw new InvalidOperationException($"Could not obtain '{name}' (cache path missing after download).");
        }

        Directory.CreateDirectory(AppPaths.PayloadsDir);
        var activePath = Path.Combine(AppPaths.PayloadsDir, name);
        File.Copy(cachePath, activePath, overwrite: true);

        if (!config.PayloadMeta.TryGetValue(name, out var meta))
        {
            meta = new PayloadMeta { SourceUrl = sourceUrl };
            config.PayloadMeta[name] = meta;
        }

        if (!meta.Versions.Contains(version))
            meta.Versions.Add(version);

        meta.Version            = version;
        meta.LocalPath          = activePath;
        meta.SourceUrl          = sourceUrl;
        meta.Size               = new FileInfo(activePath).Length;
        meta.FileHash           = ComputeSha256(activePath);
        meta.HasUpdateAvailable = false;
        meta.SourceNotAvailable = false;
        if (!string.IsNullOrEmpty(publishedAt))
            meta.PublishedAt = publishedAt;
        Log.Info("PayloadManager", $"Saved {name} v{version}  ({meta.Size} bytes  hash={meta.FileHash[..8]})");
    }

    /// <summary>
    /// Resolves the download URL by rescanning the source, then delegates to DownloadAsync.
    /// "latest" / "Latest" always picks the first (most recent) scan result for that name.
    /// </summary>
    public async Task DownloadPayloadAsync(
        AppConfig config,
        string name,
        string version,
        string sourceUrl,
        IProgress<(long, long)>? progress = null,
        CancellationToken ct = default)
    {
        var source = config.Sources.FirstOrDefault(s => s.Url == sourceUrl);
        if (source == null)
            throw new InvalidOperationException($"Source not found for URL: {sourceUrl}");

        var found = await ScanSourceAsync(source, null, ct);

        bool wantLatest = version is "latest" or "Latest";
        var match = wantLatest
            ? found.FirstOrDefault(f => f.Name == name)
            : found.FirstOrDefault(f => f.Name == name &&
                  (f.Version == version || source.Type == "folder"))
              ?? found.FirstOrDefault(f => f.Name == name);

        if (match == null)
        {
            Log.Error("PayloadManager", $"No valid payload found in release for '{name}' (source: {sourceUrl})");
            throw new InvalidOperationException(
                $"No valid payload found in release for '{name}'.");
        }

        await DownloadAsync(config, name, match.DownloadUrl, match.Version, sourceUrl, match.PublishedAt, progress, ct);
    }

    // ── ZIP helpers ───────────────────────────────────────────────────────────

    private async Task<List<(string Name, string FullPath, long Size)>> PeekZipAsync(
        string zipUrl, CancellationToken ct)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"ps5apt_{Guid.NewGuid():N}.zip");
        try
        {
            await github.DownloadFileAsync(zipUrl, tempPath, null, ct);
            using var zip = ZipFile.OpenRead(tempPath);
            return zip.Entries
                .Where(e => e.Length > 0)
                .Select(e => (e.Name, e.FullName, e.Length))
                .ToList();
        }
        finally
        {
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { }
        }
    }

    private async Task DownloadFromZipAsync(
        string zipDownloadUrl,
        string version,
        string requiredCachePath,
        IProgress<(long, long)>? progress,
        CancellationToken ct)
    {
        var inner  = zipDownloadUrl[ZipUrlPrefix.Length..];
        var sepIdx = inner.IndexOf(ZipUrlSeparator);
        if (sepIdx < 0)
            throw new InvalidOperationException("Malformed ZIP download URL (missing separator).");
        var zipUrl = inner[..sepIdx];

        var tempZip = Path.Combine(Path.GetTempPath(), $"ps5apt_{Guid.NewGuid():N}.zip");
        try
        {
            await github.DownloadFileAsync(zipUrl, tempZip, progress, ct);
            using var zip = ZipFile.OpenRead(tempZip);

            foreach (var entry in zip.Entries)
            {
                if (entry.Length == 0) continue;
                if (!GitHubClient.IsPayloadFile(entry.Name)) continue;

                var entryVersionDir = Path.Combine(AppPaths.CacheDir, entry.Name, version);
                Directory.CreateDirectory(entryVersionDir);
                var entryCachePath = Path.Combine(entryVersionDir, entry.Name);

                if (!File.Exists(entryCachePath))
                    entry.ExtractToFile(entryCachePath, overwrite: false);
            }
        }
        finally
        {
            if (File.Exists(tempZip))
                try { File.Delete(tempZip); } catch { }
        }

        if (!File.Exists(requiredCachePath))
        {
            Log.Error("PayloadManager", $"Target not found in ZIP: {Path.GetFileName(requiredCachePath)}");
            throw new InvalidOperationException(
                "The requested payload was not found inside the downloaded ZIP archive.");
        }
    }

    // ── Hash ──────────────────────────────────────────────────────────────────

    private static string ComputeSha256(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch { return ""; }
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private static bool MatchFilter(string name, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;

        if (filter.StartsWith('*'))
        {
            var suffix = filter[1..];
            return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (filter.EndsWith('*'))
        {
            var prefix = filter[..^1];
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
