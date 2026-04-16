using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class PayloadsPage : UserControl
{
    private readonly ObservableCollection<PayloadEntry> _items = new();

    public PayloadsPage() { InitializeComponent(); PayloadsList.ItemsSource = _items; }

    public void Refresh() => PopulateList();

    private void PopulateList()
    {
        _items.Clear();
        var meta = MainWindow.Config.PayloadMeta;
        if (meta != null)
            foreach (var kv in meta)
                if (kv.Value != null)
                    _items.Add(new PayloadEntry(kv.Key, kv.Value));

        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Add local payload ─────────────────────────────────────────────────────

    private void BtnAddPayload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select Payload File",
            Filter = "Payload files (*.elf;*.bin;*.lua)|*.elf;*.bin;*.lua|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        int added = 0;
        foreach (var path in dlg.FileNames)
        {
            var name = Path.GetFileName(path);
            var dest = Path.Combine(AppPaths.PayloadsDir, name);
            Directory.CreateDirectory(AppPaths.PayloadsDir);
            File.Copy(path, dest, overwrite: true);

            if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                {
                    Version   = "local",
                    Versions  = new() { "local" },
                    LocalPath = dest,
                    Size      = new FileInfo(dest).Length
                };
            else
            {
                var m = MainWindow.Config.PayloadMeta[name];
                m.LocalPath = dest;
                m.Size      = new FileInfo(dest).Length;
                if (!m.Versions.Contains("local")) m.Versions.Insert(0, "local");
            }
            added++;
        }

        MainWindow.SaveConfig();
        PopulateList();
        TxtStatus.Text = $"Added {added} payload(s).";
    }

    // ── Check All Updates ────────────────────────────────────────────────────

    private async void BtnCheckAll_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Checking for updates...";
        var tasks = new List<Task>();

        foreach (var source in MainWindow.Config.Sources)
        {
            var src = source;
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var found = await MainWindow.PayloadMgr.ScanSourceAsync(src);
                    Dispatcher.Invoke(() =>
                    {
                        foreach (var (name, _, version, size, remoteHash) in found)
                        {
                            if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                            {
                                MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                                    { SourceUrl = src.Url, Versions = new() { version }, Version = version, Size = size };
                            }
                            else
                            {
                                var meta = MainWindow.Config.PayloadMeta[name];
                                if (!meta.Versions.Contains(version)) meta.Versions.Add(version);

                                bool versionChanged = version != "folder" && meta.Version != version;
                                bool hashChanged    = remoteHash != null
                                                   && !string.IsNullOrEmpty(meta.FileHash)
                                                   && remoteHash != meta.FileHash;
                                meta.HasUpdateAvailable = versionChanged || hashChanged;
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => TxtStatus.Text = $"Error: {ex.Message}");
                }
            }));
        }

        await Task.WhenAll(tasks);
        MainWindow.SaveConfig();
        TxtStatus.Text = "Update check complete.";
        PopulateList();
    }

    // ── Update All ───────────────────────────────────────────────────────────

    private async void BtnUpdateAll_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Updating all payloads...";
        PrgDownload.Value = 0;
        PrgDownload.Visibility = Visibility.Visible;

        int updated = 0;
        int total   = _items.Count;

        foreach (var entry in _items.ToList())
        {
            if (string.IsNullOrEmpty(entry.Meta.SourceUrl)) continue;

            var source = MainWindow.Config.Sources
                .FirstOrDefault(s => s.Url == entry.Meta.SourceUrl);
            if (source == null) continue;

            try
            {
                var found  = await MainWindow.PayloadMgr.ScanSourceAsync(source);
                var latest = found.FirstOrDefault(f => f.Name == entry.Name);
                if (latest == default) continue;

                entry.StatusText = $"Downloading {latest.Version}...";

                await MainWindow.PayloadMgr.DownloadAsync(
                    MainWindow.Config, entry.Name, latest.DownloadUrl,
                    latest.Version, source.Url);

                entry.Meta.Version           = latest.Version;
                entry.Meta.HasUpdateAvailable = false;
                entry.StatusText = $"Updated to {latest.Version}";
                updated++;
            }
            catch (Exception ex)
            {
                entry.StatusText = $"Error: {ex.Message}";
            }

            Dispatcher.Invoke(() =>
                PrgDownload.Value = (double)updated / Math.Max(total, 1) * 100.0);
        }

        MainWindow.SaveConfig();
        TxtStatus.Text = $"Updated {updated} payload(s).";
        await Task.Delay(1500);
        PrgDownload.Visibility = Visibility.Collapsed;
        PopulateList();
    }

    // ── Download specific version ─────────────────────────────────────────────

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PayloadEntry entry) return;
        var selectedVersion = entry.SelectedVersion;
        if (string.IsNullOrEmpty(selectedVersion)) return;

        if (selectedVersion == "local")
        {
            entry.StatusText = "Local payload — nothing to download.";
            return;
        }

        entry.StatusText = "Downloading...";
        PrgDownload.Value = 0;
        PrgDownload.Visibility = Visibility.Visible;

        try
        {
            var source = MainWindow.Config.Sources
                .FirstOrDefault(s => s.Url == entry.Meta.SourceUrl);
            if (source == null) { entry.StatusText = "Source not found."; return; }

            var found = await MainWindow.PayloadMgr.ScanSourceAsync(source);
            var match = found.FirstOrDefault(f => f.Name == entry.Name && f.Version == selectedVersion);
            if (match == default) { entry.StatusText = "Version not found in source."; return; }

            var progress = new Progress<(long, long)>(p =>
                Dispatcher.Invoke(() =>
                {
                    if (p.Item2 > 0)
                    {
                        PrgDownload.Value = (double)p.Item1 / p.Item2 * 100.0;
                        entry.StatusText  = $"{p.Item1 * 100 / p.Item2}%";
                    }
                }));

            await MainWindow.PayloadMgr.DownloadAsync(
                MainWindow.Config, entry.Name, match.DownloadUrl,
                selectedVersion, source.Url, progress);

            MainWindow.SaveConfig();
            entry.StatusText = "Downloaded.";
            PrgDownload.Value = 100;
            PopulateList();
        }
        catch (Exception ex) { entry.StatusText = $"Error: {ex.Message}"; }
        finally
        {
            await Task.Delay(1500);
            PrgDownload.Visibility = Visibility.Collapsed;
        }
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    private void BtnDeletePayload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PayloadEntry entry) return;

        if (MessageBox.Show($"Remove \"{entry.Name}\"?\n\nThe local file will also be deleted.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        var path = Path.Combine(AppPaths.PayloadsDir, entry.Name);
        if (File.Exists(path)) try { File.Delete(path); } catch { }

        MainWindow.Config.PayloadMeta.Remove(entry.Name);
        MainWindow.SaveConfig();
        PopulateList();
        TxtStatus.Text = $"Deleted {entry.Name}.";
    }
}

/// <summary>View-model for a PayloadMeta entry.</summary>
public class PayloadEntry : System.ComponentModel.INotifyPropertyChanged
{
    public PayloadEntry(string name, PayloadMeta meta)
    {
        Name            = name;
        Meta            = meta;
        SelectedVersion = meta.Version;
    }

    public string      Name             { get; }
    public PayloadMeta Meta             { get; }
    public List<string> AvailableVersions => Meta.Versions;
    public string?     CurrentVersion   => Meta.Version;
    public string      CurrentVersionDisplay =>
        Meta.HasUpdateAvailable
            ? $"{Meta.Version}  —  Update available"
            : $"{Meta.Version}  —  Latest";
    public string      SelectedVersion  { get; set; }
    public bool        IsDownloaded     => Meta.IsDownloaded;
    public bool        HasUpdate        => Meta.HasUpdateAvailable;
    public string      Id               => Name;

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnChanged(nameof(StatusText)); OnChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    private void OnChanged(string prop) =>
        PropertyChanged?.Invoke(this, new(prop));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
