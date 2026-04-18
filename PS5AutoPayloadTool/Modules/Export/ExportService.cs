using System.IO;
using System.IO.Compression;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Payloads;
using Log = PS5AutoPayloadTool.Modules.Core.LogService;

namespace PS5AutoPayloadTool.Modules.Export;

/// <summary>Result returned by <see cref="ExportService.ExportAutoloadZipAsync"/>.</summary>
public record ExportResult(int Copied, int Skipped, string? Error = null);

/// <summary>
/// Creates PS5 autoload ZIP archives from a builder flow.
/// For remote payloads, downloads any missing files before packaging.
/// For local payloads (no SourceUrl / version == "local"), resolves directly
/// from PayloadsDir or meta.LocalPath — never attempts a network download.
/// Export is blocked (returns an error) if any required payload file is missing.
/// </summary>
public class ExportService(PayloadManager manager)
{
    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<ExportResult> ExportAutoloadZipAsync(
        string filePath,
        string autoloadTxt,
        IEnumerable<BuilderStep> elfSteps,
        AppConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var steps = elfSteps.ToList();
        Log.Info("ExportService", $"Export started: {Path.GetFileName(filePath)}  ({steps.Count} step(s))");

        // Pre-download any missing remote payloads
        var downloadError = await EnsurePayloadsDownloadedAsync(steps, config, progress, ct);
        if (downloadError != null)
            return new ExportResult(0, 0, downloadError);

        try
        {
            using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

            var txtEntry = zip.CreateEntry("ps5_autoloader/autoload.txt");
            using (var writer = new StreamWriter(txtEntry.Open()))
                writer.Write(autoloadTxt);

            int copied = 0;
            var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in steps)
            {
                // Deduplicate: same filename across multiple steps → include only once
                if (addedNames.Contains(step.Payload))
                    continue;

                var src = ResolvePayloadPath(step, config);
                if (src == null)
                {
                    var msg = $"Export blocked: {step.Payload} could not be found locally.";
                    Log.Error("ExportService", msg);
                    return new ExportResult(0, 0, msg);
                }

                var entry = zip.CreateEntry($"ps5_autoloader/{step.Payload}");
                using var dest      = entry.Open();
                using var srcStream = File.OpenRead(src);
                srcStream.CopyTo(dest);
                addedNames.Add(step.Payload);
                Log.Debug("ExportService", $"  Added: {step.Payload}");
                copied++;
            }

            Log.Info("ExportService", $"Export complete: {copied} payload(s) copied");
            return new ExportResult(copied, 0);
        }
        catch (Exception ex)
        {
            Log.Error("ExportService", $"Export failed: {ex.Message}");
            return new ExportResult(0, 0, ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// For remote payloads whose file is absent, attempts to download.
    /// Local payloads (no SourceUrl or version == "local") are skipped — their
    /// path is resolved later in <see cref="ResolvePayloadPath"/>.
    /// Returns a blocking error string if a remote download fails; null on success.
    /// </summary>
    private async Task<string?> EnsurePayloadsDownloadedAsync(
        IEnumerable<BuilderStep> steps,
        AppConfig config,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        foreach (var step in steps)
        {
            if (!config.PayloadMeta.TryGetValue(step.Payload, out var meta))
                continue;

            // Local payloads — nothing to download; resolve path later
            bool isLocal = string.IsNullOrEmpty(meta.SourceUrl) || meta.Version == "local";
            if (isLocal) continue;

            // Already present in PayloadsDir?
            var activeSrc = Path.Combine(AppPaths.PayloadsDir, step.Payload);
            if (File.Exists(activeSrc)) continue;

            // Check version cache for a pinned version
            if (!string.IsNullOrEmpty(step.SelectedVersion) && step.SelectedVersion != "Latest")
            {
                var cachePath = Path.Combine(
                    AppPaths.CacheDir, step.Payload, step.SelectedVersion, step.Payload);
                if (File.Exists(cachePath)) continue;
            }

            var targetVersion =
                !string.IsNullOrEmpty(step.SelectedVersion) && step.SelectedVersion != "Latest"
                    ? step.SelectedVersion
                    : "latest";

            progress?.Report($"Downloading {step.Payload} ({targetVersion})…");
            try
            {
                await manager.DownloadPayloadAsync(
                    config, step.Payload, targetVersion, meta.SourceUrl, null, ct);
                progress?.Report($"  ✓ {step.Payload} ready.");
            }
            catch (Exception ex)
            {
                return $"Download failed for {step.Payload}: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the best available local path for a payload step.
    /// Order: version cache → PayloadsDir → meta.LocalPath (for local uploads).
    /// Returns null when the file is not available at all.
    /// </summary>
    private static string? ResolvePayloadPath(BuilderStep step, AppConfig config)
    {
        if (!string.IsNullOrEmpty(step.SelectedVersion) && step.SelectedVersion != "Latest")
        {
            var cachePath = Path.Combine(
                AppPaths.CacheDir, step.Payload, step.SelectedVersion, step.Payload);
            if (File.Exists(cachePath)) return cachePath;
        }

        var activePath = Path.Combine(AppPaths.PayloadsDir, step.Payload);
        if (File.Exists(activePath)) return activePath;

        // Fallback: local-upload path stored in meta
        if (config.PayloadMeta.TryGetValue(step.Payload, out var meta)
            && !string.IsNullOrEmpty(meta.LocalPath)
            && File.Exists(meta.LocalPath))
            return meta.LocalPath;

        return null;
    }
}
