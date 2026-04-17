using System.Text;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Modules.Builder;

/// <summary>
/// Pure business logic for the Autoload Builder flow.
/// All methods are static — no UI dependencies, fully testable in isolation.
/// Views call these methods to validate steps, generate autoload.txt content,
/// and refresh per-step version metadata.
/// </summary>
public static class FlowService
{
    // ── Compatibility ────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether <paramref name="steps"/> are compatible with the PS5
    /// autoload.txt format (ELF/BIN only, no WAIT steps, no Lua payloads).
    /// Returns true when fully compatible; <paramref name="incompatible"/>
    /// lists the descriptions of any incompatible steps.
    /// </summary>
    public static bool IsAutoloadCompatible(
        IEnumerable<BuilderStep> steps,
        out List<string> incompatible)
    {
        incompatible = new List<string>();

        foreach (var step in steps)
        {
            if (step.Type == "wait_port")
                incompatible.Add("WAIT step");
            else if (step.Type == "payload" &&
                     step.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                incompatible.Add($"{step.Payload} (Lua)");
        }

        return incompatible.Count == 0;
    }

    /// <summary>
    /// Removes all WAIT and Lua-payload steps from <paramref name="steps"/> in-place.
    /// Returns the count of removed steps.
    /// </summary>
    public static int RemoveIncompatibleSteps(IList<BuilderStep> steps)
    {
        var toRemove = steps
            .Where(s => s.Type == "wait_port" ||
                        (s.Type == "payload" &&
                         s.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var s in toRemove)
            steps.Remove(s);

        return toRemove.Count;
    }

    // ── autoload.txt generation ──────────────────────────────────────────────

    /// <summary>
    /// Serialises the ELF/BIN payload steps and delay steps from
    /// <paramref name="steps"/> into the PS5 autoload.txt format.
    /// WAIT and Lua steps are silently excluded.
    /// </summary>
    public static string BuildAutoloadTxt(IEnumerable<BuilderStep> steps)
    {
        var sb = new StringBuilder();
        foreach (var step in steps)
        {
            if (step.Type == "delay")
                sb.AppendLine($"!{step.Ms}");
            else if (step.Type == "payload" &&
                     !step.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(step.Payload);
        }
        return sb.ToString();
    }

    // ── Version data ─────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes <see cref="BuilderStep.VersionOptions"/> and
    /// <see cref="BuilderStep.VersionLabel"/> on every payload step based on
    /// the current <paramref name="config"/>.PayloadMeta.
    /// Call after the payload list changes or after downloading a new version.
    /// </summary>
    public static void UpdateVersionData(
        IEnumerable<BuilderStep> steps,
        AppConfig config)
    {
        foreach (var step in steps)
        {
            if (step.Type != "payload" || string.IsNullOrEmpty(step.Payload))
            {
                step.VersionLabel   = "";
                step.VersionOptions = new List<string> { "Latest" };
                continue;
            }

            if (config.PayloadMeta.TryGetValue(step.Payload, out var meta)
                && !string.IsNullOrEmpty(meta.Version))
            {
                var opts = new List<string> { "Latest" };
                foreach (var v in meta.Versions)
                    if (v != "folder" && !opts.Contains(v))
                        opts.Add(v);
                step.VersionOptions = opts;

                // Ensure persisted selection is still a valid choice
                if (!opts.Contains(step.SelectedVersion))
                    step.SelectedVersion = "Latest";

                var effective = step.SelectedVersion == "Latest"
                    ? (meta.Versions.Count > 0 ? meta.Versions[0] : meta.Version)
                    : step.SelectedVersion;

                step.VersionLabel = $"({effective})";
            }
            else
            {
                step.VersionLabel   = "";
                step.VersionOptions = new List<string> { "Latest" };
            }
        }
    }
}
