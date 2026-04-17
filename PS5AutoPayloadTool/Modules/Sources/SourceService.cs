using System.Text.RegularExpressions;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Payloads;

namespace PS5AutoPayloadTool.Modules.Sources;

/// <summary>
/// Encapsulates all GitHub source scanning and management operations.
/// Views delegate to this service — no direct GitHub API calls from UI code.
/// </summary>
public class SourceService(GitHubClient github, PayloadManager payloads)
{
    // Well-known payload folder names (highest priority shown first in dropdowns)
    private static readonly string[] KnownPayloadFolders =
        { "payloads", "bin", "elf", "release", "releases", "output", "dist" };

    // ── URL parsing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Parses and normalises a GitHub URL or "owner/repo" shorthand.
    /// Returns null if the input cannot be resolved to a valid GitHub repo URL.
    /// </summary>
    public static string? ParseRepoUrl(string input)
    {
        input = input.Trim().TrimEnd('/');
        var m = Regex.Match(input,
            @"(?:https?://)?(?:www\.)?github\.com/([^/\s]+)/([^/\s]+)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}";
        m = Regex.Match(input, @"^([^/\s]+)/([^/\s]+)$");
        if (m.Success)
            return $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}";
        return null;
    }

    // ── Repo discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Scans a repository to discover its folder structure and whether it has
    /// payload releases. Returns prioritised folder list for the UI dropdown
    /// and a flag indicating release availability.
    /// </summary>
    public record RepoDirInfo(bool HasReleases, List<string> FolderItems);

    public async Task<RepoDirInfo> GetRepoDirInfoAsync(
        SourceConfig source,
        CancellationToken ct = default)
    {
        var dirsTask     = github.GetRepoDirsAsync(source.Owner, source.Repo);
        var releasesTask = github.HasReleasesAsync(source.Owner, source.Repo);
        await Task.WhenAll(dirsTask, releasesTask);

        var dirs         = dirsTask.Result;
        bool hasReleases = releasesTask.Result;

        // Build prioritised folder list: root → known payload dirs → remainder → Custom
        var items = new List<string> { "/ (root)" };
        foreach (var kf in KnownPayloadFolders)
            if (dirs.Any(d => d.Equals(kf, StringComparison.OrdinalIgnoreCase)))
                items.Add(dirs.First(d => d.Equals(kf, StringComparison.OrdinalIgnoreCase)));
        foreach (var d in dirs)
            if (!items.Contains(d))
                items.Add(d);
        items.Add("Custom…");

        return new RepoDirInfo(hasReleases, items);
    }

    // ── Scan & update config ─────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="source"/> for payload files and merges the results
    /// into <paramref name="config"/>.PayloadMeta.  Does not save config.
    /// Returns (found count, null) on success or (0, errorMessage) on failure.
    /// </summary>
    public async Task<(int Found, string? Error)> ScanAndUpdateConfigAsync(
        SourceConfig source,
        AppConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            var found = await payloads.ScanSourceAsync(source, progress, ct);

            foreach (var (name, _, version, size, _) in found)
            {
                if (!config.PayloadMeta.ContainsKey(name))
                    config.PayloadMeta[name] = new PayloadMeta
                        { SourceUrl = source.Url, Versions = new() { version }, Version = version, Size = size };
                else if (!config.PayloadMeta[name].Versions.Contains(version))
                    config.PayloadMeta[name].Versions.Add(version);
            }

            return (found.Count, null);
        }
        catch (Exception ex)
        {
            return (0, ex.Message);
        }
    }
}
