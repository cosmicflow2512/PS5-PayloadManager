using System.IO;
using System.Text.RegularExpressions;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Modules.Execution;

/// <summary>
/// Parses autoload profile .txt files into executable directives.
///
/// Supported syntax (mirrors PS5AutopayloadHA format):
///   filename.lua [port]                     → SendDirective  (default port 9026)
///   filename.elf [port]                     → SendDirective  (default port 9021)
///   filename.bin [port]                     → SendDirective  (default port 9021)
///   !&lt;ms&gt;                                  → DelayDirective
///   ?&lt;port&gt; [timeout_s] [interval_ms]       → WaitPortDirective
///   # ~version filename.elf v1.0.3          → version pin (resolved to cache path)
///   # other comment                         → ignored
/// </summary>
public static class ProfileParser
{
    private static readonly Regex PayloadRx =
        new(@"^([\w\-.\s]+\.(?:lua|elf|bin))(?:\s+(\d+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DelayRx =
        new(@"^!(\d+)$", RegexOptions.Compiled);

    private static readonly Regex WaitPortRx =
        new(@"^\?(\d+)(?:\s+(\d+))?(?:\s+(\d+))?$", RegexOptions.Compiled);

    private static readonly Regex VersionPinRx =
        new(@"^#\s*~version\s+(\S+)\s+(\S+)\s*$", RegexOptions.Compiled);

    /// <summary>Parses raw text content into a list of directives.</summary>
    public static List<IDirective> Parse(string content, string payloadDirectory)
    {
        // First pass: collect all version pins  (# ~version filename tag)
        var pins = Builder.FlowService.ParseVersionPins(content);

        var directives = new List<IDirective>();

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var dm = DelayRx.Match(line);
            if (dm.Success)
            {
                directives.Add(new DelayDirective { DelayMs = int.Parse(dm.Groups[1].Value) });
                continue;
            }

            var wm = WaitPortRx.Match(line);
            if (wm.Success)
            {
                directives.Add(new WaitPortDirective
                {
                    Port           = int.Parse(wm.Groups[1].Value),
                    TimeoutSeconds = wm.Groups[2].Success ? int.Parse(wm.Groups[2].Value) : 60,
                    IntervalMs     = wm.Groups[3].Success ? int.Parse(wm.Groups[3].Value) : 500
                });
                continue;
            }

            var pm = PayloadRx.Match(line);
            if (pm.Success)
            {
                var filename = pm.Groups[1].Value.Trim();
                var port = pm.Groups[2].Success
                    ? int.Parse(pm.Groups[2].Value)
                    : PayloadSender.GetDefaultPort(filename);

                // If a version pin exists for this file, resolve it from the
                // version cache (CacheDir/filename/version/filename).
                // Falls back to the active PayloadsDir copy when cache is absent.
                string filePath;
                if (pins.TryGetValue(filename, out var pinnedVersion))
                {
                    var cachePath = System.IO.Path.Combine(
                        Core.AppPaths.CacheDir, filename, pinnedVersion, filename);
                    filePath = System.IO.File.Exists(cachePath)
                        ? cachePath
                        : System.IO.Path.Combine(payloadDirectory, filename);
                }
                else
                {
                    filePath = System.IO.Path.Combine(payloadDirectory, filename);
                }

                directives.Add(new SendDirective { FilePath = filePath, Port = port });
            }
        }

        return directives;
    }

    /// <summary>Parses a profile file from disk.</summary>
    public static List<IDirective> ParseFile(string profilePath, string payloadDirectory)
        => Parse(File.ReadAllText(profilePath), payloadDirectory);
}
