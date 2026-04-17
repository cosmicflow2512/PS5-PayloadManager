using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Sources;

namespace PS5AutoPayloadTool.Modules.Payloads;

public class PayloadManager(GitHubClient github)
{
    // ZIP-sourced payloads use this URL convention:
    //   zip:{zipBrowserDownloadUrl}|{entryFullName}
    // so DownloadAsync knows to download the archive and extract the specific entry.
    private const string ZipUrlPrefix    = "zip:";
    private const char   ZipUrlSeparator = '|';

    // ── Scan ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a source and returns (name, downloadUrl, version, size, hash?) tuples
    /// for all payload files found.  ZIP release assets are transparently
    /// unpacked: every .elf / .bin / .lua inside the archive is returned as
    /// a separate entry with a <c>zip:</c>-prefixed download URL.
    /// Hash is the git blob SHA for folder sources; null for release/ZIP sources.
    /// </summary>
    public async Task<List<(string Name, string DownloadUrl, string Version, long Size, string? Hash)>> ScanSourceAsync(
        SourceConfig source,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<(string, string, string, long, string?)>();

        if (source.Type == "release")
        {
            var releases = await github.GetReleasesAsync(source.Owner, source.Repo);
            foreach (var release in releases)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var asset in release.Assets)
                {
                    if (GitHubClient.IsPayloadFile(asset.Name))
                    {
                        if (!string.IsNullOrEmpty(source.Filter) &&
                            !MatchFilter(asset.Name, source.Filter)) continue;
                        results.Add((asset.Name, asset.BrowserDownloadUrl, release.TagName, asset.Size, null));
                    }
                    else if (GitHubClient.IsZipFile(asset.Name))
                    {
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
                                results.Add((entryName, zipUrl, release.TagName, entrySize, null));
                                anyPayload = true;
                            }
                            if (!anyPayload)
                                progress?.Report($"No valid payloads found in {asset.Name}");
                        }
                        catch (Exception ex)
                        {
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
                results.Add((file.Name, file.DownloadUrl, "folder", file.Size,
                    string.IsNullOrEmpty(file.Sha) ? null : file.Sha));
            }
        }

        progress?.Report($"Found {results.Count} payload(s) in {source.DisplayName}");
        return results;
    }

    // ── Download ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a payload to cache and copies it to the active payloads directory.
    /// Handles both direct URLs and <c>zip:</c>-prefixed URLs for archive assets.
    /// When extracting from a ZIP, all payload files in the archive are cached at
    /// once so sibling payloads don't require a second ZIP download.
    /// </summary>
    public async Task DownloadAsync(
        AppConfig config,
        string name,
        string downloadUrl,
        string version,
        string sourceUrl,
        IProgress<(long, long)>? progress = null,
        CancellationToken ct = default)
    {
        var versionDir = Path.Combine(AppPaths.CacheDir, name, version);
        Directory.CreateDirectory(versionDir);
        var cachePath = Path.Combine(versionDir, name);

        if (!File.Exists(cachePath))
        {
            if (downloadUrl.StartsWith(ZipUrlPrefix))
                await DownloadFromZipAsync(downloadUrl, version, cachePath, progress, ct);
            else
                await github.DownloadFileAsync(downloadUrl, cachePath, progress, ct);
        }

        if (!File.Exists(cachePath))
            throw new InvalidOperationException($"Could not obtain '{name}' (cache path missing after download).");

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

        meta.Version   = version;
        meta.LocalPath = activePath;
        meta.SourceUrl = sourceUrl;
        meta.Size      = new FileInfo(activePath).Length;
        meta.FileHash  = ComputeSha256(activePath);
    }

    /// <summary>
    /// Resolves the download URL by rescanning the source, then delegates to DownloadAsync.
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
        var match = found.FirstOrDefault(f =>
            f.Name == name &&
            (f.Version == version || version == "latest" || source.Type == "folder"));

        if (match == default)
            throw new InvalidOperationException($"Version '{version}' of '{name}' not found in source.");

        await DownloadAsync(config, name, match.DownloadUrl, version, sourceUrl, progress, ct);
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
            throw new InvalidOperationException(
                "The requested payload was not found inside the downloaded ZIP archive.");
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
