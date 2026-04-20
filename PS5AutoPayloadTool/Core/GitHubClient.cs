using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PS5AutoPayloadTool.Core;

public class GitHubClient
{
    private static readonly HttpClient _http = new();
    private static readonly Dictionary<string, (DateTime at, JsonNode data)> _treeCache = new();
    private static readonly TimeSpan _ttl = TimeSpan.FromMinutes(5);

    static GitHubClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PS5AutoPayloadTool/1.1");
    }

    public string? Token { get; set; }

    public GitHubClient(string? token = null) { Token = token; }

    // ── Releases ─────────────────────────────────────────────────────────────

    public async Task<List<ReleaseAsset>> GetReleasesAsync(string repo, string filter = "")
    {
        var result = new List<ReleaseAsset>();
        try
        {
            var releases = await GetJsonArrayAsync($"https://api.github.com/repos/{repo}/releases?per_page=3");
            foreach (var rel in releases ?? [])
            {
                var tag  = rel?["tag_name"]?.GetValue<string>() ?? "";
                var pub  = rel?["published_at"]?.GetValue<string>() ?? "";
                var rid  = rel?["id"]?.GetValue<long>() ?? 0;
                var assets = rel?["assets"]?.AsArray();
                if (assets == null) continue;

                foreach (var asset in assets)
                {
                    var name = asset?["name"]?.GetValue<string>() ?? "";
                    var url  = asset?["browser_download_url"]?.GetValue<string>() ?? "";
                    var size = asset?["size"]?.GetValue<long>() ?? 0;
                    var upd  = asset?["updated_at"]?.GetValue<string>() ?? "";

                    if (!MatchesFilter(name, filter)) continue;

                    var ext = Path.GetExtension(name).ToLowerInvariant();
                    if (ext == ".zip")
                    {
                        result.Add(new ReleaseAsset(name, url, tag, pub, size, rid, upd, true));
                    }
                    else if (ext is ".elf" or ".lua" or ".bin")
                    {
                        result.Add(new ReleaseAsset(name, url, tag, pub, size, rid, upd, false));
                    }
                }
            }
        }
        catch { }
        return result;
    }

    // ── Tree / folder ─────────────────────────────────────────────────────────

    public async Task<List<TreeFile>> ScanRepoFilesAsync(string repo, string folder = "", string filter = "")
    {
        var key = $"{repo}:{folder}";
        if (_treeCache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.at < _ttl)
            return FilterTree(cached.data, filter);

        try
        {
            var url = string.IsNullOrEmpty(folder)
                ? $"https://api.github.com/repos/{repo}/git/trees/HEAD?recursive=1"
                : $"https://api.github.com/repos/{repo}/contents/{Uri.EscapeDataString(folder)}";

            var data = await GetJsonAsync(url);
            if (data != null)
                _treeCache[key] = (DateTime.UtcNow, data);
            return FilterTree(data, filter);
        }
        catch { return []; }
    }

    public async Task<List<string>> GetRepoFoldersAsync(string repo)
    {
        try
        {
            var contents = await GetJsonArrayAsync(
                $"https://api.github.com/repos/{repo}/contents/");
            return contents?
                .Where(c => c?["type"]?.GetValue<string>() == "dir")
                .Select(c => c?["name"]?.GetValue<string>() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList() ?? [];
        }
        catch { return []; }
    }

    public void InvalidateCache(string repo) =>
        _treeCache.Remove($"{repo}:");

    // ── Download ──────────────────────────────────────────────────────────────

    public async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuth(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync();
        }
        catch { return null; }
    }

    // ── Download payload (handles ZIP) ────────────────────────────────────────

    public async Task<(byte[]? data, string filename)> DownloadPayloadAsync(
        string url, string assetName)
    {
        var raw = await DownloadAsync(url);
        if (raw == null) return (null, assetName);

        var ext = Path.GetExtension(assetName).ToLowerInvariant();
        if (ext != ".zip") return (raw, assetName);

        // Extract first ELF/LUA/BIN from ZIP
        using var ms  = new MemoryStream(raw);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);
        var entry = zip.Entries
            .FirstOrDefault(e =>
            {
                var x = Path.GetExtension(e.Name).ToLowerInvariant();
                return x is ".elf" or ".lua" or ".bin";
            });
        if (entry == null) return (null, assetName);

        using var es = entry.Open();
        using var buf = new MemoryStream();
        await es.CopyToAsync(buf);
        return (buf.ToArray(), entry.Name);
    }

    // ── Check updates for all sources ─────────────────────────────────────────

    public async Task<List<UpdateResult>> CheckUpdatesAsync(
        List<SourceEntry> sources, Dictionary<string, PayloadMeta> existing)
    {
        var results = new List<UpdateResult>();
        foreach (var src in sources)
        {
            try
            {
                List<ReleaseAsset> assets;
                if (src.SourceType == "folder")
                    assets = await ScanAsFolder(src);
                else
                    assets = await GetReleasesAsync(src.Repo, src.Filter);

                foreach (var asset in assets)
                {
                    var name = Path.GetExtension(asset.Name).ToLowerInvariant() == ".zip"
                        ? null : asset.Name;
                    if (name == null) continue;

                    if (!existing.TryGetValue(name, out var meta)) continue;

                    bool hasUpdate = HasUpdate(meta, asset);
                    if (hasUpdate)
                        results.Add(new UpdateResult(name, meta.Version, asset.Tag,
                            asset.DownloadUrl, asset.PublishedAt, asset.Size, asset.ReleaseId,
                            asset.UpdatedAt));
                }
            }
            catch { }
        }
        return results;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool HasUpdate(PayloadMeta meta, ReleaseAsset asset)
    {
        if (meta.Version != asset.Tag) return true;
        if (meta.AssetSize != 0 && asset.Size != 0 && meta.AssetSize != asset.Size) return true;
        if (!string.IsNullOrEmpty(meta.PublishedAt) && !string.IsNullOrEmpty(asset.PublishedAt))
        {
            if (DateTime.TryParse(meta.PublishedAt, out var old) &&
                DateTime.TryParse(asset.PublishedAt, out var fresh))
                if ((fresh - old).TotalSeconds >= 60) return true;
        }
        return false;
    }

    private async Task<List<ReleaseAsset>> ScanAsFolder(SourceEntry src)
    {
        var files = await ScanRepoFilesAsync(src.Repo, src.Folder, src.Filter);
        return files.Select(f => new ReleaseAsset(
            Path.GetFileName(f.Path), f.DownloadUrl, "folder", "", 0, 0, "", false)).ToList();
    }

    private static List<TreeFile> FilterTree(JsonNode? data, string filter)
    {
        var result = new List<TreeFile>();
        if (data == null) return result;

        IEnumerable<JsonNode?> items = data is JsonArray arr
            ? arr
            : data["tree"]?.AsArray() ?? Enumerable.Empty<JsonNode?>();

        foreach (var item in items)
        {
            var path = item?["path"]?.GetValue<string>()
                    ?? item?["name"]?.GetValue<string>() ?? "";
            var url  = item?["download_url"]?.GetValue<string>()
                    ?? item?["html_url"]?.GetValue<string>() ?? "";
            var ext  = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".elf" or ".lua" or ".bin")) continue;
            if (!MatchesFilter(Path.GetFileName(path), filter)) continue;
            result.Add(new TreeFile(path, url));
        }
        return result;
    }

    private static bool MatchesFilter(string name, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var pat = filter.Replace("*", ".*").Replace("?", ".");
        return System.Text.RegularExpressions.Regex.IsMatch(name, $"^{pat}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private async Task<JsonArray?> GetJsonArrayAsync(string url)
    {
        var node = await GetJsonAsync(url);
        return node as JsonArray;
    }

    private async Task<JsonNode?> GetJsonAsync(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        return JsonNode.Parse(json);
    }

    private void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record ReleaseAsset(
    string Name, string DownloadUrl, string Tag,
    string PublishedAt, long Size, long ReleaseId, string UpdatedAt, bool IsZip);

public record TreeFile(string Path, string DownloadUrl);

public record UpdateResult(
    string Filename, string OldVersion, string NewVersion,
    string DownloadUrl, string PublishedAt, long AssetSize,
    long ReleaseId, string AssetUpdatedAt);
