using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public class PayloadManager
{
    private readonly GitHubClient _github;

    public PayloadManager(GitHubClient github)
    {
        _github = github;
    }

    public async Task<List<PayloadItem>> ScanSourceAsync(
        PayloadSource source,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var results = new List<PayloadItem>();

        if (source.Type == SourceType.GitHubRelease)
        {
            progress?.Report($"Fetching releases for {source.DisplayName}...");
            var releases = await _github.GetReleasesAsync(source.Owner, source.Repo);

            // Group assets by name across all releases to build version lists
            var assetsByName = new Dictionary<string, List<(string version, string url, long size)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var release in releases)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var asset in release.Assets)
                {
                    if (!GitHubClient.IsPayloadFile(asset.Name)) continue;

                    // Apply filter if provided
                    if (!string.IsNullOrWhiteSpace(source.Filter))
                    {
                        if (!MatchesFilter(asset.Name, source.Filter)) continue;
                    }

                    if (!assetsByName.TryGetValue(asset.Name, out var versions))
                    {
                        versions = new List<(string, string, long)>();
                        assetsByName[asset.Name] = versions;
                    }

                    versions.Add((release.TagName, asset.BrowserDownloadUrl, asset.Size));
                }
            }

            foreach (var kvp in assetsByName)
            {
                var name = kvp.Key;
                var versions = kvp.Value;
                var latestVersion = versions.First();

                var item = new PayloadItem
                {
                    SourceId = source.Id,
                    Name = name,
                    CurrentVersion = latestVersion.version,
                    AvailableVersions = versions.Select(v => v.version).Distinct().ToList(),
                    FileSize = latestVersion.size
                };

                // Check if already downloaded
                var localPath = Path.Combine(AppPaths.PayloadsDir, name);
                if (File.Exists(localPath))
                    item.LocalPath = localPath;

                results.Add(item);
                progress?.Report($"Found: {name} ({versions.Count} version(s))");
            }
        }
        else if (source.Type == SourceType.GitHubFolder)
        {
            progress?.Report($"Fetching folder contents for {source.DisplayName}/{source.FolderPath}...");
            var contents = await _github.GetFolderContentsAsync(source.Owner, source.Repo, source.FolderPath);

            foreach (var content in contents)
            {
                ct.ThrowIfCancellationRequested();
                if (content.Type != "file") continue;
                if (!GitHubClient.IsPayloadFile(content.Name)) continue;

                // Apply filter if provided
                if (!string.IsNullOrWhiteSpace(source.Filter))
                {
                    if (!MatchesFilter(content.Name, source.Filter)) continue;
                }

                var item = new PayloadItem
                {
                    SourceId = source.Id,
                    Name = content.Name,
                    CurrentVersion = "latest",
                    AvailableVersions = new List<string> { "latest" },
                    FileSize = content.Size
                };

                var localPath = Path.Combine(AppPaths.PayloadsDir, content.Name);
                if (File.Exists(localPath))
                    item.LocalPath = localPath;

                results.Add(item);
                progress?.Report($"Found: {content.Name}");
            }
        }

        return results;
    }

    public async Task DownloadPayloadAsync(
        PayloadItem item,
        string version,
        IProgress<(long, long)>? progress,
        CancellationToken ct)
    {
        // Find the source to get the download URL
        var config = ConfigManager.Load();
        var source = config.Sources.FirstOrDefault(s => s.Id == item.SourceId);
        if (source == null) throw new InvalidOperationException("Source not found for payload item.");

        string downloadUrl;

        if (source.Type == SourceType.GitHubRelease)
        {
            var releases = await _github.GetReleasesAsync(source.Owner, source.Repo);
            GhAsset? asset = null;

            foreach (var release in releases)
            {
                if (release.TagName == version || (version == "latest" && asset == null))
                {
                    asset = release.Assets.FirstOrDefault(a =>
                        string.Equals(a.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                    if (release.TagName == version) break;
                }
            }

            if (asset == null)
                throw new InvalidOperationException($"Asset '{item.Name}' not found in release '{version}'.");

            downloadUrl = asset.BrowserDownloadUrl;
        }
        else // GitHubFolder
        {
            var contents = await _github.GetFolderContentsAsync(source.Owner, source.Repo, source.FolderPath);
            var content = contents.FirstOrDefault(c =>
                string.Equals(c.Name, item.Name, StringComparison.OrdinalIgnoreCase));

            if (content == null || string.IsNullOrEmpty(content.DownloadUrl))
                throw new InvalidOperationException($"File '{item.Name}' not found in folder.");

            downloadUrl = content.DownloadUrl;
        }

        // Download to cache directory
        var cacheVersionDir = Path.Combine(AppPaths.CacheDir, item.Name, version);
        var cachePath = Path.Combine(cacheVersionDir, item.Name);
        await _github.DownloadFileAsync(downloadUrl, cachePath, progress, ct);

        // Copy to active payloads directory
        var activePath = Path.Combine(AppPaths.PayloadsDir, item.Name);
        Directory.CreateDirectory(AppPaths.PayloadsDir);
        File.Copy(cachePath, activePath, overwrite: true);

        // Update item properties
        item.LocalPath = activePath;
        item.CurrentVersion = version;
        item.LastUpdated = DateTime.UtcNow;
    }

    private static bool MatchesFilter(string fileName, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;

        // Support simple glob patterns like *.elf
        if (filter.StartsWith("*"))
        {
            var suffix = filter.Substring(1);
            return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (filter.EndsWith("*"))
        {
            var prefix = filter.Substring(0, filter.Length - 1);
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return fileName.Equals(filter, StringComparison.OrdinalIgnoreCase);
    }
}
