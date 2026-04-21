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

    // Maps deduplicated display key → all ReleaseAssets (latest first)
    private Dictionary<string, List<ReleaseAsset>> _allVersionsMap = [];

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
            if (!string.IsNullOrEmpty(s.Folder)) parts.Add("/" + s.Folder);
            _rows.Add(new SourceRow
            {
                Repo     = s.Repo,
                Display  = string.IsNullOrEmpty(s.DisplayName) ? s.Repo : s.DisplayName,
                SubText  = parts.Count > 0 ? string.Join(" · ", parts) : (s.SourceType == "folder" ? "folder scan" : "latest releases"),
                TypeIcon = s.SourceType == "folder" ? "📁" : "📦",
            });
        }
        CountBadge.Text = $"{_rows.Count} source{(_rows.Count != 1 ? "s" : "")}";
    }

    // ── Source type toggle ────────────────────────────────────────────────────

    private void OnSourceTypeChanged(object sender, RoutedEventArgs e)
    {
        if (FolderRow == null) return;
        FolderRow.Visibility = TypeFolder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Add / Edit panel ──────────────────────────────────────────────────────

    private void OnAddSource(object sender, RoutedEventArgs e)
    {
        if (AddPanel.Visibility == Visibility.Visible && _editingRepo == null)
        { ClosePanel(); return; }
        _editingRepo = null;
        PanelTitle.Text = "Add Source";
        RepoInput.Text = ""; DisplayInput.Text = ""; FilterInput.Text = ""; FolderInput.Text = "";
        TypeRelease.IsChecked = true;
        FolderRow.Visibility = Visibility.Collapsed;
        ScanStatus.Text = "";
        BtnFetch.Visibility  = Visibility.Visible;
        BtnSave.Visibility   = Visibility.Collapsed;
        BtnImport.Visibility = Visibility.Collapsed;
        DetectedPanel.Visibility = Visibility.Collapsed;
        _detected.Clear();
        _allVersionsMap.Clear();
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
        FilterInput.Text  = src.Filter ?? "";
        FolderInput.Text  = src.Folder ?? "";
        TypeRelease.IsChecked = src.SourceType != "folder";
        TypeFolder.IsChecked  = src.SourceType == "folder";
        FolderRow.Visibility  = src.SourceType == "folder" ? Visibility.Visible : Visibility.Collapsed;
        ScanStatus.Text = "";
        BtnFetch.Visibility  = Visibility.Visible;
        BtnSave.Visibility   = Visibility.Visible;
        BtnImport.Visibility = Visibility.Collapsed;
        DetectedPanel.Visibility = Visibility.Collapsed;
        _detected.Clear();
        _allVersionsMap.Clear();
        AddPanel.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => ClosePanel();

    private void ClosePanel()
    {
        AddPanel.Visibility = Visibility.Collapsed;
        _editingRepo = null;
        _detected.Clear();
        _allVersionsMap.Clear();
    }

    // ── Fetch ─────────────────────────────────────────────────────────────────

    private async void OnFetch(object sender, RoutedEventArgs e)
    {
        var repo = NormalizeRepo(RepoInput.Text.Trim());
        if (string.IsNullOrEmpty(repo)) { ScanStatus.Text = "Enter a repository."; return; }

        ScanStatus.Text = "Scanning…";
        ScanStatus.Foreground = (Brush)FindResource("TextMuted");
        BtnFetch.IsEnabled = false;

        try
        {
            bool isFolder  = TypeFolder.IsChecked == true;
            string sourceType = isFolder ? "folder" : "release";
            string folder  = FolderInput.Text.Trim();
            string filter  = FilterInput.Text.Trim();

            // Persist source entry
            if (_editingRepo == null)
            {
                var sources = Storage.LoadSources();
                if (!sources.Any(s => s.Repo == repo))
                    sources.Add(new SourceEntry { Repo = repo, Filter = filter, DisplayName = DisplayInput.Text.Trim(), SourceType = sourceType, Folder = folder });
                Storage.SaveSources(sources);
            }

            // Fetch raw assets
            List<ReleaseAsset> raw;
            if (isFolder)
            {
                var files = await _gh.ScanRepoFilesAsync(repo, folder, filter);
                raw = files.Select(f => new ReleaseAsset(
                    Path.GetFileName(f.Path), f.DownloadUrl, "folder", "", 0, 0, "", false)).ToList();
            }
            else
            {
                raw = await _gh.GetReleasesAsync(repo, filter);
            }

            // Deduplicate: group by asset name (for zip: use base name), versions latest-first
            _allVersionsMap = raw
                .GroupBy(a => a.IsZip ? Path.GetFileNameWithoutExtension(a.Name) : a.Name)
                .ToDictionary(g => g.Key, g => g.ToList());

            _detected.Clear();
            foreach (var (key, versions) in _allVersionsMap)
            {
                var latest = versions[0];
                var ext = Path.GetExtension(key).TrimStart('.').ToLower();
                var extUpper = string.IsNullOrEmpty(ext) ? "BIN" : ext.ToUpper();
                var displayName = latest.IsZip ? key + " (zip)" : key;
                Brush badgeColor = ext switch
                {
                    "lua" => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                    "elf" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    _     => new SolidColorBrush(Color.FromRgb(107, 114, 128))
                };

                _detected.Add(new DetectedRow
                {
                    Name         = displayName,
                    Version      = latest.Tag == "folder" ? "folder" : latest.Tag,
                    VersionCount = versions.Count > 1 ? $"{versions.Count} versions" : "",
                    IsChecked    = true,
                    BadgeColor   = badgeColor,
                    ExtUpper     = extUpper,
                });
            }

            ScanStatus.Text = $"Found {_detected.Count} payload(s)";
            ScanStatus.Foreground = (Brush)FindResource("Success");
            DetectedPanel.Visibility = _detected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DetectedCount.Text       = $"{_detected.Count} found";
            BtnImport.Visibility = _detected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnImport.IsEnabled  = _detected.Count > 0;
            LogBus.Log($"Source {repo}: found {_detected.Count} payload(s)", LogLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            ScanStatus.Text = "Error: " + ex.Message;
            ScanStatus.Foreground = (Brush)FindResource("Danger");
            LogBus.Log("Fetch source error: " + ex.Message, LogLevel.Error);
        }
        BtnFetch.IsEnabled = true;
    }

    // ── Save (edit mode) ──────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_editingRepo == null) return;
        var sources = Storage.LoadSources();
        var src = sources.FirstOrDefault(s => s.Repo == _editingRepo);
        if (src != null)
        {
            src.Filter      = FilterInput.Text.Trim();
            src.DisplayName = DisplayInput.Text.Trim();
            src.SourceType  = TypeFolder.IsChecked == true ? "folder" : "release";
            src.Folder      = FolderInput.Text.Trim();
            Storage.SaveSources(sources);
            LogBus.Log($"Source {_editingRepo} saved", LogLevel.Success);
        }
        ClosePanel();
        Refresh();
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var toImport = _detected.Where(d => d.IsChecked).ToList();
        if (toImport.Count == 0) return;
        BtnImport.IsEnabled = false;
        int imported = 0;
        var meta = Storage.LoadPayloadMeta();

        foreach (var row in toImport)
        {
            // Strip " (zip)" suffix to find versions map key
            var key = row.Name.EndsWith(" (zip)") ? row.Name[..^6] : row.Name;
            if (!_allVersionsMap.TryGetValue(key, out var versions) || versions.Count == 0) continue;

            var latest = versions[0];
            try
            {
                var (data, realName) = await _gh.DownloadPayloadAsync(latest.DownloadUrl, latest.Name);
                if (data == null) continue;
                await File.WriteAllBytesAsync(Path.Combine(AppPaths.PayloadsDir, realName), data);

                // Store all available versions so the version picker works
                var allVers = versions
                    .Where(v => v.Tag != "folder")
                    .Select(v => new VersionEntry
                    {
                        Tag         = v.Tag,
                        DownloadUrl = v.DownloadUrl,
                        PublishedAt = v.PublishedAt,
                        AssetSize   = v.Size,
                    }).ToList();

                meta[realName] = new PayloadMeta
                {
                    Repo        = NormalizeRepo(RepoInput.Text),
                    Version     = latest.Tag == "folder" ? "" : latest.Tag,
                    DownloadUrl = latest.DownloadUrl,
                    PublishedAt = latest.PublishedAt,
                    AssetSize   = latest.Size,
                    ReleaseId   = latest.ReleaseId,
                    AllVersions = allVers,
                };
                imported++;
            }
            catch (Exception ex) { LogBus.Log($"Import {latest.Name} failed: {ex.Message}", LogLevel.Error); }
        }

        Storage.SavePayloadMeta(meta);
        LogBus.Log($"Imported {imported} payload(s)", LogLevel.Success);
        ClosePanel();
        Refresh();
        BtnImport.IsEnabled = true;
    }

    // ── Check updates ─────────────────────────────────────────────────────────

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        UpdateStatus.Visibility = Visibility.Visible;
        UpdateStatus.Foreground = (Brush)FindResource("TextMuted");
        UpdateStatus.Text = "Checking updates…";
        try
        {
            var sources = Storage.LoadSources();
            var meta    = Storage.LoadPayloadMeta();
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
    public string Repo     { get; set; } = "";
    public string Display  { get; set; } = "";
    public string SubText  { get; set; } = "";
    public string TypeIcon { get; set; } = "📦";
}

public class DetectedRow : INotifyPropertyChanged
{
    private bool _isChecked = true;
    public string Name         { get; set; } = "";
    public string Version      { get; set; } = "";
    public string VersionCount { get; set; } = "";
    public string ExtUpper     { get; set; } = "";
    public Brush  BadgeColor   { get; set; } = Brushes.Gray;
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
