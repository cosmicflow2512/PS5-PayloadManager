using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Core;

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
            if (kv.Value != null)
                _items.Add(new PayloadEntry(kv.Key, kv.Value));

        EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Add local payload ─────────────────────────────────────────────────────

    private void BtnAddPayload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Select Payload File",
            Filter      = "Payload files (*.elf;*.bin;*.lua)|*.elf;*.bin;*.lua|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        int added = 0;
        foreach (var path in dlg.FileNames)
        {
            MainWindow.PayloadSvc.AddLocal(MainWindow.Config, path);
            added++;
        }

        MainWindow.SaveConfig();
        PopulateList();
        TxtStatus.Text = $"Added {added} payload(s).";
    }

    // ── Check all for updates ─────────────────────────────────────────────────

    private async void BtnCheckAll_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Checking for updates…";

        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => TxtStatus.Text = msg));

        await MainWindow.PayloadSvc.CheckUpdatesAsync(MainWindow.Config, progress);

        MainWindow.SaveConfig();
        TxtStatus.Text = "Update check complete.";
        PopulateList();
    }

    // ── Update all ───────────────────────────────────────────────────────────

    private async void BtnUpdateAll_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text         = "Updating all payloads…";
        PrgDownload.Value      = 0;
        PrgDownload.Visibility = Visibility.Visible;

        var progress = new Progress<(string Name, double Pct)>(p =>
            Dispatcher.Invoke(() =>
            {
                PrgDownload.Value = p.Pct;
                var entry = _items.FirstOrDefault(i => i.Name == p.Name);
                if (entry != null) entry.StatusText = "Updating…";
            }));

        var results = await MainWindow.PayloadSvc.UpdateAllAsync(MainWindow.Config, progress);

        foreach (var (name, success, msg) in results)
        {
            var entry = _items.FirstOrDefault(i => i.Name == name);
            if (entry != null) entry.StatusText = msg;
        }

        MainWindow.SaveConfig();
        TxtStatus.Text = $"Updated {results.Count(r => r.Success)} payload(s).";
        await Task.Delay(1500);
        PrgDownload.Visibility = Visibility.Collapsed;
        PopulateList();
    }

    // ── Download specific version ─────────────────────────────────────────────

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not PayloadEntry entry) return;
        var rawVersion = entry.RawDownloadVersion;
        if (string.IsNullOrEmpty(rawVersion)) return;

        if (entry.SourceNotAvailable)
        {
            entry.StatusText = "Source not available — cannot download.";
            return;
        }

        if (rawVersion == "local")
        {
            entry.StatusText = "Local payload — nothing to download.";
            return;
        }

        entry.StatusText       = "Downloading…";
        PrgDownload.Value      = 0;
        PrgDownload.Visibility = Visibility.Visible;

        try
        {
            var progress = new Progress<(long, long)>(p =>
                Dispatcher.Invoke(() =>
                {
                    if (p.Item2 > 0)
                    {
                        PrgDownload.Value = (double)p.Item1 / p.Item2 * 100.0;
                        entry.StatusText  = $"{p.Item1 * 100 / p.Item2}%";
                    }
                }));

            var error = await MainWindow.PayloadSvc.DownloadVersionAsync(
                MainWindow.Config, entry.Name, rawVersion, entry.Meta.SourceUrl, progress);

            if (error != null)
                entry.StatusText = $"Error: {error}";
            else
            {
                MainWindow.SaveConfig();
                entry.StatusText  = "Downloaded.";
                PrgDownload.Value = 100;
                PopulateList();
            }
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

        if (MessageBox.Show($"Remove \"{entry.Name}\"?\n\nThe local file will also be deleted.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        MainWindow.PayloadSvc.Delete(MainWindow.Config, entry.Name);
        MainWindow.SaveConfig();
        PopulateList();
        TxtStatus.Text = $"Deleted {entry.Name}.";
    }
}

/// <summary>View-model for a single PayloadMeta entry in the payloads list.</summary>
public class PayloadEntry : System.ComponentModel.INotifyPropertyChanged
{
    public PayloadEntry(string name, PayloadMeta meta)
    {
        Name = name;
        Meta = meta;
        var versions = AvailableVersions;
        _selectedVersion = versions.Count > 0 ? versions[0] : "";
    }

    public string      Name { get; }
    public PayloadMeta Meta { get; }

    /// <summary>
    /// Dropdown items: real version tags, newest labelled "(Latest)".
    /// No generic "Latest" pseudo-entry. Local/unsourced payloads show "local".
    /// </summary>
    public List<string> AvailableVersions
    {
        get
        {
            if (string.IsNullOrEmpty(Meta.SourceUrl) || Meta.Version == "local")
                return new List<string> { "local" };

            var versions = Meta.Versions
                .Where(v => v != "Latest" && v != "local" && !string.IsNullOrEmpty(v))
                .Distinct()
                .ToList();

            if (versions.Count == 0)
            {
                return string.IsNullOrEmpty(Meta.Version)
                    ? new List<string>()
                    : new List<string> { $"{Meta.Version} (Latest)" };
            }

            var result = new List<string>();
            for (int i = 0; i < versions.Count; i++)
                result.Add(i == 0 ? $"{versions[i]} (Latest)" : versions[i]);
            return result;
        }
    }

    /// <summary>
    /// Current-version line text.
    /// Format: "v1.03 (Latest)" / "v1.02 (Update available)" / "local" / "Latest"
    /// </summary>
    public string CurrentVersionDisplay
    {
        get
        {
            if (Meta.SourceNotAvailable)
                return $"{Meta.Version} (Source not available)";
            if (string.IsNullOrEmpty(Meta.SourceUrl) || Meta.Version == "local")
                return "local";
            if (string.IsNullOrEmpty(Meta.Version))
                return "Latest";
            if (Meta.HasUpdateAvailable)
                return $"{Meta.Version} (Update available)";
            return $"{Meta.Version} (Latest)";
        }
    }

    private string _selectedVersion = "";
    public string SelectedVersion
    {
        get => _selectedVersion;
        set { _selectedVersion = value ?? ""; OnChanged(nameof(SelectedVersion)); }
    }

    /// <summary>Strips the " (Latest)" display suffix to get the raw version tag for API calls.</summary>
    public string RawDownloadVersion
    {
        get
        {
            const string suffix = " (Latest)";
            return _selectedVersion.EndsWith(suffix)
                ? _selectedVersion[..^suffix.Length]
                : _selectedVersion;
        }
    }

    public bool IsDownloaded       => Meta.IsDownloaded;
    public bool HasUpdate          => Meta.HasUpdateAvailable && !Meta.SourceNotAvailable;
    public bool SourceNotAvailable => Meta.SourceNotAvailable;
    public bool IsDownloadEnabled  => !Meta.SourceNotAvailable
                                   && Meta.Version != "local"
                                   && !string.IsNullOrEmpty(Meta.SourceUrl);

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value ?? ""; OnChanged(nameof(StatusText)); OnChanged(nameof(HasStatus)); }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    private void OnChanged(string prop) =>
        PropertyChanged?.Invoke(this, new(prop));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
