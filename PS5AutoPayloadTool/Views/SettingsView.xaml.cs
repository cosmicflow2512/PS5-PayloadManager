using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class SettingsView : UserControl
{
    public event Action? DeviceChanged;
    private readonly ObservableCollection<DeviceRow> _devices = [];
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        DevicesList.ItemsSource = _devices;
        Load();
    }

    private void Load()
    {
        _loading = true;
        var state = Storage.LoadUiState();
        IpInput.Text    = state["ps5_ip"]?.GetValue<string>()     ?? "";
        TokenInput.Text = state["github_token"]?.GetValue<string>() ?? "";
        _loading = false;
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        _devices.Clear();
        foreach (var d in Storage.LoadDevices())
            _devices.Add(new DeviceRow { Id = d.Id, Ip = d.Ip, Label = $"{d.Name}  —  {d.Ip}" });
    }

    private void SaveIp()
    {
        if (_loading) return;
        var state = Storage.LoadUiState();
        state["ps5_ip"] = IpInput.Text.Trim();
        Storage.SaveUiState(state);
        DeviceChanged?.Invoke();
    }

    private void SaveToken()
    {
        if (_loading) return;
        var state = Storage.LoadUiState();
        state["github_token"] = TokenInput.Text.Trim();
        Storage.SaveUiState(state);
    }

    private void OnIpChanged(object sender, TextChangedEventArgs e)    => SaveIp();
    private void OnTokenChanged(object sender, TextChangedEventArgs e) => SaveToken();

    private async void OnCheckPort(object sender, RoutedEventArgs e)
        => await CheckPort(9021);

    private async void OnCheckLua(object sender, RoutedEventArgs e)
        => await CheckPort(9026);

    private async System.Threading.Tasks.Task CheckPort(int port)
    {
        var ip = IpInput.Text.Trim();
        if (string.IsNullOrEmpty(ip)) { PortStatus.Text = "Enter IP first."; return; }
        PortStatus.Text = $"Checking {ip}:{port} …";
        PortStatus.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
        var open = await PortChecker.CheckAsync(ip, port, 5.0);
        PortStatus.Text = open ? $"Port {port}: OPEN ✓" : $"Port {port}: closed";
        PortStatus.Foreground = (System.Windows.Media.Brush)FindResource(open ? "Success" : "Danger");
        LogBus.Log($"{ip}:{port} is {(open ? "open" : "closed")}", open ? LogLevel.Success : LogLevel.Warn);
    }

    private void OnAddDevice(object sender, RoutedEventArgs e)
    {
        var name = NewDeviceName.Text.Trim();
        var ip   = NewDeviceIp.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip)) { MessageBox.Show("Enter name and IP."); return; }
        var devices = Storage.LoadDevices();
        devices.Add(new DeviceEntry(Guid.NewGuid().ToString(), name, ip));
        Storage.SaveDevices(devices);
        NewDeviceName.Text = ""; NewDeviceIp.Text = "";
        RefreshDevices();
    }

    private void OnUseDevice(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string ip)
        {
            IpInput.Text = ip;
            SaveIp();
        }
    }

    private void OnDeleteDevice(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string id) return;
        var devices = Storage.LoadDevices();
        devices.RemoveAll(d => d.Id == id);
        Storage.SaveDevices(devices);
        RefreshDevices();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "ps5_backup.json" };
        if (dlg.ShowDialog() != true) return;
        var backup = Storage.BuildBackup();
        File.WriteAllText(dlg.FileName, backup.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        LogBus.Log("Config exported.", LogLevel.Success);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var obj  = JsonNode.Parse(json) as JsonObject;
            if (obj == null) { MessageBox.Show("Invalid backup file."); return; }
            if (MessageBox.Show("Merge backup into current config?", "Import",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            Storage.RestoreSelective(obj, true, true, true, true, true, "merge");
            LogBus.Log("Config imported.", LogLevel.Success);
            Load();
        }
        catch (Exception ex) { MessageBox.Show("Import failed: " + ex.Message); }
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Factory reset? All config and payloads will be deleted (backup created first).",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Storage.FactoryReset();
        LogBus.Log("Factory reset complete.", LogLevel.Warn);
        Load();
    }
}

public class DeviceRow
{
    public string Id    { get; set; } = "";
    public string Ip    { get; set; } = "";
    public string Label { get; set; } = "";
}
