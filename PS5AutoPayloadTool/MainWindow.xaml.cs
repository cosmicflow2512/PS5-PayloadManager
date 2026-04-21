using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Views;

namespace PS5AutoPayloadTool;

public partial class MainWindow : Window
{
    private readonly SourcesView  _sources  = new();
    private readonly PayloadsView _payloads = new();
    private readonly BuilderView  _builder  = new();
    private readonly ProfilesView _profiles = new();
    private readonly SettingsView _settings = new();
    private readonly LogsView     _logs     = new();

    public MainWindow()
    {
        InitializeComponent();
        ContentHost.Content = _sources;
        UpdateConnection();
        _settings.DeviceChanged += UpdateConnection;
        _profiles.EditRequested += name =>
        {
            _builder.LoadProfile(name);
            NavBuilder.IsChecked = true;
            ContentHost.Content  = _builder;
        };
        Loaded += (_, _) => UpdateConnection();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        ContentHost.Content = rb.Tag switch
        {
            "Sources"  => _sources,
            "Payloads" => _payloads,
            "Builder"  => _builder,
            "Profiles" => _profiles,
            "Settings" => _settings,
            "Logs"     => _logs,
            _ => ContentHost.Content
        };
        if (rb.Tag?.ToString() == "Payloads") _payloads.Refresh();
        if (rb.Tag?.ToString() == "Profiles") _profiles.Refresh();
        if (rb.Tag?.ToString() == "Sources")  _sources.Refresh();
        if (rb.Tag?.ToString() == "Builder")  _builder.Refresh();
    }

    private void UpdateConnection()
    {
        var state = Storage.LoadUiState();
        var ip = state["ps5_ip"]?.GetValue<string>() ?? "";
        var devices = Storage.LoadDevices();
        var dev = devices.FirstOrDefault(d => d.Ip == ip);
        DeviceNameText.Text = dev?.Name ?? (string.IsNullOrEmpty(ip) ? "No device" : "PS4/PS5");
        DeviceIpText.Text   = string.IsNullOrEmpty(ip) ? "Set IP in Settings" : ip;
    }
}
