using System.Text.RegularExpressions;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

/// <summary>
/// Parses autoload profile .txt files into executable directives.
///
/// Supported syntax (mirrors PS5AutopayloadHA format):
///   filename.lua [port]   → SendDirective  (default port 9026)
///   filename.elf [port]   → SendDirective  (default port 9021)
///   filename.bin [port]   → SendDirective  (default port 9021)
///   !&lt;ms&gt;                 → DelayDirective
///   ?&lt;port&gt; [timeout_s] [interval_ms] → WaitPortDirective
///   # comment             → ignored
/// </summary>
public static class ProfileParser
{
    private static readonly Regex PayloadRx =
        new(@"^([\w\-.\s]+\.(?:lua|elf|bin))(?:\s+(\d+))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DelayRx =
        new(@"^!(\d+)$", RegexOptions.Compiled);

    private static readonly Regex WaitPortRx =
        new(@"^\?(\d+)(?:\s+(\d+))?(?:\s+(\d+))?$", RegexOptions.Compiled);

    /// <summary>Parse raw text content into a list of directives.</summary>
    public static List<IDirective> Parse(string content, string payloadDirectory)
    {
        var directives = new List<IDirective>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            // !500  →  delay 500 ms
            var dm = DelayRx.Match(line);
            if (dm.Success)
            {
                directives.Add(new DelayDirective { DelayMs = int.Parse(dm.Groups[1].Value) });
                continue;
            }

            // ?9026 60 500  →  wait for port
            var wm = WaitPortRx.Match(line);
            if (wm.Success)
            {
                directives.Add(new WaitPortDirective
                {
                    Port = int.Parse(wm.Groups[1].Value),
                    TimeoutSeconds = wm.Groups[2].Success ? int.Parse(wm.Groups[2].Value) : 60,
                    IntervalMs = wm.Groups[3].Success ? int.Parse(wm.Groups[3].Value) : 500
                });
                continue;
            }

            // payload.lua [port]
            var pm = PayloadRx.Match(line);
            if (pm.Success)
            {
                var filename = pm.Groups[1].Value.Trim();
                var port = pm.Groups[2].Success
                    ? int.Parse(pm.Groups[2].Value)
                    : PayloadSender.GetDefaultPort(filename);

                directives.Add(new SendDirective
                {
                    FilePath = Path.Combine(payloadDirectory, filename),
                    Port = port
                });
            }
        }

        return directives;
    }

    /// <summary>Parse a profile file from disk.</summary>
    public static List<IDirective> ParseFile(string profilePath, string payloadDirectory)
        => Parse(File.ReadAllText(profilePath), payloadDirectory);
}
