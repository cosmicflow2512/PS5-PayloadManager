using System.Text.RegularExpressions;

namespace PS5AutoPayloadTool.Core;

// ── Directives ───────────────────────────────────────────────────────────────

public abstract record Directive;

public record SendDirective(string Filename, int Port, int AutoPort) : Directive;
public record DelayDirective(int Ms) : Directive;
public record WaitPortDirective(int Port, double Timeout, int IntervalMs) : Directive;

// ── Parser ────────────────────────────────────────────────────────────────────

public static class AutoloadParser
{
    private static readonly Regex _delayRx    = new(@"^!(\d+)$");
    private static readonly Regex _waitRx     = new(@"^\?(\d+)(?:\s+(\d+(?:\.\d+)?))?(?:\s+(\d+))?$");
    private static readonly Regex _sendRx     = new(@"^(\S+\.(?:elf|lua|bin))(?:\s+(\d+))?$", RegexOptions.IgnoreCase);
    private static readonly Regex _versionRx  = new(@"^#\s*~(\S+)\s+(\S.*)$");

    public static List<Directive> Parse(string content)
    {
        var result = new List<Directive>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;
            var d = ParseLine(line);
            if (d != null) result.Add(d);
        }
        return result;
    }

    public static Directive? ParseLine(string line)
    {
        var m = _delayRx.Match(line);
        if (m.Success) return new DelayDirective(int.Parse(m.Groups[1].Value));

        m = _waitRx.Match(line);
        if (m.Success)
        {
            int port       = int.Parse(m.Groups[1].Value);
            double timeout = m.Groups[2].Success ? double.Parse(m.Groups[2].Value) : 60.0;
            int interval   = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 500;
            return new WaitPortDirective(port, timeout, interval);
        }

        m = _sendRx.Match(line);
        if (m.Success)
        {
            var filename = m.Groups[1].Value;
            int autoPort = ResolveDefaultPort(filename);
            int port     = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : autoPort;
            return new SendDirective(filename, port, autoPort);
        }

        return null;
    }

    public static Dictionary<string, string> ParseVersionPins(string content)
    {
        var pins = new Dictionary<string, string>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            var m = _versionRx.Match(line);
            if (m.Success) pins[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }
        return pins;
    }

    public static string SetVersionPin(string content, string filename, string version)
    {
        var lines = content.Split('\n').ToList();
        var tag   = $"# ~{filename} {version}";
        var idx   = lines.FindIndex(l => l.TrimStart().StartsWith($"# ~{filename} "));
        if (idx >= 0) lines[idx] = tag;
        else lines.Add(tag);
        return string.Join('\n', lines);
    }

    public static int ResolveDefaultPort(string filename)
        => filename.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? 9026 : 9021;

    // ── Directive → profile line ────────────────────────────────────────────

    public static string DirectiveToLine(Directive d) => d switch
    {
        SendDirective s     => $"{s.Filename} {s.Port}",
        DelayDirective dl   => $"!{dl.Ms}",
        WaitPortDirective w => $"?{w.Port} {w.Timeout} {w.IntervalMs}",
        _                   => ""
    };

    // ── Steps → profile content ────────────────────────────────────────────

    public static string StepsToContent(IEnumerable<FlowStep> steps)
    {
        var lines = new List<string>();
        foreach (var s in steps)
        {
            switch (s.Type)
            {
                case "payload":
                    int port = s.PortOverride ?? AutoloadParser.ResolveDefaultPort(s.Filename ?? "");
                    lines.Add($"{s.Filename} {port}");
                    if (!string.IsNullOrEmpty(s.Version))
                        lines.Add($"# ~{s.Filename} {s.Version}");
                    break;
                case "delay":
                    lines.Add($"!{s.Ms}");
                    break;
                case "wait_port":
                    lines.Add($"?{s.Port} {s.Timeout} {s.IntervalMs}");
                    break;
            }
        }
        return string.Join('\n', lines);
    }
}

// ── FlowStep DTO (matches HA JS builder state) ───────────────────────────────

public class FlowStep
{
    public string Type        { get; set; } = "payload";
    public string? Filename   { get; set; }
    public int?   PortOverride { get; set; }
    public int    AutoPort    { get; set; }
    public int    Ms          { get; set; }
    public int    Port        { get; set; }
    public double Timeout     { get; set; } = 60.0;
    public int    IntervalMs  { get; set; } = 500;
    public string? Version    { get; set; }
}
