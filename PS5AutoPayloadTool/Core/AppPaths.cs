using System.IO;

namespace PS5AutoPayloadTool.Core;

public static class AppPaths
{
    public static string DataDir     { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PS5Autopayload");

    public static string PayloadsDir { get; } = Path.Combine(DataDir, "payloads");
    public static string ProfilesDir { get; } = Path.Combine(DataDir, "profiles");

    public static string DevicesJson  { get; } = Path.Combine(DataDir, "devices.json");
    public static string SourcesJson  { get; } = Path.Combine(DataDir, "sources.json");
    public static string PayloadsJson { get; } = Path.Combine(DataDir, "payloads.json");
    public static string StateJson    { get; } = Path.Combine(DataDir, "state.json");
    public static string TimingJson   { get; } = Path.Combine(DataDir, "timing.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(PayloadsDir);
        Directory.CreateDirectory(ProfilesDir);
    }
}
