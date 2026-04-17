using System.IO;
using System.IO.Compression;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Payloads;

namespace PS5AutoPayloadTool.Modules.Export;

/// <summary>Result returned by <see cref="ExportService.ExportAutoloadZipAsync"/>.</summary>
public record ExportResult(int Copied, int Skipped, string? Error = null);

/// <summary>
/// Creates PS5 autoload ZIP archives from a builder flow.
/// Automatically downloads any missing payload files before packaging,
/// so the ZIP export never silently skips a payload.
/// Views supply only the file path and step list — all I/O is handled here.
/// </summary>
public class ExportService(PayloadManager manager)
{
    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Exports a PS5 autoload ZIP to <paramref name="filePath"/>.
    /// <list type="bullet">
    ///   <item>Downloads any missing payload files first (using the step's
    ///         <see cref="BuilderStep.SelectedVersion"/> or "latest").</item>
    ///   <item>Writes <c>ps5_autoloader/autoload.txt</c> from <paramref name="autoloadTxt"/>.</item>
    ///   <item>Copies each ELF/BIN payload into <c>ps5_autoloader/</c>.</item>
    /// </list>
    /// Progress messages are reported via <paramref name="progress"/>.
    /// Returns an <see cref="ExportResult"/> describing the outcome.
    /// </summary>
    public async Task<ExportResult> ExportAutoloadZipAsync(
        string filePath,
        string autoloadTxt,
        IEnumerable<BuilderStep> elfSteps,
        AppConfig config,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var steps = elfSteps.ToList();

        await EnsurePayloadsDownloadedAsync(steps, config, progress, ct);

        try
        {
            using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);

            // autoload.txt
            var txtEntry = zip.CreateEntry("ps5_autoloader/autoload.txt");
            using (var writer = new StreamWriter(txtEntry.Open()))
                writer.Write(autoloadTxt);

            int copied = 0, skipped = 0;
            foreach (var step in steps)
            {
                var src = ResolvePayloadPath(step);
                if (src == null)
                {
                    progress?.Report($"Warning: {step.Payload} not available — skipped in ZIP.");
                    skipped++;
                    continue;
                }

                var entry = zip.CreateEntry($"ps5_autoloader/{step.Payload}");
                using var dest      = entry.Open();
                using var srcStream = File.OpenRead(src);
                srcStream.CopyTo(dest);
                copied++;
            }

            return new ExportResult(copied, skipped);
        }
        catch (Exception ex)
        {
            return new ExportResult(0, 0, ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// For each step whose payload file is absent from PayloadsDir,
    /// attempts to download it from the registered source.
    /// </summary>
    private async Task EnsurePayloadsDownloadedAsync(
        IEnumerable<BuilderStep> steps,
        AppConfig config,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        foreach (var step in steps)
        {
            var activeSrc = Path.Combine(AppPaths.PayloadsDir, step.Payload);
            if (File.Exists(activeSrc)) continue;

            if (!config.PayloadMeta.TryGetValue(step.Payload, out var meta)
                || string.IsNullOrEmpty(meta.SourceUrl))
            {
                progress?.Report(
                    $"Warning: {step.Payload} has no source configured — cannot auto-download.");
                continue;
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
                progress?.Report($"  ✗ Download failed for {step.Payload}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resolves the best available local path for a payload step.
    /// For a pinned (non-Latest) version, checks the version cache first.
    /// Falls back to the active payloads directory.
    /// Returns null when the file is not available at all.
    /// </summary>
    private static string? ResolvePayloadPath(BuilderStep step)
    {
        if (!string.IsNullOrEmpty(step.SelectedVersion) && step.SelectedVersion != "Latest")
        {
            var cachePath = Path.Combine(
                AppPaths.CacheDir, step.Payload, step.SelectedVersion, step.Payload);
            if (File.Exists(cachePath)) return cachePath;
        }

        var activePath = Path.Combine(AppPaths.PayloadsDir, step.Payload);
        return File.Exists(activePath) ? activePath : null;
    }
}
