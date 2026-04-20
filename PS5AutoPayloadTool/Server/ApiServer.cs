using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Server;

public static class ApiServer
{
    private const string Version = "1.1.1";
    private static readonly GitHubClient _gh = new();
    private static readonly JsonSerializerOptions _jo = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public static async Task StartAsync(int port)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseWebSockets();

        // ── Static files from embedded resources ────────────────────────────
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new EmbeddedFileProvider(
                Assembly.GetExecutingAssembly(), "PS5AutoPayloadTool.wwwroot"),
            RequestPath = "/static"
        });

        // ── WebSocket ────────────────────────────────────────────────────────
        app.Map("/ws", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            await WsManager.HandleAsync(ws);
        });

        // ── Root → index.html ────────────────────────────────────────────────
        app.MapGet("/", async ctx =>
        {
            var html = GetEmbeddedText("wwwroot/index.html");
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(html);
        });

        // ── Version ──────────────────────────────────────────────────────────
        app.MapGet("/api/version", () => Results.Json(new { version = Version }));

        // ── Config ───────────────────────────────────────────────────────────
        app.MapGet("/api/config", () =>
        {
            var state = Storage.LoadUiState();
            return Results.Json(new
            {
                ps5_ip            = state["ps5_ip"]?.GetValue<string>() ?? "",
                port_check_timeout = 10.0,
                port_check_interval = 0.5,
                github_token      = state["github_token"]?.GetValue<string>() ?? "",
            });
        });

        // ── UI State ─────────────────────────────────────────────────────────
        app.MapGet("/api/state", async ctx =>
        {
            var s = Storage.LoadUiState();
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(s.ToJsonString());
        });

        app.MapPost("/api/state", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var node = JsonNode.Parse(body) as JsonObject;
            if (node != null)
            {
                // Persist GitHub token into client
                if (node["github_token"]?.GetValue<string>() is { } tok && !string.IsNullOrEmpty(tok))
                    _gh.Token = tok;
                Storage.SaveUiState(node);
            }
            return Results.Ok();
        });

        // ── Devices ──────────────────────────────────────────────────────────
        app.MapGet("/api/devices", () =>
        {
            var devices = Storage.LoadDevices();
            return Results.Json(new { devices });
        });

        app.MapPost("/api/devices", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<DevicesRequest>(body, _jo);
            if (req?.Devices != null)
                Storage.SaveDevices(req.Devices);
            return Results.Ok();
        });

        // ── Payloads list ────────────────────────────────────────────────────
        app.MapGet("/api/payloads", () =>
        {
            var list = Storage.ListPayloads();
            return Results.Json(list);
        });

        // ── Upload payload ────────────────────────────────────────────────────
        app.MapPost("/api/payloads/upload", async Task<IResult> (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var saved = new List<string>();
            foreach (var file in form.Files)
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext is not (".elf" or ".lua" or ".bin")) continue;
                var dest = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(file.FileName));
                await using var fs = File.Create(dest);
                await file.CopyToAsync(fs);
                saved.Add(file.FileName);
            }
            return Results.Json(new { saved });
        });

        // ── Delete payload ────────────────────────────────────────────────────
        app.MapDelete("/api/payloads/{filename}", (string filename) =>
        {
            var path = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(filename));
            if (File.Exists(path)) File.Delete(path);
            var meta = Storage.LoadPayloadMeta();
            meta.Remove(filename);
            Storage.SavePayloadMeta(meta);
            return Results.Ok();
        });

        // ── Payload usage ─────────────────────────────────────────────────────
        app.MapGet("/api/payloads/{filename}/usage", (string filename) =>
        {
            var profiles = Storage.ListProfiles()
                .Where(p => (Storage.ReadProfile(p) ?? "").Contains(filename))
                .ToList();
            return Results.Json(new { profiles });
        });

        // ── Import payload from GitHub ────────────────────────────────────────
        app.MapPost("/api/payloads/import", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<ImportRequest>(body, _jo);
            if (req == null) return Results.BadRequest("bad request");

            var (data, realName) = await _gh.DownloadPayloadAsync(req.DownloadUrl, req.AssetName);
            if (data == null) return Results.Problem("Download failed");

            var dest = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(realName));
            await File.WriteAllBytesAsync(dest, data);

            var meta = Storage.LoadPayloadMeta();
            var allVer = (req.AllVersions ?? [])
                .Select(v => new VersionEntry
                {
                    Tag         = v.TryGetValue("tag", out var t) ? t : "",
                    DownloadUrl = v.TryGetValue("download_url", out var u) ? u : "",
                    PublishedAt = v.TryGetValue("published_at", out var p) ? p : "",
                    AssetSize   = v.TryGetValue("size", out var s) && long.TryParse(s, out var l) ? l : 0,
                }).ToList();

            meta[realName] = new PayloadMeta
            {
                Repo            = req.Repo,
                Version         = req.Version,
                DownloadUrl     = req.DownloadUrl,
                PublishedAt     = req.ReleasePublishedAt,
                AssetUpdatedAt  = req.AssetUpdatedAt,
                AssetSize       = req.AssetSize,
                ReleaseId       = req.ReleaseId,
                AllVersions     = allVer,
            };
            Storage.SavePayloadMeta(meta);
            return Results.Json(new { name = realName, size = data.Length });
        });

        // ── Switch version ────────────────────────────────────────────────────
        app.MapPost("/api/payloads/{filename}/switch-version", async (string filename, HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<SwitchVersionRequest>(body, _jo);
            if (req == null) return Results.BadRequest();

            var (data, realName) = await _gh.DownloadPayloadAsync(req.DownloadUrl, filename);
            if (data == null) return Results.Problem("Download failed");

            var dest = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(filename));
            await File.WriteAllBytesAsync(dest, data);

            var meta = Storage.LoadPayloadMeta();
            if (meta.TryGetValue(filename, out var m))
            {
                m.Version     = req.Version;
                m.DownloadUrl = req.DownloadUrl;
                Storage.SavePayloadMeta(meta);
            }
            return Results.Json(new { name = filename, version = req.Version });
        });

        // ── Set default version (metadata only) ───────────────────────────────
        app.MapPost("/api/payloads/{filename}/set-default-version", async (string filename, HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonNode.Parse(body);
            var ver  = req?["version"]?.GetValue<string>() ?? "";
            var meta = Storage.LoadPayloadMeta();
            if (meta.TryGetValue(filename, out var m)) { m.Version = ver; Storage.SavePayloadMeta(meta); }
            return Results.Ok();
        });

        // ── Sources ───────────────────────────────────────────────────────────
        app.MapGet("/api/sources", () => Results.Json(Storage.LoadSources()));

        app.MapPost("/api/sources", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<SourceRequest>(body, _jo);
            if (req == null) return Results.BadRequest();

            var repo = NormalizeRepo(req.Repo);
            var sources = Storage.LoadSources();
            if (!sources.Any(s => s.Repo == repo))
                sources.Add(new SourceEntry
                {
                    Repo        = repo,
                    Filter      = req.Filter,
                    SourceType  = req.SourceType,
                    Folder      = req.Folder,
                    DisplayName = req.DisplayName,
                });
            Storage.SaveSources(sources);

            // Scan for available payloads
            List<object> detected;
            if (req.SourceType == "folder")
            {
                var files = await _gh.ScanRepoFilesAsync(repo, req.Folder, req.Filter);
                detected = files.Select(f => (object)new
                {
                    name = Path.GetFileName(f.Path),
                    path = f.Path,
                    download_url = f.DownloadUrl,
                    version = "folder",
                    repo,
                }).ToList();
            }
            else
            {
                var assets = await _gh.GetReleasesAsync(repo, req.Filter);
                detected = GroupAssets(assets, repo);
            }

            return Results.Json(new { repo, detected });
        });

        app.MapDelete("/api/sources/{owner}/{repoName}", (string owner, string repoName) =>
        {
            var repo    = $"{owner}/{repoName}";
            var sources = Storage.LoadSources();
            sources.RemoveAll(s => s.Repo == repo);
            Storage.SaveSources(sources);
            _gh.InvalidateCache(repo);
            return Results.Ok();
        });

        app.MapPut("/api/sources/{owner}/{repoName}", async (string owner, string repoName, HttpContext ctx) =>
        {
            var repo = $"{owner}/{repoName}";
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<SourceRequest>(body, _jo);
            var sources = Storage.LoadSources();
            var src = sources.FirstOrDefault(s => s.Repo == repo);
            if (src == null) return Results.NotFound();
            if (req != null)
            {
                src.Filter      = req.Filter;
                src.SourceType  = req.SourceType;
                src.Folder      = req.Folder;
                src.DisplayName = req.DisplayName;
            }
            Storage.SaveSources(sources);
            return Results.Ok();
        });

        // ── Source: get repo folders ──────────────────────────────────────────
        app.MapGet("/api/sources/tree", async Task<IResult> (HttpContext ctx) =>
        {
            var repo = ctx.Request.Query["repo"].FirstOrDefault() ?? "";
            repo = NormalizeRepo(repo);
            var folders = await _gh.GetRepoFoldersAsync(repo);
            return Results.Json(new { folders });
        });

        // ── Source: get releases ──────────────────────────────────────────────
        app.MapGet("/api/sources/releases", async ctx =>
        {
            var repo   = NormalizeRepo(ctx.Request.Query["repo"].FirstOrDefault() ?? "");
            var filter = ctx.Request.Query["filter"].FirstOrDefault() ?? "";
            var assets = await _gh.GetReleasesAsync(repo, filter);
            var grouped = GroupAssets(assets, repo);
            return Results.Json(new { repo, assets = grouped });
        });

        // ── Source: check all updates ─────────────────────────────────────────
        app.MapGet("/api/sources/check-updates", async () =>
        {
            var sources  = Storage.LoadSources();
            var meta     = Storage.LoadPayloadMeta();
            var updates  = await _gh.CheckUpdatesAsync(sources, meta);
            return Results.Json(updates);
        });

        // ── Send payload ──────────────────────────────────────────────────────
        app.MapPost("/api/send", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<SendRequest>(body, _jo);
            if (req == null) return Results.BadRequest();

            int port = req.Port ?? PayloadSender.ResolvePort(req.Filename);
            var (ok, msg, bytes) = await PayloadSender.SendAsync(req.Host, port, req.Filename);
            await WsManager.BroadcastAsync("status", msg, ok ? "success" : "error");
            return Results.Json(new { ok, message = msg, bytes });
        });

        // ── Autoload: profiles ────────────────────────────────────────────────
        app.MapGet("/api/autoload/profiles", () =>
            Results.Json(Storage.ListProfiles()));

        app.MapGet("/api/autoload/content/{profile}", (string profile) =>
        {
            var content = Storage.ReadProfile(profile);
            return content != null ? Results.Text(content) : Results.NotFound();
        });

        app.MapPost("/api/autoload/content", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<SaveProfileRequest>(body, _jo);
            if (req == null) return Results.BadRequest();
            Storage.WriteProfile(req.Profile, req.Content);
            return Results.Ok();
        });

        app.MapDelete("/api/autoload/content/{profile}", (string profile) =>
        {
            Storage.DeleteProfile(profile);
            return Results.Ok();
        });

        // ── Autoload: parse profile ────────────────────────────────────────────
        app.MapGet("/api/autoload/parse/{profile}", (string profile) =>
        {
            var content = Storage.ReadProfile(profile);
            if (content == null) return Results.NotFound();
            var directives = AutoloadParser.Parse(content);
            var pins       = AutoloadParser.ParseVersionPins(content);
            var steps = directives.Select(d => d switch
            {
                SendDirective s => new FlowStep
                {
                    Type     = "payload", Filename = s.Filename,
                    PortOverride = s.Port != s.AutoPort ? s.Port : null,
                    AutoPort = s.AutoPort,
                    Version  = pins.TryGetValue(s.Filename, out var v) ? v : null,
                },
                DelayDirective dl => new FlowStep { Type = "delay", Ms = dl.Ms },
                WaitPortDirective w => new FlowStep
                {
                    Type = "wait_port", Port = w.Port,
                    Timeout = w.Timeout, IntervalMs = w.IntervalMs,
                },
                _ => null
            }).Where(s => s != null).ToList();
            return Results.Json(new { steps, profile });
        });

        // ── Autoload: execution state ─────────────────────────────────────────
        app.MapGet("/api/autoload/state", () =>
            Results.Json(new { state = ExecEngine.State, profile = ExecEngine.Profile }));

        // ── Autoload: run ─────────────────────────────────────────────────────
        app.MapPost("/api/autoload/run", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<RunRequest>(body, _jo);
            if (req == null) return Results.BadRequest();

            string content;
            if (!string.IsNullOrEmpty(req.Profile) && string.IsNullOrEmpty(req.Content))
            {
                content = Storage.ReadProfile(req.Profile) ?? "";
            }
            else
            {
                content = req.Content ?? "";
                if (!string.IsNullOrEmpty(req.Profile))
                    Storage.WriteProfile(req.Profile, content);
            }

            _ = ExecEngine.RunAsync(req.Host, content, req.ContinueOnError, req.Profile ?? "");
            return Results.Json(new { started = true });
        });

        app.MapPost("/api/autoload/stop",   () => { ExecEngine.RequestStop();   return Results.Ok(); });
        app.MapPost("/api/autoload/pause",  () => { ExecEngine.RequestPause();  return Results.Ok(); });
        app.MapPost("/api/autoload/resume", () => { ExecEngine.RequestResume(); return Results.Ok(); });

        // ── Autoload: patch version pin ───────────────────────────────────────
        app.MapPost("/api/autoload/patch-versions/{profile}", async (string profile, HttpContext ctx) =>
        {
            var body    = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req     = JsonNode.Parse(body);
            var filename = req?["filename"]?.GetValue<string>() ?? "";
            var version  = req?["version"]?.GetValue<string>() ?? "";
            var content  = Storage.ReadProfile(profile) ?? "";
            content = AutoloadParser.SetVersionPin(content, filename, version);
            Storage.WriteProfile(profile, content);
            return Results.Ok();
        });

        // ── Autoload: export ZIP ──────────────────────────────────────────────
        app.MapPost("/api/autoload/export-zip", async Task<IResult> (HttpContext ctx) =>
        {
            var body  = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req   = JsonNode.Parse(body);
            var steps = req?["steps"]?.Deserialize<List<FlowStep>>(_jo) ?? [];

            var lines    = new List<string>();
            var missing  = new List<string>();
            var warnings = new List<string>();

            foreach (var step in steps)
            {
                switch (step.Type)
                {
                    case "payload":
                    {
                        var fn = step.Filename ?? "";
                        if (string.IsNullOrEmpty(fn)) continue;
                        var ext = Path.GetExtension(fn).ToLowerInvariant();
                        if (ext == ".lua") { warnings.Add($"Lua skipped: {fn}"); continue; }
                        var src = Path.Combine(AppPaths.PayloadsDir, fn);
                        if (!File.Exists(src)) missing.Add(fn);
                        else lines.Add(fn);
                        break;
                    }
                    case "delay":
                        lines.Add($"!{step.Ms}");
                        break;
                    case "wait_port":
                        warnings.Add($"Wait-port step skipped (not supported in autoload.txt)");
                        break;
                }
            }

            if (missing.Count > 0)
                return Results.Json(new { ok = false, error = $"Missing: {string.Join(", ", missing)}" });

            // Build ZIP
            using var ms  = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var txt = zip.CreateEntry("ps5_autoloader/autoload.txt");
                await using var tw = new StreamWriter(txt.Open());
                await tw.WriteAsync(string.Join('\n', lines));

                foreach (var step in steps.Where(s => s.Type == "payload"))
                {
                    var fn  = step.Filename ?? "";
                    var ext = Path.GetExtension(fn).ToLowerInvariant();
                    if (ext == ".lua") continue;
                    var src = Path.Combine(AppPaths.PayloadsDir, fn);
                    if (!File.Exists(src)) continue;
                    var entry = zip.CreateEntry($"ps5_autoloader/{fn}");
                    await using var es = entry.Open();
                    await using var fs = File.OpenRead(src);
                    await fs.CopyToAsync(es);
                }
            }

            ms.Position = 0;
            var zipBytes = ms.ToArray();
            return Results.Bytes(zipBytes, "application/zip", "autoload.zip");
        });

        // ── Port check ────────────────────────────────────────────────────────
        app.MapPost("/api/port/check", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<PortCheckRequest>(body, _jo);
            if (req == null) return Results.BadRequest();
            var open = await PortChecker.CheckAsync(req.Host, req.Port, req.Timeout);
            return Results.Json(new { open, host = req.Host, port = req.Port });
        });

        app.MapPost("/api/port/wait", async Task<IResult> (HttpContext ctx) =>
        {
            var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req  = JsonSerializer.Deserialize<PortCheckRequest>(body, _jo);
            if (req == null) return Results.BadRequest();
            var open = await PortChecker.WaitAsync(req.Host, req.Port,
                req.Timeout, req.Interval,
                onProgress: async (e, t) =>
                    await WsManager.BroadcastAsync("status",
                        $"Port {req.Port}: {e:F0}s / {t}s …", "info"));
            return Results.Json(new { open, host = req.Host, port = req.Port });
        });

        // ── Timing stubs ──────────────────────────────────────────────────────
        app.MapGet("/api/timing",         () => Results.Json(new { samples = Array.Empty<object>() }));
        app.MapPost("/api/timing/record", () => Results.Ok());
        app.MapDelete("/api/timing",      () => Results.Ok());

        // ── Backup ────────────────────────────────────────────────────────────
        app.MapGet("/api/backup", async ctx =>
        {
            var backup = Storage.BuildBackup();
            var json   = backup.ToJsonString();
            ctx.Response.ContentType = "application/json";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"ps5_backup.json\"";
            await ctx.Response.WriteAsync(json);
        });

        app.MapPost("/api/backup/restore-selective", async Task<IResult> (HttpContext ctx) =>
        {
            var body   = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req    = JsonNode.Parse(body) as JsonObject;
            if (req == null) return Results.BadRequest();
            var backup = req["backup"] as JsonObject ?? req;
            var mode   = req["mode"]?.GetValue<string>() ?? "merge";
            Storage.RestoreSelective(backup,
                req["sources"]?.GetValue<bool>()  ?? true,
                req["payloads"]?.GetValue<bool>() ?? true,
                req["profiles"]?.GetValue<bool>() ?? true,
                req["settings"]?.GetValue<bool>() ?? true,
                req["flows"]?.GetValue<bool>()    ?? true,
                mode);
            return Results.Ok();
        });

        // ── Factory reset ─────────────────────────────────────────────────────
        app.MapPost("/api/config/reset", () =>
        {
            Storage.FactoryReset();
            return Results.Ok();
        });

        // ── Log export stub ────────────────────────────────────────────────────
        app.MapPost("/api/logs/export", async ctx =>
        {
            var body    = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
            var req     = JsonNode.Parse(body);
            var entries = req?["entries"]?.AsArray()
                ?.Select(e => e?.GetValue<string>() ?? "")
                .ToList() ?? [];
            var text    = string.Join('\n', entries);
            var bytes   = Encoding.UTF8.GetBytes(text);
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.Headers["Content-Disposition"] =
                $"attachment; filename=\"ps5_log_{DateTime.Now:yyyyMMddHHmmss}.txt\"";
            await ctx.Response.Body.WriteAsync(bytes);
        });

        await app.RunAsync($"http://127.0.0.1:{port}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetEmbeddedText(string path)
    {
        var asm  = Assembly.GetExecutingAssembly();
        var name = $"PS5AutoPayloadTool.{path.Replace('/', '.').Replace('\\', '.')}";
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return $"<!-- resource not found: {name} -->";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string NormalizeRepo(string repo)
    {
        repo = repo.Trim().TrimEnd('/');
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            repo = repo["https://github.com/".Length..];
        else if (repo.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))
            repo = repo["github.com/".Length..];
        return repo;
    }

    private static List<object> GroupAssets(List<ReleaseAsset> assets, string repo)
    {
        // Group by real payload name (strip .zip wrapper when present)
        var byName = new Dictionary<string, List<ReleaseAsset>>();
        foreach (var a in assets)
        {
            var key = a.IsZip ? Path.GetFileNameWithoutExtension(a.Name) : a.Name;
            if (!byName.TryGetValue(key, out var list)) byName[key] = list = [];
            list.Add(a);
        }

        var result = new List<object>();
        foreach (var (name, list) in byName)
        {
            var latest = list[0];
            result.Add(new
            {
                name         = name,
                asset_name   = latest.Name,
                download_url = latest.DownloadUrl,
                version      = latest.Tag,
                repo,
                all_versions = list.Select(v => new
                {
                    tag          = v.Tag,
                    download_url = v.DownloadUrl,
                    published_at = v.PublishedAt,
                    size         = v.Size,
                }).ToList(),
                release_published_at = latest.PublishedAt,
                asset_updated_at     = latest.UpdatedAt,
                asset_size           = latest.Size,
                release_id           = latest.ReleaseId,
            });
        }
        return result;
    }

    // ── Request DTOs ──────────────────────────────────────────────────────────

    private record DevicesRequest(List<DeviceEntry> Devices);

    private record ImportRequest(
        string Repo, string AssetName, string DownloadUrl, string Version,
        List<Dictionary<string, string>>? AllVersions,
        string ReleasePublishedAt, string AssetUpdatedAt,
        long AssetSize, long ReleaseId);

    private record SwitchVersionRequest(string Version, string DownloadUrl);

    private record SourceRequest(
        string Repo, string Filter, string SourceType, string Folder, string DisplayName);

    private record SendRequest(string Host, int? Port, string Filename);

    private record SaveProfileRequest(string Profile, string Content);

    private record RunRequest(
        string Host, string? Profile, string? Content, bool ContinueOnError);

    private record PortCheckRequest(
        string Host, int Port, double Timeout, double Interval);
}
