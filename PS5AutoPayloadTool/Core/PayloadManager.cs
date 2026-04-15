using System.IO;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public class PayloadManager(GitHubClient github)
{
    /// <summary>
    /// Scans a source and returns (name, downloadUrl, version, size) tuples
    /// for all payload files found.
    /// </summary>
    public async Task<List<(string Name, string DownloadUrl, string Version, long Size)>> ScanSourceAsync(
        SourceConfig source,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<(string, string, string, long)>();

        if (source.Type == "release")
        {
            var releases = await github.GetReleasesAsync(source.Owner, source.Repo);
            foreach (var release in releases)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var asset in release.Assets)
                {
                    if (!GitHubClient.IsPayloadFile(asset.Name)) continue;
                    if (!string.IsNullOrEmpty(source.Filter) &&
                        !MatchFilter(asset.Name, source.Filter)) continue;
                    results.Add((asset.Name, asset.BrowserDownloadUrl, release.TagName, asset.Size));
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
                results.Add((file.Name, file.DownloadUrl, "folder", file.Size));
            }
        }

        progress?.Report($"Found {results.Count} payload(s) in {source.DisplayName}");
        return results;
    }

    /// <summary>
    /// Downloads a payload to cache and updates payload_meta in config.
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
        // Cache path: CacheDir/name/version/name
        var versionDir = Path.Combine(AppPaths.CacheDir, name, version);
        Directory.CreateDirectory(versionDir);
        var cachePath = Path.Combine(versionDir, name);

        if (!File.Exists(cachePath))
            await github.DownloadFileAsync(downloadUrl, cachePath, progress, ct);

        // Copy to active payloads dir
        Directory.CreateDirectory(AppPaths.PayloadsDir);
        var activePath = Path.Combine(AppPaths.PayloadsDir, name);
        File.Copy(cachePath, activePath, overwrite: true);

        // Update payload_meta
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
    }

    /// <summary>
    /// Legacy overload kept for backward compatibility with PayloadsPage.
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
            (f.Version == version || version == "latest"));

        if (match == default)
            throw new InvalidOperationException($"Version '{version}' of '{name}' not found in source.");

        await DownloadAsync(config, name, match.DownloadUrl, version, sourceUrl, progress, ct);
    }

    private static bool MatchFilter(string name, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;

        // Support simple glob patterns like *.elf
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
