using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class SourcesView : UserControl
{
    private readonly GitHubClient _gh = new();
    private readonly ObservableCollection<SourceRow> _rows = [];
    private readonly ObservableCollection<DetectedRow> _detected = [];
    private string? _editingRepo;
    private List<ReleaseAsset> _detectedAssets = [];

    public SourcesView()
    {
        InitializeComponent();
        SourcesList.ItemsSource = _rows;
        DetectedList.ItemsSource = _detected;
        Refresh();
    }

    public void Refresh()
    {
        _rows.Clear();
        var sources = Storage.LoadSources();
        foreach (var s in sources)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(s.Filter)) parts.Add(s.Filter);
            if (!string.IsNullOrEmpty(s.SourceType) && s.SourceType != "auto") parts.Add(s.SourceType);
            if (!string.IsNullOrEmpty(s.Folder)) parts.Add(s.Folder);
            _rows.Add(new SourceRow
            {
                Repo    = s.Repo,
                Display = string.IsNullOrEmpty(s.DisplayName) ? s.Repo : s.DisplayName,
                SubText = parts.Count > 0 ? string.Join(" · ", parts) : "auto",
            });
        }
        CountBadge.Text = $"{_rows.Count} source{(_rows.Count != 1 ? "s" : "")}";
    }

    private void OnAddSource(object sender, RoutedEventArgs e)
    {
        if (AddPanel.Visibility == Visibility.Visible && _editingRepo == null)
        { ClosePanel(); return; }
        _editingRepo = null;
        PanelTitle.Text = "Add Source";
        RepoInput.Text = ""; DisplayInput.Text = ""; FilterInput.Text = "";
        ScanStatus.Text = "";
        BtnFetch.Visibility  = Visibility.Visible;
        BtnSave.Visibility   = Visibility.Collapsed;
        BtnImport.Visibility = Visibility.Collapsed;
        DetectedPanel.Visibility = Visibility.Collapsed;
        _detected.Clear();
        AddPanel.Visibility = Visibility.Visible;
        RepoInput.IsReadOnly = false;
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string repo) return;
        var src = Storage.LoadSources().FirstOrDefault(s => s.Repo == repo);
        if (src == null) return;
        _editingRepo = repo;
        PanelTitle.Text = "Edit Source";
        RepoInput.Text = src.Repo; RepoInput.IsReadOnly = true;
        DisplayInput.Text = src.DisplayName ?? "";
        FilterInput.Text = src.Filter ?? "";
        ScanStatus.Text = "";
        BtnFetch.Visibility  = Visibility.Visible;
        BtnSave.Visibility   = Visibility.Visible;
        BtnImport.Visibility = Visibility.Collapsed;
        DetectedPanel.Visibility = Visibility.Collapsed;
        _detected.Clear();
        AddPanel.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => ClosePanel();

    private void ClosePanel()
    {
        AddPanel.Visibility = Visibility.Collapsed;
        _editingRepo = null;
        _detectedAssets.Clear();
        _detected.Clear();
    }

    private async void OnFetch(object sender, RoutedEventArgs e)
    {
        var repo = NormalizeRepo(RepoInput.Text.Trim());
        if (string.IsNullOrEmpty(repo)) { ScanStatus.Text = "Enter a repository."; return; }

        ScanStatus.Text = "Scanning …";
        ScanStatus.Foreground = (Brush)FindResource("TextMuted");
        BtnFetch.IsEnabled = false;

        try
        {
            if (_editingRepo == null)
            {
                var sources = Storage.LoadSources();
                if (!sources.Any(s => s.Repo == repo))
                    sources.Add(new SourceEntry { Repo = repo, Filter = FilterInput.Text.Trim(), DisplayName = DisplayInput.Text.Trim() });
                Storage.SaveSources(sources);
            }

            _detectedAssets = await _gh.GetReleasesAsync(repo, FilterInput.Text.Trim());
            _detected.Clear();
            foreach (var a in _detectedAssets)
            {
                var ext = System.IO.Path.GetExtension(a.Name).TrimStart('.').ToLower();
                var displayName = a.IsZip ? System.IO.Path.GetFileNameWithoutExtension(a.Name) + " (zip)" : a.Name;
                var badgeColor = ext switch { "lua" => (Brush)new SolidColorBrush(Color.FromRgb(139, 92, 246)), "elf" => new SolidColorBrush(Color.FromRgb(59, 130, 246)), _ => new SolidColorBrush(Color.FromRgb(107, 114, 128)) };
                _detected.Add(new DetectedRow { Name = displayName, Version = a.Tag, IsChecked = true, BadgeColor = badgeColor, ExtUpper = ext.ToUpper() });
            }

            ScanStatus.Text = $"Found {_detected.Count} payload(s)";
            ScanStatus.Foreground = (Brush)FindResource("Success");
            DetectedPanel.Visibility = _detected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetectedCount.Text = $"{_detected.Count} found";
            BtnImport.Visibility  = _detected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnImport.IsEnabled   = _detected.Count > 0;
            LogBus.Log($"Source {repo}: found {_detected.Count} payload(s)", LogLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            ScanStatus.Text = "Error: " + ex.Message;
            ScanStatus.Foreground = (Brush)FindResource("Danger");
            LogBus.Log("Add source error: " + ex.Message, LogLevel.Error);
        }
        BtnFetch.IsEnabled = true;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_editingRepo == null) return;
        var sources = Storage.LoadSources();
        var src = sources.FirstOrDefault(s => s.Repo == _editingRepo);
        if (src != null)
        {
            src.Filter = FilterInput.Text.Trim();
            src.DisplayName = DisplayInput.Text.Trim();
            Storage.SaveSources(sources);
            LogBus.Log($"Source {_editingRepo} saved", LogLevel.Success);
        }
        ClosePanel();
        Refresh();
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var toImport = _detected.Select((d, i) => (d, i)).Where(x => x.d.IsChecked).ToList();
        if (toImport.Count == 0) return;
        BtnImport.IsEnabled = false;
        int imported = 0;
        var meta = Storage.LoadPayloadMeta();
        foreach (var (row, idx) in toImport)
        {
            if (idx >= _detectedAssets.Count) continue;
            var a = _detectedAssets[idx];
            try
            {
                var (data, realName) = await _gh.DownloadPayloadAsync(a.DownloadUrl, a.Name);
                if (data == null) continue;
                await File.WriteAllBytesAsync(Path.Combine(AppPaths.PayloadsDir, realName), data);
                meta[realName] = new PayloadMeta { Repo = NormalizeRepo(RepoInput.Text), Version = a.Tag, DownloadUrl = a.DownloadUrl, PublishedAt = a.PublishedAt, AssetSize = a.Size, ReleaseId = a.ReleaseId };
                imported++;
            }
            catch (Exception ex) { LogBus.Log($"Import {a.Name} failed: {ex.Message}", LogLevel.Error); }
        }
        Storage.SavePayloadMeta(meta);
        LogBus.Log($"Imported {imported} payload(s)", LogLevel.Success);
        ClosePanel();
        Refresh();
        BtnImport.IsEnabled = true;
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateStatus.Foreground = (Brush)FindResource("TextMuted");
        UpdateStatus.Text = "Checking updates…";
        try
        {
            var sources = Storage.LoadSources();
            var meta = Storage.LoadPayloadMeta();
            var updates = await _gh.CheckUpdatesAsync(sources, meta);
            if (updates.Count == 0)
            {
                UpdateStatus.Text = "✓ All up to date";
                UpdateStatus.Foreground = (Brush)FindResource("Success");
            }
            else
            {
                UpdateStatus.Text = $"{updates.Count} update(s) available";
                UpdateStatus.Foreground = (Brush)FindResource("Warn");
                if (MessageBox.Show($"{updates.Count} update(s) available. Download now?", "Updates", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    foreach (var u in updates)
                    {
                        var (data, name) = await _gh.DownloadPayloadAsync(u.DownloadUrl, u.Filename);
                        if (data == null) continue;
                        await File.WriteAllBytesAsync(Path.Combine(AppPaths.PayloadsDir, name), data);
                        if (meta.TryGetValue(name, out var m)) { m.Version = u.NewVersion; m.DownloadUrl = u.DownloadUrl; }
                        LogBus.Log($"Updated {name} → {u.NewVersion}", LogLevel.Success);
                    }
                    Storage.SavePayloadMeta(meta);
                    UpdateStatus.Text = "✓ All up to date";
                    UpdateStatus.Foreground = (Brush)FindResource("Success");
                }
            }
        }
        catch (Exception ex) { UpdateStatus.Text = "Error: " + ex.Message; UpdateStatus.Foreground = (Brush)FindResource("Danger"); }
    }

    private void OnScanOne(object sender, RoutedEventArgs e) => OnCheckUpdates(sender, e);

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string repo) return;
        if (MessageBox.Show($"Remove source '{repo}'?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var sources = Storage.LoadSources();
        sources.RemoveAll(s => s.Repo == repo);
        Storage.SaveSources(sources);
        LogBus.Log($"Removed source: {repo}", LogLevel.Info);
        Refresh();
    }

    private void OnSelectAllDetected(object sender, RoutedEventArgs e) { foreach (var d in _detected) d.IsChecked = true; }
    private void OnDeselectDetected(object sender, RoutedEventArgs e)  { foreach (var d in _detected) d.IsChecked = false; }

    private static string NormalizeRepo(string r)
    {
        r = r.Trim().TrimEnd('/');
        if (r.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) r = r["https://github.com/".Length..];
        else if (r.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))    r = r["github.com/".Length..];
        return r;
    }
}

public class SourceRow
{
    public string Repo    { get; set; } = "";
    public string Display { get; set; } = "";
    public string SubText { get; set; } = "";
}

public class DetectedRow : INotifyPropertyChanged
{
    private bool _isChecked = true;
    public string Name      { get; set; } = "";
    public string Version   { get; set; } = "";
    public string ExtUpper  { get; set; } = "";
    public Brush  BadgeColor { get; set; } = Brushes.Gray;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
