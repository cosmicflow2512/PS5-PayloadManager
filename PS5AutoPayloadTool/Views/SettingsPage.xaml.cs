using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;

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
        TxtHost.Text  = MainWindow.Config.PS5Host;
        TxtToken.Text = MainWindow.Config.GitHubToken;
        TxtDataPath.Text = AppPaths.Base;

        // Stats
        var payloadCount = Directory.GetFiles(AppPaths.PayloadsDir).Length;
        var profileCount = Directory.GetFiles(AppPaths.ProfilesDir, "*.txt").Length;
        long cacheBytes  = GetDirSize(AppPaths.CacheDir);
        TxtStats.Text = $"{payloadCount} payload(s)  •  {profileCount} profile(s)  •  cache {FormatBytes(cacheBytes)}";

        _loading = false;
        TxtStatus.Text = "";
    }

    // ── Inputs ───────────────────────────────────────────────────────────────

    private void TxtHost_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        MainWindow.Config.PS5Host = TxtHost.Text.Trim();
        SaveAndNotify();
    }

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

        var json      = File.ReadAllText(dlg.FileName);
        var imported  = ConfigManager.Import(json);
        if (imported == null)
        {
            MessageBox.Show("Could not parse the config file.", "Import Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Overwrite current config
        MainWindow.Config.PS5Host      = imported.PS5Host;
        MainWindow.Config.GitHubToken  = imported.GitHubToken;
        MainWindow.Config.Sources      = imported.Sources;
        MainWindow.Config.Payloads     = imported.Payloads;
        MainWindow.Config.CurrentFlow  = imported.CurrentFlow;

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

        MainWindow.Config.Sources     = new();
        MainWindow.Config.Payloads    = new();
        MainWindow.Config.CurrentFlow = new();

        // Clear payload files and profiles
        foreach (var f in Directory.GetFiles(AppPaths.PayloadsDir))
            File.Delete(f);
        foreach (var f in Directory.GetFiles(AppPaths.ProfilesDir))
            File.Delete(f);
        foreach (var d in Directory.GetDirectories(AppPaths.CacheDir))
            Directory.Delete(d, true);

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
