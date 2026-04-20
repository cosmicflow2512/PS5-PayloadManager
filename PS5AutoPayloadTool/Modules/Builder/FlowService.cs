using System.Text;
using System.Text.RegularExpressions;
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
    // ── Version tier sorting ─────────────────────────────────────────────────

    /// <summary>
    /// Stable-sorts version tags exactly as the HA tool does:
    ///   stable release (tier 0) → beta (tier 1) → alpha / test (tier 2).
    /// Within each tier the original insertion order is preserved.
    /// </summary>
    public static List<string> SortVersionsByTier(IEnumerable<string> versions)
    {
        static int Tier(string tag)
        {
            var t = tag.ToLowerInvariant();
            if (t.Contains("alpha") || t.Contains("test")) return 2;
            if (t.Contains("beta"))                         return 1;
            return 0;
        }
        return versions.OrderBy(Tier).ToList();
    }

    // ── Version pin comments ──────────────────────────────────────────────────

    // Mirrors HA autoload_parser.py: # ~version <filename> <tag>
    private static readonly Regex _versionPinRx =
        new(@"^#\s*~version\s+(\S+)\s+(\S+)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Reads all <c># ~version filename tag</c> comments from a profile and
    /// returns a filename → version map.  Mirrors HA parse_version_pins().
    /// </summary>
    public static Dictionary<string, string> ParseVersionPins(string content)
    {
        var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in content.Split('\n'))
        {
            var m = _versionPinRx.Match(raw.Trim());
            if (m.Success) pins[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return pins;
    }

    /// <summary>
    /// Sets or replaces the <c># ~version filename tag</c> line for one file
    /// inside a profile's text content.  Mirrors HA set_version_pin().
    /// </summary>
    public static string SetVersionPin(string content, string filename, string version)
    {
        var pinLine = $"# ~version {filename} {version}";
        var lines   = content.Split('\n').ToList();

        // Replace existing pin for this file
        for (int i = 0; i < lines.Count; i++)
        {
            var m = _versionPinRx.Match(lines[i].Trim());
            if (m.Success && m.Groups[1].Value.Equals(filename, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = pinLine;
                return string.Join("\n", lines);
            }
        }

        // Insert before the first non-comment, non-empty line
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.Length > 0 && !t.StartsWith('#'))
            {
                lines.Insert(i, pinLine);
                return string.Join("\n", lines);
            }
        }

        return content.TrimEnd('\n') + "\n" + pinLine + "\n";
    }

    /// <summary>
    /// Generates a profile .txt that embeds a <c># ~version</c> pin comment
    /// before each payload line whose step has a pinned (non-"Latest") version.
    /// Mirrors the HA convention so profiles are portable between tools.
    /// </summary>
    public static string BuildProfileWithPins(IEnumerable<BuilderStep> steps)
    {
        var sb = new StringBuilder();
        foreach (var step in steps)
        {
            var line = step.ToProfileLine();
            if (string.IsNullOrEmpty(line)) continue;

            if (step.Type == "payload"
                && !string.IsNullOrEmpty(step.SelectedVersion)
                && step.SelectedVersion != "Latest")
            {
                sb.AppendLine($"# ~version {step.Payload} {step.SelectedVersion}");
            }
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }


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
                // Build VersionOptions: "Latest" first, then tier-sorted versions
                // (stable → beta → alpha/test), with meta.Version leading its tier
                // so the currently active version is always visible near the top.
                var rawVersions = meta.Versions
                    .Where(v => v != "folder" && !string.IsNullOrEmpty(v))
                    .Distinct()
                    .ToList();
                var sorted = SortVersionsByTier(rawVersions);
                // Promote meta.Version to the front of its tier group
                if (!string.IsNullOrEmpty(meta.Version) && sorted.Contains(meta.Version))
                {
                    sorted.Remove(meta.Version);
                    sorted.Insert(0, meta.Version);
                }
                var opts = new List<string> { "Latest" };
                foreach (var v in sorted)
                    if (!opts.Contains(v))
                        opts.Add(v);
                step.VersionOptions = opts;

                // Ensure persisted selection is still a valid choice
                if (!opts.Contains(step.SelectedVersion))
                    step.SelectedVersion = "Latest";

                // Effective version for "Latest": the currently downloaded version
                // (meta.Version), which is what ExportService actually uses from PayloadsDir.
                var effective = step.SelectedVersion == "Latest"
                    ? (!string.IsNullOrEmpty(meta.Version) ? meta.Version : "")
                    : step.SelectedVersion;

                step.VersionLabel = string.IsNullOrEmpty(effective) ? "" : $"({effective})";
            }
            else
            {
                step.VersionLabel   = "";
                step.VersionOptions = new List<string> { "Latest" };
            }
        }
    }
}
