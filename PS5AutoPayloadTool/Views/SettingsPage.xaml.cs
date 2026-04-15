using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class SettingsPage : UserControl
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        _loading = true;
        TxtToken.Text = MainWindow.Config.GitHubToken;
        TxtDataPath.Text = AppPaths.Base;

        RefreshDevices();

        // Stats
        var payloadCount = Directory.Exists(AppPaths.PayloadsDir)
            ? Directory.GetFiles(AppPaths.PayloadsDir).Length : 0;
        var profileCount = Directory.Exists(AppPaths.ProfilesDir)
            ? Directory.GetFiles(AppPaths.ProfilesDir, "*.txt").Length : 0;
        long cacheBytes  = GetDirSize(AppPaths.CacheDir);
        TxtStats.Text = $"{payloadCount} payload(s)  •  {profileCount} profile(s)  •  cache {FormatBytes(cacheBytes)}";

        _loading = false;
        TxtStatus.Text = "";
    }

    private void RefreshDevices()
    {
        var devices = MainWindow.Config.Devices;
        DevicesList.ItemsSource = null;
        DevicesList.ItemsSource = devices;
        TxtNoDevices.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Inputs ───────────────────────────────────────────────────────────────

    private void TxtToken_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        MainWindow.Config.GitHubToken = TxtToken.Text.Trim();
        SaveAndNotify();
    }

    private void SaveAndNotify()
    {
        (Window.GetWindow(this) as MainWindow)?.OnConfigChanged();
        TxtStatus.Text = "Saved.";
    }

    // ── Device management ────────────────────────────────────────────────────

    private void BtnAddDevice_Click(object sender, RoutedEventArgs e)
    {
        TxtDeviceName.Text = "";
        TxtDeviceIp.Text   = "192.168.1.";
        AddDeviceForm.Visibility = Visibility.Visible;
        TxtDeviceIp.Focus();
    }

    private void BtnSaveDevice_Click(object sender, RoutedEventArgs e)
    {
        var ip   = TxtDeviceIp.Text.Trim();
        var name = TxtDeviceName.Text.Trim();

        if (string.IsNullOrEmpty(ip))
        {
            TxtStatus.Text = "IP address is required.";
            return;
        }

        // Don't add duplicate IPs
        if (MainWindow.Config.Devices.Any(d => d.Ip == ip))
        {
            TxtStatus.Text = "A device with that IP already exists.";
            return;
        }

        MainWindow.Config.Devices.Add(new DeviceConfig { Name = name, Ip = ip });

        // If this is the first device, set it as the active host
        if (MainWindow.Config.Devices.Count == 1)
            MainWindow.Config.PS5Host = ip;

        MainWindow.SaveConfig();
        AddDeviceForm.Visibility = Visibility.Collapsed;
        RefreshDevices();
        SaveAndNotify();
    }

    private void BtnCancelDevice_Click(object sender, RoutedEventArgs e)
    {
        AddDeviceForm.Visibility = Visibility.Collapsed;
    }

    private void BtnRemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not DeviceConfig dev) return;

        MainWindow.Config.Devices.Remove(dev);

        // If we removed the active device, switch to first remaining
        if (MainWindow.Config.PS5Host == dev.Ip)
        {
            var next = MainWindow.Config.Devices.FirstOrDefault();
            MainWindow.Config.PS5Host = next?.Ip ?? "192.168.1.100";
        }

        MainWindow.SaveConfig();
        RefreshDevices();
        SaveAndNotify();
    }

    // ── Buttons ──────────────────────────────────────────────────────────────

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = AppPaths.Base, UseShellExecute = true });
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title      = "Export Config",
            Filter     = "JSON files (*.json)|*.json",
            FileName   = "ps5autopayload-config.json"
        };
        if (dlg.ShowDialog() != true) return;
        var json = ConfigManager.Export(MainWindow.Config);
        File.WriteAllText(dlg.FileName, json);
        TxtStatus.Text = "Config exported.";
    }

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import Config",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var json = File.ReadAllText(dlg.FileName);
        var (imported, importError) = ConfigManager.Import(json);
        if (imported == null)
        {
            MessageBox.Show($"Could not parse the config file.\n\n{importError}", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Overwrite current config
        MainWindow.Config.PS5Host                = imported.PS5Host;
        MainWindow.Config.GitHubToken            = imported.GitHubToken;
        MainWindow.Config.Sources                = imported.Sources;
        MainWindow.Config.PayloadMeta            = imported.PayloadMeta;
        MainWindow.Config.Profiles               = imported.Profiles;
        MainWindow.Config.Devices                = imported.Devices;
        MainWindow.Config.State.BuilderSteps     = imported.State.BuilderSteps;

        (Window.GetWindow(this) as MainWindow)?.OnConfigChanged();
        Refresh();
        TxtStatus.Text = "Config imported.";
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will remove all sources, payloads and profiles.\nAre you sure?",
            "Factory Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        MainWindow.Config.Sources              = new();
        MainWindow.Config.PayloadMeta          = new();
        MainWindow.Config.Profiles             = new();
        MainWindow.Config.Devices              = new();
        MainWindow.Config.State.BuilderSteps   = new();

        // Clear payload files and profiles
        if (Directory.Exists(AppPaths.PayloadsDir))
            foreach (var f in Directory.GetFiles(AppPaths.PayloadsDir))
                try { File.Delete(f); } catch { }
        if (Directory.Exists(AppPaths.ProfilesDir))
            foreach (var f in Directory.GetFiles(AppPaths.ProfilesDir))
                try { File.Delete(f); } catch { }
        if (Directory.Exists(AppPaths.CacheDir))
            foreach (var d in Directory.GetDirectories(AppPaths.CacheDir))
                try { Directory.Delete(d, true); } catch { }

        MainWindow.SaveConfig();
        Refresh();
        TxtStatus.Text = "Reset complete.";
    }

    private void LnkGitHub_Click(object sender, MouseButtonEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = "https://github.com/cosmicflow2512/PS5AutoPayloadTool",
            UseShellExecute = true
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static long GetDirSize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
