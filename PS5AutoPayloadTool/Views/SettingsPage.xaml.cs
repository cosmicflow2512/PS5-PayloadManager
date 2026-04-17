using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Devices;
using PS5AutoPayloadTool.Modules.Execution;

namespace PS5AutoPayloadTool.Views;

public partial class SettingsPage : UserControl
{
    private bool _loading;

    public SettingsPage() { InitializeComponent(); }

    public void Refresh()
    {
        _loading         = true;
        TxtToken.Text    = MainWindow.Config.GitHubToken;
        TxtDataPath.Text = AppPaths.Base;

        RefreshDevices();

        var payloadCount = Directory.Exists(AppPaths.PayloadsDir)
            ? Directory.GetFiles(AppPaths.PayloadsDir).Length : 0;
        var profileCount = Directory.Exists(AppPaths.ProfilesDir)
            ? Directory.GetFiles(AppPaths.ProfilesDir, "*.txt").Length : 0;
        long cacheBytes  = GetDirSize(AppPaths.CacheDir);
        TxtStats.Text    = $"{payloadCount} payload(s)  •  {profileCount} profile(s)  •  cache {FormatBytes(cacheBytes)}";

        TxtElfPort.Text = MainWindow.Config.Ports.ElfPort.ToString();
        TxtLuaPort.Text = MainWindow.Config.Ports.LuaPort.ToString();
        TxtBinPort.Text = MainWindow.Config.Ports.BinPort.ToString();

        ChkDebugMode.IsChecked = MainWindow.Config.State.DebugMode;
        _loading       = false;
        TxtStatus.Text = "";
    }

    // ── Debug mode ───────────────────────────────────────────────────────────

    private void ChkDebugMode_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        MainWindow.Config.State.DebugMode = ChkDebugMode.IsChecked == true;
        LogService.DebugMode = MainWindow.Config.State.DebugMode;
        SaveAndNotify();
    }

    private void RefreshDevices()
    {
        var devices = MainWindow.Config.Devices;
        DevicesList.ItemsSource = null;
        DevicesList.ItemsSource = devices;
        TxtNoDevices.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Token ────────────────────────────────────────────────────────────────

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
        TxtDeviceName.Text       = "";
        TxtDeviceIp.Text         = "192.168.1.";
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

        var device = DeviceService.Add(MainWindow.Config, ip, name);
        if (device == null)
        {
            TxtStatus.Text = "A device with that IP already exists.";
            return;
        }

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
        DeviceService.Remove(MainWindow.Config, dev);
        MainWindow.SaveConfig();
        RefreshDevices();
        SaveAndNotify();
    }

    // ── Port settings ────────────────────────────────────────────────────────

    private void BtnSavePorts_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtElfPort.Text.Trim(), out var newElf) || newElf < 1 || newElf > 65535)
        {
            TxtStatus.Text  = "ELF port must be 1–65535.";
            TxtElfPort.Text = MainWindow.Config.Ports.ElfPort.ToString();
            return;
        }

        if (!int.TryParse(TxtLuaPort.Text.Trim(), out var newLua) || newLua < 1 || newLua > 65535)
        {
            TxtStatus.Text  = "LUA port must be 1–65535.";
            TxtLuaPort.Text = MainWindow.Config.Ports.LuaPort.ToString();
            return;
        }

        var binText = TxtBinPort.Text.Trim();
        if (string.IsNullOrEmpty(binText)) binText = "0";
        if (!int.TryParse(binText, out var newBin) || newBin < 0 || newBin > 65535)
        {
            TxtStatus.Text  = "BIN port must be 0 or 1–65535.";
            TxtBinPort.Text = MainWindow.Config.Ports.BinPort.ToString();
            return;
        }

        bool changed = newElf != MainWindow.Config.Ports.ElfPort ||
                       newLua != MainWindow.Config.Ports.LuaPort ||
                       newBin != MainWindow.Config.Ports.BinPort;

        MainWindow.Config.Ports.ElfPort = newElf;
        MainWindow.Config.Ports.LuaPort = newLua;
        MainWindow.Config.Ports.BinPort = newBin;
        MainWindow.SaveConfig();
        TxtStatus.Text = "Port settings saved.";

        if (!changed) return;

        var payloadSteps = MainWindow.Config.State.BuilderSteps
            .Where(s => s.Type == "payload").ToList();

        if (payloadSteps.Count == 0) return;

        var result = MessageBox.Show(
            $"Apply new port settings to {payloadSteps.Count} existing builder step(s)?",
            "Update Builder Steps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        foreach (var step in payloadSteps)
            step.Port = PayloadSender.GetDefaultPort(step.Payload, MainWindow.Config.Ports);

        MainWindow.SaveConfig();
        TxtStatus.Text = $"Port settings saved. {payloadSteps.Count} step(s) updated.";
    }

    // ── Data folder ──────────────────────────────────────────────────────────

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = AppPaths.Base, UseShellExecute = true });
    }

    // ── Backup export ─────────────────────────────────────────────────────────

    private void BtnExportBackup_Click(object sender, RoutedEventArgs e)
    {
        bool includePayloads = ChkIncludePayloads.IsChecked == true;

        var dlg = new SaveFileDialog
        {
            Title    = "Export Backup ZIP",
            Filter   = "ZIP backup (*.zip)|*.zip",
            FileName = "ps5autopayload-backup.zip"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            ConfigManager.ExportBackupZip(MainWindow.Config, includePayloads, dlg.FileName);
            var count = Directory.Exists(AppPaths.PayloadsDir)
                ? Directory.GetFiles(AppPaths.PayloadsDir).Length : 0;
            TxtStatus.Text = includePayloads
                ? $"Backup exported ({count} payload(s) included)."
                : "Backup exported (config only — payloads not included).";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Export failed: {ex.Message}";
        }
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title    = "Export Config JSON",
            Filter   = "JSON files (*.json)|*.json",
            FileName = "ps5autopayload-config.json"
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, ConfigManager.Export(MainWindow.Config));
        TxtStatus.Text = "Config JSON exported.";
    }

    // ── Import ───────────────────────────────────────────────────────────────

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import Config or Backup",
            Filter = "Backup files (*.zip;*.json)|*.zip;*.json|ZIP backup (*.zip)|*.zip|JSON config (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        AppConfig? imported;
        string[]   restoredFromZip;

        if (dlg.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var (cfg, restored, err) = ConfigManager.ImportFromBackupZip(dlg.FileName);
            if (cfg == null)
            {
                MessageBox.Show($"Could not import backup.\n\n{err}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            imported        = cfg;
            restoredFromZip = restored;
        }
        else
        {
            var json = File.ReadAllText(dlg.FileName);
            var (cfg, err) = ConfigManager.Import(json);
            if (cfg == null)
            {
                MessageBox.Show($"Could not parse the config file.\n\n{err}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            imported        = cfg;
            restoredFromZip = Array.Empty<string>();
        }

        // Apply imported config
        MainWindow.Config.PS5Host            = imported.PS5Host;
        MainWindow.Config.GitHubToken        = imported.GitHubToken;
        MainWindow.Config.Sources            = imported.Sources;
        MainWindow.Config.PayloadMeta        = imported.PayloadMeta;
        MainWindow.Config.Profiles           = imported.Profiles;
        MainWindow.Config.Devices            = imported.Devices;
        MainWindow.Config.State.BuilderSteps = imported.State.BuilderSteps;

        // Fix up LocalPath for payloads already present on disk
        foreach (var kv in MainWindow.Config.PayloadMeta)
        {
            var localFile = Path.Combine(AppPaths.PayloadsDir, kv.Key);
            if (File.Exists(localFile))
                kv.Value.LocalPath = localFile;
        }

        (Window.GetWindow(this) as MainWindow)?.OnConfigChanged();
        MainWindow.SaveConfig();
        Refresh();

        var zCount = restoredFromZip.Length;
        TxtStatus.Text = zCount > 0
            ? $"Imported. {zCount} payload(s) restored from backup. Resolving missing payloads…"
            : "Config imported. Resolving missing payloads…";

        await ResolvePayloadsAsync(new HashSet<string>(restoredFromZip, StringComparer.OrdinalIgnoreCase));
    }

    // ── Payload auto-resolution ──────────────────────────────────────────────

    private async Task ResolvePayloadsAsync(HashSet<string> alreadyRestored)
    {
        PrgRestore.Value         = 0;
        PrgRestore.Visibility    = Visibility.Visible;
        TxtRestoreLog.Text       = "";
        TxtRestoreLog.Visibility = Visibility.Visible;

        int resolved = 0, total = 0;

        var progress = new Progress<(string Name, string Message, double Pct)>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                AppendRestoreLog(p.Message);
                PrgRestore.Value = p.Pct;
                if (p.Message.Contains("restored") || p.Message.Contains("found locally")
                                                    || p.Message.Contains("updated to"))
                    Interlocked.Increment(ref resolved);
                Interlocked.Increment(ref total);
            });
        });

        await MainWindow.PayloadSvc.ResolveAfterImportAsync(
            MainWindow.Config, alreadyRestored, progress);

        MainWindow.SaveConfig();
        Refresh();
        PrgRestore.Value = 100;
        TxtStatus.Text   = "Import complete. See restore log for details.";
    }

    private void AppendRestoreLog(string line)
    {
        TxtRestoreLog.Text = string.IsNullOrEmpty(TxtRestoreLog.Text)
            ? line : TxtRestoreLog.Text + "\n" + line;
    }

    // ── Factory reset ────────────────────────────────────────────────────────

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will remove all sources, payloads and profiles.\nAre you sure?",
            "Factory Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        MainWindow.Config.Sources            = new();
        MainWindow.Config.PayloadMeta        = new();
        MainWindow.Config.Profiles           = new();
        MainWindow.Config.Devices            = new();
        MainWindow.Config.State.BuilderSteps = new();

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

    // ── About ────────────────────────────────────────────────────────────────

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
        if (bytes < 1024)        return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
