using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace PS5AutoPayloadTool.Models;

public class PortSettings
{
    [JsonPropertyName("elf_port")]
    public int ElfPort { get; set; } = 9021;

    [JsonPropertyName("lua_port")]
    public int LuaPort { get; set; } = 9026;

    /// <summary>
    /// Optional separate port for .bin payloads.  0 means "use ElfPort".
    /// </summary>
    [JsonPropertyName("bin_port")]
    public int BinPort { get; set; } = 0;

    [JsonIgnore]
    public int EffectiveBinPort => BinPort >= 1 && BinPort <= 65535 ? BinPort : ElfPort;
}

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

    [JsonPropertyName("ports")]
    public PortSettings Ports { get; set; } = new();

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

    [JsonIgnore]
    public string Id => Url;

    [JsonIgnore]
    public string TypeLabel => Type == "release" ? "Releases" : "Folder";

    [JsonIgnore]
    public string FolderOrReleases =>
        Type == "release" ? "release assets" :
        string.IsNullOrEmpty(FolderPath) ? "root" : FolderPath;
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

    /// <summary>
    /// SHA256 hex digest of the locally cached file.
    /// Compared against the remote git blob SHA (folder sources) to detect updates.
    /// </summary>
    [JsonPropertyName("file_hash")]
    public string FileHash { get; set; } = "";

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

public class BuilderStep : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string p) => PropertyChanged?.Invoke(this, new(p));

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    private string _payload = "";
    [JsonPropertyName("payload")]
    public string Payload
    {
        get => _payload;
        set { _payload = value; Notify(nameof(Payload)); }
    }

    private int _port = 9021;
    [JsonPropertyName("port")]
    public int Port
    {
        get => _port;
        set { _port = value; Notify(nameof(Port)); }
    }

    private int _ms = 1000;
    [JsonPropertyName("ms")]
    public int Ms
    {
        get => _ms;
        set { _ms = value; Notify(nameof(Ms)); }
    }

    private int _timeout = 60;
    [JsonPropertyName("timeout")]
    public int Timeout
    {
        get => _timeout;
        set { _timeout = value; Notify(nameof(Timeout)); }
    }

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
    /// Version badge shown next to the version ComboBox in the Autoload Builder.
    /// Set externally by FlowBuilderPage.UpdateVersionLabels().
    /// </summary>
    private string _versionLabel = "";
    [JsonIgnore]
    public string VersionLabel
    {
        get => _versionLabel;
        set { _versionLabel = value; Notify(nameof(VersionLabel)); }
    }

    /// <summary>
    /// The version selected for this step in the builder (persisted).
    /// "Latest" means use the most recent available version.
    /// </summary>
    private string _selectedVersion = "Latest";
    [JsonPropertyName("selected_version")]
    public string SelectedVersion
    {
        get => _selectedVersion;
        set { _selectedVersion = value; Notify(nameof(SelectedVersion)); }
    }

    /// <summary>
    /// Available version options for the version ComboBox (not persisted).
    /// Populated by FlowBuilderPage.UpdateVersionLabels().
    /// </summary>
    private List<string> _versionOptions = new() { "Latest" };
    [JsonIgnore]
    public List<string> VersionOptions
    {
        get => _versionOptions;
        set { _versionOptions = value; Notify(nameof(VersionOptions)); }
    }

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
