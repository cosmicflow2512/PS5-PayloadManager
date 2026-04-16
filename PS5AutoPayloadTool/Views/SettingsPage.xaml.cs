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

        var payloadCount = Directory.Exists(AppPaths.PayloadsDir)
            ? Directory.GetFiles(AppPaths.PayloadsDir).Length : 0;
        var profileCount = Directory.Exists(AppPaths.ProfilesDir)
            ? Directory.GetFiles(AppPaths.ProfilesDir, "*.txt").Length : 0;
        long cacheBytes = GetDirSize(AppPaths.CacheDir);
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

        if (MainWindow.Config.Devices.Any(d => d.Ip == ip))
        {
            TxtStatus.Text = "A device with that IP already exists.";
            return;
        }

        MainWindow.Config.Devices.Add(new DeviceConfig { Name = name, Ip = ip });

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

        if (MainWindow.Config.PS5Host == dev.Ip)
        {
            var next = MainWindow.Config.Devices.FirstOrDefault();
            MainWindow.Config.PS5Host = next?.Ip ?? "192.168.1.100";
        }

        MainWindow.SaveConfig();
        RefreshDevices();
        SaveAndNotify();
    }

    // ── Data folder ──────────────────────────────────────────────────────────

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = AppPaths.Base, UseShellExecute = true });
    }

    // ── Export ───────────────────────────────────────────────────────────────

    /// <summary>Exports config + optional payload files as a portable ZIP backup.</summary>
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
            var payloadCount = Directory.Exists(AppPaths.PayloadsDir)
                ? Directory.GetFiles(AppPaths.PayloadsDir).Length : 0;
            TxtStatus.Text = includePayloads
                ? $"Backup exported ({payloadCount} payload(s) included)."
                : "Backup exported (config only — payloads not included).";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>Exports config as plain JSON (for HA compatibility).</summary>
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

        // Fix up LocalPath for any payloads that already exist in PayloadsDir
        // (covers payloads restored from ZIP and previously-downloaded payloads)
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

        // Auto-download any remaining missing payloads
        await ResolvePayloadsAsync(new HashSet<string>(restoredFromZip, StringComparer.OrdinalIgnoreCase));
    }

    // ── Payload auto-resolution ──────────────────────────────────────────────

    /// <summary>
    /// After import, checks every payload in PayloadMeta.  Payloads in
    /// <paramref name="alreadyRestored"/> or already present on disk are skipped.
    /// For the rest, the tool tries to download from the registered source:
    ///   1. exact version match
    ///   2. any version of that payload (fallback to latest)
    ///   3. if no source → warn and skip
    /// </summary>
    private async Task ResolvePayloadsAsync(HashSet<string> alreadyRestored)
    {
        // Collect payloads whose file is genuinely absent
        var missing = MainWindow.Config.PayloadMeta
            .Where(kv => !alreadyRestored.Contains(kv.Key)
                      && !File.Exists(Path.Combine(AppPaths.PayloadsDir, kv.Key)))
            .ToList();

        if (missing.Count == 0)
        {
            HideRestoreProgress();
            TxtStatus.Text = "Import complete. All payloads present.";
            return;
        }

        PrgRestore.Value      = 0;
        PrgRestore.Visibility = Visibility.Visible;
        TxtRestoreLog.Text    = "";
        TxtRestoreLog.Visibility = Visibility.Visible;

        int resolved = 0;

        for (int i = 0; i < missing.Count; i++)
        {
            var (name, meta) = missing[i];
            PrgRestore.Value = (double)(i + 1) / missing.Count * 100;

            // Re-check: might have been written as a side-effect of a sibling ZIP
            var localFile = Path.Combine(AppPaths.PayloadsDir, name);
            if (File.Exists(localFile))
            {
                meta.LocalPath = localFile;
                resolved++;
                AppendRestoreLog($"{name}: found locally.");
                continue;
            }

            if (string.IsNullOrEmpty(meta.SourceUrl))
            {
                AppendRestoreLog($"{name}: no source — skipped.");
                continue;
            }

            var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Url == meta.SourceUrl);
            if (source == null)
            {
                AppendRestoreLog($"{name}: source not in config — skipped.");
                continue;
            }

            try
            {
                TxtStatus.Text = $"Downloading {name}…";

                var found = await MainWindow.PayloadMgr.ScanSourceAsync(source);

                // Priority: exact version → any version (latest first)
                var match = found.FirstOrDefault(f => f.Name == name && f.Version == meta.Version);
                if (match == default)
                    match = found.FirstOrDefault(f => f.Name == name);

                if (match == default)
                {
                    AppendRestoreLog($"{name}: not found in source — skipped.");
                    continue;
                }

                await MainWindow.PayloadMgr.DownloadAsync(
                    MainWindow.Config, name, match.DownloadUrl, match.Version, source.Url);

                if (match.Version != meta.Version && !string.IsNullOrEmpty(meta.Version))
                    AppendRestoreLog($"{name}: updated to {match.Version} (requested {meta.Version}).");
                else
                    AppendRestoreLog($"{name}: restored ({match.Version}).");

                resolved++;
            }
            catch (Exception ex)
            {
                AppendRestoreLog($"{name}: download failed — {ex.Message}");
            }
        }

        MainWindow.SaveConfig();
        Refresh();
        PrgRestore.Value = 100;

        TxtStatus.Text = resolved == missing.Count
            ? $"Import complete. All {resolved} missing payload(s) resolved."
            : $"Import complete. {resolved}/{missing.Count} payload(s) resolved. See log above for details.";
    }

    private void AppendRestoreLog(string line)
    {
        TxtRestoreLog.Text = string.IsNullOrEmpty(TxtRestoreLog.Text)
            ? line : TxtRestoreLog.Text + "\n" + line;
    }

    private void HideRestoreProgress()
    {
        PrgRestore.Visibility    = Visibility.Collapsed;
        TxtRestoreLog.Visibility = Visibility.Collapsed;
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
        if (bytes < 1024)             return $"{bytes} B";
        if (bytes < 1024 * 1024)      return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
