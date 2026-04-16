using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PS5AutoPayloadTool.Core;

public class GhRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GhAsset> Assets { get; set; } = new();
}

public class GhAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class GhContent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class GitHubClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubClient(string? token = null)
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PS5AutoPayloadTool", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (!string.IsNullOrWhiteSpace(token))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public async Task<List<GhRelease>> GetReleasesAsync(string owner, string repo)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<GhRelease>>(json, _jsonOpts) ?? new();
    }

    public async Task<List<GhContent>> GetFolderContentsAsync(string owner, string repo, string path)
    {
        var encodedPath = path.TrimStart('/');
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{encodedPath}";
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<List<GhContent>>(json, _jsonOpts) ?? new();
    }

    public async Task DownloadFileAsync(
        string url,
        string destPath,
        IProgress<(long, long)>? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);

        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1L;
        long received = 0L;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            received += bytesRead;
            if (total > 0)
                progress?.Report((received, total));
        }
    }

    public static bool IsPayloadFile(string name)
    {
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext is ".lua" or ".elf" or ".bin";
    }

    public static bool IsZipFile(string name)
        => System.IO.Path.GetExtension(name).ToLowerInvariant() == ".zip";

    /// <summary>Returns top-level directory names in the repository root.</summary>
    public async Task<List<string>> GetRepoDirsAsync(string owner, string repo)
    {
        try
        {
            var contents = await GetFolderContentsAsync(owner, repo, "");
            return contents.Where(c => c.Type == "dir").Select(c => c.Name).ToList();
        }
        catch { return new(); }
    }

    /// <summary>
    /// Returns true if the repo has at least one release with a payload asset
    /// (direct .elf/.bin/.lua or a .zip that may contain payloads).
    /// </summary>
    public async Task<bool> HasReleasesAsync(string owner, string repo)
    {
        try
        {
            var releases = await GetReleasesAsync(owner, repo);
            return releases.Any(r => r.Assets.Any(a => IsPayloadFile(a.Name) || IsZipFile(a.Name)));
        }
        catch { return false; }
    }
}
