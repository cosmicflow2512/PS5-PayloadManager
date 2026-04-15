using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        foreach (var kv in MainWindow.Config.PayloadMeta)
            _items.Add(new PayloadEntry(kv.Key, kv.Value));

        EmptyState.Visibility = _items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── Check All Updates ────────────────────────────────────────────────────

    private async void BtnCheckAll_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Checking for updates...";

        var tasks = new List<Task>();
        foreach (var source in MainWindow.Config.Sources)
        {
            var src = source;
            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => TxtStatus.Text = msg));

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var found = await MainWindow.PayloadMgr.ScanSourceAsync(src, progress);
                    Dispatcher.Invoke(() =>
                    {
                        foreach (var (name, url, version, size) in found)
                        {
                            if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                            {
                                MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                                {
                                    SourceUrl = src.Url,
                                    Versions  = new() { version },
                                    Version   = version,
                                    Size      = size
                                };
                            }
                            else
                            {
                                var meta = MainWindow.Config.PayloadMeta[name];
                                if (!meta.Versions.Contains(version))
                                    meta.Versions.Add(version);
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

    // ── Download ─────────────────────────────────────────────────────────────

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PayloadEntry entry) return;
        var selectedVersion = entry.SelectedVersion;
        if (string.IsNullOrEmpty(selectedVersion)) return;

        entry.StatusText = "Downloading...";
        PrgDownload.Value = 0;
        PrgDownload.Visibility = Visibility.Visible;

        try
        {
            // Find download URL from scan
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
                        entry.StatusText = $"{p.Item1 * 100 / p.Item2}%";
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
        catch (Exception ex)
        {
            entry.StatusText = $"Error: {ex.Message}";
        }
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

        var result = MessageBox.Show(
            $"Remove \"{entry.Name}\" from the payload list?\n\nThe local file will also be deleted if it exists.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var path = Path.Combine(AppPaths.PayloadsDir, entry.Name);
        if (File.Exists(path))
        {
            try { File.Delete(path); }
            catch { /* ignore */ }
        }

        MainWindow.Config.PayloadMeta.Remove(entry.Name);
        MainWindow.SaveConfig();
        PopulateList();
        TxtStatus.Text = $"Deleted {entry.Name}.";
    }
}

/// <summary>View-model wrapper for a PayloadMeta entry.</summary>
public class PayloadEntry(string name, PayloadMeta meta) : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; } = name;
    public PayloadMeta Meta { get; } = meta;
    public List<string> AvailableVersions => meta.Versions;
    public string? CurrentVersion => meta.Version;
    public string SelectedVersion { get; set; } = meta.Version;
    public bool IsDownloaded => meta.IsDownloaded;

    // Expose Id so XAML Tag="{Binding Id}" still binds (Id = Name)
    public string Id => name;

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; PropertyChanged?.Invoke(this, new(nameof(StatusText))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
