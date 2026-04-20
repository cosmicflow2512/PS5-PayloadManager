using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PS5AutoPayloadTool.Modules.Core;
using Log = PS5AutoPayloadTool.Modules.Core.LogService;

namespace PS5AutoPayloadTool.Modules.Execution;

// Mirrors HA port_timing.py: record / stats / clear per port.

public record PortTimingEntry(
    string Start,
    string Ready,
    int    DurationMs);

public record PortTimingStats(
    int Port,
    int Count,
    int AvgMs,
    int LastMs,
    int MinMs,
    int MaxMs,
    List<PortTimingEntry> Entries);

public static class PortTimingService
{
    private const int MaxEntries = 10;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // ── I/O ──────────────────────────────────────────────────────────────────

    private static Dictionary<string, List<PortTimingEntry>> Load()
    {
        try
        {
            if (File.Exists(AppPaths.TimingFile))
            {
                var raw = File.ReadAllText(AppPaths.TimingFile);
                return JsonSerializer.Deserialize<Dictionary<string, List<PortTimingEntry>>>(
                    raw, _opts) ?? new();
            }
        }
        catch { }
        return new();
    }

    private static void Save(Dictionary<string, List<PortTimingEntry>> data)
    {
        try { File.WriteAllText(AppPaths.TimingFile, JsonSerializer.Serialize(data, _opts)); }
        catch (Exception ex) { Log.Warn("PortTimingService", $"Save failed: {ex.Message}"); }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Appends a timing measurement for <paramref name="port"/>.
    /// Keeps only the newest <see cref="MaxEntries"/> entries per port.
    /// </summary>
    public static void Record(int port, int durationMs, string? startIso = null, string? readyIso = null)
    {
        var data = Load();
        var key  = port.ToString();
        if (!data.ContainsKey(key)) data[key] = new();

        var now = DateTime.UtcNow.ToString("O");
        data[key].Add(new PortTimingEntry(
            Start:      startIso ?? now,
            Ready:      readyIso ?? now,
            DurationMs: durationMs));

        // Prune: keep only the newest MaxEntries
        if (data[key].Count > MaxEntries)
            data[key] = data[key][^MaxEntries..];

        Save(data);
        Log.Debug("PortTimingService", $"Timing recorded — port {port}: {durationMs} ms");
    }

    /// <summary>Returns aggregated statistics for every tracked port.</summary>
    public static Dictionary<int, PortTimingStats> GetStats()
    {
        var result = new Dictionary<int, PortTimingStats>();
        foreach (var (portStr, entries) in Load())
        {
            if (entries.Count == 0) continue;
            if (!int.TryParse(portStr, out var port)) continue;

            var durations = entries.Select(e => e.DurationMs).ToList();
            result[port] = new PortTimingStats(
                Port:    port,
                Count:   entries.Count,
                AvgMs:   (int)Math.Round(durations.Average()),
                LastMs:  durations[^1],
                MinMs:   durations.Min(),
                MaxMs:   durations.Max(),
                Entries: entries);
        }
        return result;
    }

    /// <summary>Returns stats for a single port, or null if no data exists.</summary>
    public static PortTimingStats? GetPortStats(int port)
        => GetStats().TryGetValue(port, out var s) ? s : null;

    /// <summary>Deletes all timing data.</summary>
    public static void Clear()
    {
        try { if (File.Exists(AppPaths.TimingFile)) File.Delete(AppPaths.TimingFile); }
        catch (Exception ex) { Log.Warn("PortTimingService", $"Clear failed: {ex.Message}"); }
    }
}
