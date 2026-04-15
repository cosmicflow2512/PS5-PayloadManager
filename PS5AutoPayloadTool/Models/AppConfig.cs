using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace PS5AutoPayloadTool.Models;

public class AppConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("state")]
    public AppState State { get; set; } = new();

    [JsonPropertyName("sources")]
    public List<SourceConfig> Sources { get; set; } = new();

    [JsonPropertyName("payload_meta")]
    public Dictionary<string, PayloadMeta> PayloadMeta { get; set; } = new();

    [JsonPropertyName("profiles")]
    public Dictionary<string, string> Profiles { get; set; } = new();

    [JsonPropertyName("devices")]
    public List<DeviceConfig> Devices { get; set; } = new();

    /// <summary>
    /// Unified accessor: returns devices[0].Ip if present, else state.PS5Ip.
    /// Setter updates both state.PS5Ip and devices[0].Ip (creating entry if needed).
    /// </summary>
    [JsonIgnore]
    public string PS5Host
    {
        get => Devices.Count > 0 ? Devices[0].Ip : State.PS5Ip;
        set
        {
            State.PS5Ip = value;
            if (Devices.Count == 0)
                Devices.Add(new DeviceConfig { Ip = value });
            else
                Devices[0].Ip = value;
        }
    }

    [JsonIgnore]
    public string GitHubToken
    {
        get => State.GitHubToken;
        set => State.GitHubToken = value;
    }
}

public class AppState
{
    [JsonPropertyName("ps5_ip")]
    public string PS5Ip { get; set; } = "192.168.1.100";

    [JsonPropertyName("github_token")]
    public string GitHubToken { get; set; } = "";

    [JsonPropertyName("builder_steps")]
    public List<BuilderStep> BuilderSteps { get; set; } = new();

    [JsonPropertyName("builder_profile_name")]
    public string BuilderProfileName { get; set; } = "My Flow";

    [JsonPropertyName("selected_device_ip")]
    public string SelectedDeviceIp { get; set; } = "";

    [JsonPropertyName("payload_filter")]
    public string PayloadFilter { get; set; } = "";

    [JsonPropertyName("advanced_mode")]
    public bool AdvancedMode { get; set; } = false;

    [JsonPropertyName("favorites")]
    public List<string> Favorites { get; set; } = new();
}

public class SourceConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "release";

    [JsonPropertyName("filter")]
    public string Filter { get; set; } = "";

    [JsonPropertyName("folder_path")]
    public string FolderPath { get; set; } = "";

    /// <summary>Parses url: splits by '/' and returns second-to-last segment.</summary>
    [JsonIgnore]
    public string Owner
    {
        get
        {
            var parts = Url.TrimEnd('/').Split('/');
            return parts.Length >= 2 ? parts[^2] : "";
        }
    }

    /// <summary>Parses url: splits by '/' and returns last segment.</summary>
    [JsonIgnore]
    public string Repo
    {
        get
        {
            var parts = Url.TrimEnd('/').Split('/');
            return parts.Length >= 1 ? parts[^1] : "";
        }
    }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var o = Owner;
            var r = Repo;
            return (!string.IsNullOrEmpty(o) && !string.IsNullOrEmpty(r))
                ? $"{o}/{r}"
                : Url;
        }
    }

    // Keep an Id property so the existing SourcesPage XAML (Tag="{Binding Id}") still compiles.
    // We derive it from the URL so it's stable.
    [JsonIgnore]
    public string Id => Url;
}

public class PayloadMeta
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = new();

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; } = 0;

    [JsonPropertyName("local_path")]
    public string LocalPath { get; set; } = "";

    [JsonIgnore]
    public bool IsDownloaded => !string.IsNullOrEmpty(LocalPath) && File.Exists(LocalPath);

    [JsonIgnore]
    public bool HasUpdateAvailable { get; set; } = false;
}

public class DeviceConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = "192.168.1.100";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Ip : $"{Name}  ({Ip})";
}

public class BuilderStep
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 9021;

    [JsonPropertyName("ms")]
    public int Ms { get; set; } = 500;

    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 60;

    [JsonPropertyName("interval_ms")]
    public int IntervalMs { get; set; } = 500;

    [JsonIgnore]
    public string DisplayText => Type switch
    {
        "payload"   => $"PAYLOAD  {Payload}  \u2192  :{Port}",
        "delay"     => $"DELAY  {Ms} ms",
        "wait_port" => $"WAIT  port {Port}  ({Timeout}s)",
        _           => Type
    };

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        "payload"   => "PAYLOAD",
        "delay"     => "DELAY",
        "wait_port" => "WAIT",
        _           => Type.ToUpperInvariant()
    };

    [JsonIgnore]
    public string TypeColor => Type switch
    {
        "payload"   => "#89B4FA",
        "delay"     => "#A6E3A1",
        "wait_port" => "#F9E2AF",
        _           => "#CDD6F4"
    };

    /// <summary>
    /// Serialises this step to a profile-text line.
    /// "payload"   → "filename.elf port"
    /// "delay"     → "!ms"
    /// "wait_port" → "?port timeout interval_ms"
    /// </summary>
    public string ToProfileLine() => Type switch
    {
        "payload"   => $"{Payload} {Port}",
        "delay"     => $"!{Ms}",
        "wait_port" => $"?{Port} {Timeout} {IntervalMs}",
        _           => ""
    };

    // ── Legacy compatibility shims so old code that still uses these property
    // names continues to compile without changes.

    [JsonIgnore]
    public string PayloadName
    {
        get => Payload;
        set => Payload = value;
    }

    [JsonIgnore]
    public int DelayMs
    {
        get => Ms;
        set => Ms = value;
    }

    [JsonIgnore]
    public int TimeoutSeconds
    {
        get => Timeout;
        set => Timeout = value;
    }
}
