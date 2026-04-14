using System.IO;

namespace PS5AutoPayloadTool.Core;

/// <summary>
/// Centralises every path the application uses.
/// All data goes to %LOCALAPPDATA%\PS5Autopayload\ — never next to the .exe.
/// </summary>
public static class AppPaths
{
    /// <summary>Root: %LOCALAPPDATA%\PS5Autopayload\</summary>
    public static readonly string Base = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PS5Autopayload");

    /// <summary>Payload files (.lua / .elf / .bin) stored here.</summary>
    public static string PayloadsDir => Path.Combine(Base, "payloads");

    /// <summary>Autoload profile .txt files stored here.</summary>
    public static string ProfilesDir => Path.Combine(Base, "profiles");

    /// <summary>Application config (JSON).</summary>
    public static string ConfigFile => Path.Combine(Base, "config.json");

    /// <summary>Downloaded / cached files.</summary>
    public static string CacheDir => Path.Combine(Base, "cache");

    /// <summary>Rotating log files.</summary>
    public static string LogsDir => Path.Combine(Base, "logs");

    /// <summary>
    /// Called once on startup. Creates every required directory if it does not
    /// already exist. Safe to call multiple times (no-op when dirs exist).
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(PayloadsDir);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(LogsDir);
    }
}
