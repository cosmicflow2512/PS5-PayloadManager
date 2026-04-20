using System.IO;
using System.Text.Json;
using PS5AutoPayloadTool.Modules.Core;
using Log = PS5AutoPayloadTool.Modules.Core.LogService;

namespace PS5AutoPayloadTool.Modules.Execution;

// Mirrors HA flow_analysis.py: persist + query flow execution runs.

public record FlowRunStep(
    string Type,        // "send" | "delay" | "wait_port"
    string Label,
    int    DurationMs,
    bool   Success,
    string? Error = null);

public record FlowRunRecord(
    string            Id,
    string            StartedAt,
    int               TotalMs,
    bool              SafeMode,
    List<FlowRunStep> Steps);

public static class FlowRunHistory
{
    private const int MaxRuns = 10;

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // ── I/O ──────────────────────────────────────────────────────────────────

    private static List<FlowRunRecord> Load()
    {
        try
        {
            if (File.Exists(AppPaths.FlowRunsFile))
            {
                var raw = File.ReadAllText(AppPaths.FlowRunsFile);
                return JsonSerializer.Deserialize<List<FlowRunRecord>>(raw, _opts) ?? new();
            }
        }
        catch { }
        return new();
    }

    private static void Save(List<FlowRunRecord> runs)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Base);
            File.WriteAllText(AppPaths.FlowRunsFile, JsonSerializer.Serialize(runs, _opts));
        }
        catch (Exception ex) { Log.Warn("FlowRunHistory", $"Save failed: {ex.Message}"); }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Appends a run record; prunes to newest <see cref="MaxRuns"/>.</summary>
    public static void RecordRun(FlowRunRecord run)
    {
        var runs = Load();
        runs.Add(run);
        if (runs.Count > MaxRuns)
            runs = runs[^MaxRuns..];
        Save(runs);
        Log.Debug("FlowRunHistory", $"Run recorded: {run.Id}  steps={run.Steps.Count}  total={run.TotalMs}ms");
    }

    /// <summary>Returns all stored runs, newest last.</summary>
    public static List<FlowRunRecord> GetRuns() => Load();

    /// <summary>
    /// Returns aggregated port-wait timing stats across all runs.
    /// Mirrors HA flow_analysis.get_stats().
    /// </summary>
    public static Dictionary<int, PortTimingStats> GetStats()
    {
        var byPort = new Dictionary<int, List<int>>();
        foreach (var run in Load())
            foreach (var step in run.Steps)
                if (step.Type == "wait_port" && step.Success)
                {
                    // Step label format: "port {N}" — extract port number
                    var parts = step.Label.Split(' ');
                    if (parts.Length >= 2 && int.TryParse(parts[^1], out var port))
                    {
                        if (!byPort.ContainsKey(port)) byPort[port] = new();
                        byPort[port].Add(step.DurationMs);
                    }
                }

        var result = new Dictionary<int, PortTimingStats>();
        foreach (var (port, durations) in byPort)
            result[port] = new PortTimingStats(
                Port:    port,
                Count:   durations.Count,
                AvgMs:   (int)Math.Round(durations.Average()),
                LastMs:  durations[^1],
                MinMs:   durations.Min(),
                MaxMs:   durations.Max(),
                Entries: new());

        return result;
    }

    /// <summary>Deletes all stored runs.</summary>
    public static void ClearRuns()
    {
        try { if (File.Exists(AppPaths.FlowRunsFile)) File.Delete(AppPaths.FlowRunsFile); }
        catch (Exception ex) { Log.Warn("FlowRunHistory", $"Clear failed: {ex.Message}"); }
    }
}
