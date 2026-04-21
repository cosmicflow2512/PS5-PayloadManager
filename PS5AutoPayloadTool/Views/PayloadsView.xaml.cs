using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class PayloadsView : UserControl
{
    private readonly ObservableCollection<PayloadVM> _rows = [];
    private string _filter = "all";
    private string _search = "";
    private List<UpdateResult> _updates = [];
    private readonly GitHubClient _gh = new();
    private readonly HashSet<string> _selected = [];

    public PayloadsView()
    {
        InitializeComponent();
        PayloadsList.ItemsSource = _rows;
    }

    public void Refresh(List<UpdateResult>? updates = null)
    {
        if (updates != null) _updates = updates;
        var favs = Storage.LoadPayloadFavs();
        var all  = Storage.ListPayloads();
        _rows.Clear();

        foreach (var p in all)
        {
            var ext  = Path.GetExtension(p.Name).TrimStart('.').ToLower();
            var isFav = favs.Contains(p.Name);
            if (_filter == "fav" && !isFav) continue;
            if (_filter == "elf" && ext != "elf") continue;
            if (_filter == "lua" && ext != "lua") continue;
            if (!string.IsNullOrEmpty(_search) && !p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)) continue;

            var upd = _updates.FirstOrDefault(u => u.Filename == p.Name);
            var badgeColor = ext switch
            {
                "lua" => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                "elf" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                _     => new SolidColorBrush(Color.FromRgb(107, 114, 128))
            };

            bool hasRepo     = !string.IsNullOrEmpty(p.Repo);
            bool hasVersions = hasRepo && p.AllVersions.Count > 1;
            string verText   = string.IsNullOrEmpty(p.Version) ? "Latest" : p.Version;

            _rows.Add(new PayloadVM
            {
                Name         = p.Name,
                ExtUpper     = ext.ToUpper(),
                BadgeColor   = badgeColor,
                SizeText     = FormatBytes(p.Size) + " • " + p.Modified.ToLocalTime().ToString("MMM dd HH:mm"),
                RepoText     = p.Repo,
                HasRepo      = hasRepo,
                HasVersions  = hasVersions,
                VersionTags  = hasVersions ? p.AllVersions.Select(v => v.Tag).ToList() : [],
                VersionText  = verText,
                SelectedVersion      = p.Version,
                SingleVersionVisibility = hasRepo && !hasVersions ? Visibility.Visible : Visibility.Collapsed,
                LocalVisibility         = hasRepo ? Visibility.Collapsed : Visibility.Visible,
                IsSelected   = _selected.Contains(p.Name),
                FavStar      = isFav ? "⭐" : "☆",
                FavColor     = isFav ? Brushes.Gold : (Brush)Application.Current.FindResource("TextMuted"),
                UpdateLabel  = upd != null ? upd.NewVersion : "",
                UpdateBadgeVisibility = upd != null ? Visibility.Visible : Visibility.Collapsed,
                AllVersions  = p.AllVersions,
                DownloadUrl  = p.DownloadUrl,
                UpdateResult = upd,
            });
        }

        CountBadge.Text = $"{_rows.Count} payload{(_rows.Count != 1 ? "s" : "")}";
        UpdateBulkBar();

        if (_updates.Count > 0)
        {
            UpdateAllBar.Visibility = Visibility.Visible;
            UpdateAllText.Text = $"{_updates.Count} update(s) available";
        }
        else
        {
            UpdateAllBar.Visibility = Visibility.Collapsed;
        }
    }

    // ── Check Updates ─────────────────────────────────────────────────────────

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdates.IsEnabled = false;
        ShowUpdateStatus("Checking for updates…", "TextMuted");
        try
        {
            var sources = Storage.LoadSources();
            if (sources.Count == 0)
            {
                ShowUpdateStatus("No sources configured. Add sources first.", "Warn");
                return;
            }
            var meta    = Storage.LoadPayloadMeta();
            var updates = await _gh.CheckUpdatesAsync(sources, meta);
            _updates = updates;
            Refresh();
            ShowUpdateStatus(
                updates.Count == 0 ? "✓ All payloads are up to date" : $"{updates.Count} update(s) available",
                updates.Count == 0 ? "Success" : "Warn");
        }
        catch (Exception ex)
        {
            ShowUpdateStatus("Error: " + ex.Message, "Danger");
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private void ShowUpdateStatus(string msg, string colorKey)
    {
        UpdateStatusText.Text = msg;
        UpdateStatusText.Foreground = (Brush)Application.Current.FindResource(colorKey);
        UpdateStatusText.Visibility = Visibility.Visible;
    }

    // ── Update one payload ────────────────────────────────────────────────────

    private async void OnUpdateOne(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        var upd = _updates.FirstOrDefault(u => u.Filename == name);
        if (upd == null) return;
        b.IsEnabled = false;
        LogBus.Log($"Updating {name} → {upd.NewVersion}…", LogLevel.Info);
        try
        {
            var (data, realName) = await _gh.DownloadPayloadAsync(upd.DownloadUrl, upd.Filename);
            if (data != null)
            {
                await File.WriteAllBytesAsync(Path.Combine(AppPaths.PayloadsDir, realName), data);
                var meta = Storage.LoadPayloadMeta();
                if (meta.TryGetValue(realName, out var mx))
                {
                    mx.Version     = upd.NewVersion;
                    mx.DownloadUrl = upd.DownloadUrl;
                    mx.PublishedAt = upd.PublishedAt;
                    mx.AssetSize   = upd.AssetSize;
                }
                Storage.SavePayloadMeta(meta);
                _updates.RemoveAll(u => u.Filename == name);
                LogBus.Log($"Updated {name} → {upd.NewVersion}", LogLevel.Success);
                Refresh();
            }
        }
        catch (Exception ex) { LogBus.Log($"Update {name} failed: {ex.Message}", LogLevel.Error); }
        b.IsEnabled = true;
    }

    // ── Update All ────────────────────────────────────────────────────────────

    private async void OnUpdateAll(object sender, RoutedEventArgs e)
    {
        var meta = Storage.LoadPayloadMeta();
        int done = 0;
        foreach (var u in _updates.ToList())
        {
            try
            {
                var (data, realName) = await _gh.DownloadPayloadAsync(u.DownloadUrl, u.Filename);
                if (data == null) continue;
                await File.WriteAllBytesAsync(Path.Combine(AppPaths.PayloadsDir, realName), data);
                if (meta.TryGetValue(realName, out var mx))
                {
                    mx.Version     = u.NewVersion;
                    mx.DownloadUrl = u.DownloadUrl;
                    mx.PublishedAt = u.PublishedAt;
                    mx.AssetSize   = u.AssetSize;
                }
                done++;
            }
            catch (Exception ex) { LogBus.Log($"Update {u.Filename} failed: {ex.Message}", LogLevel.Error); }
        }
        Storage.SavePayloadMeta(meta);
        _updates.Clear();
        LogBus.Log($"Updated {done} payload(s)", LogLevel.Success);
        ShowUpdateStatus($"Updated {done} payload(s)", "Success");
        Refresh();
    }

    // ── Version switch ────────────────────────────────────────────────────────

    private async void OnVersionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.Tag is not string name) return;
        if (cb.SelectedItem is not string tag) return;
        var vm = _rows.FirstOrDefault(r => r.Name == name);
        if (vm == null) return;
        var current = Storage.LoadPayloadMeta().TryGetValue(name, out var cur) ? cur.Version : "";
        if (tag == current) return;
        var ver = vm.AllVersions.FirstOrDefault(v => v.Tag == tag);
        if (ver == null) return;
        LogBus.Log($"Switching {name} to {tag}…", LogLevel.Info);
        cb.IsEnabled = false;
        try
        {
            var (data, realName) = await _gh.DownloadPayloadAsync(ver.DownloadUrl, name);
            if (data != null)
            {
                await File.WriteAllBytesAsync(Path.Combine(AppPaths.PayloadsDir, realName), data);
                var meta = Storage.LoadPayloadMeta();
                if (meta.TryGetValue(name, out var mx)) { mx.Version = tag; mx.DownloadUrl = ver.DownloadUrl; Storage.SavePayloadMeta(meta); }
                LogBus.Log($"Switched {name} → {tag}", LogLevel.Success);
                Refresh();
            }
        }
        catch (Exception ex) { LogBus.Log($"Switch failed: {ex.Message}", LogLevel.Error); }
        cb.IsEnabled = true;
    }

    // ── Misc handlers ─────────────────────────────────────────────────────────

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        _updates.Clear();
        UpdateStatusText.Visibility = Visibility.Collapsed;
        Refresh();
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { _search = SearchBox.Text; Refresh(); }
    private void OnFilter(object sender, RoutedEventArgs e) { if (sender is RadioButton rb) { _filter = rb.Tag?.ToString() ?? "all"; Refresh(); } }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        bool check = SelectAllCb.IsChecked == true;
        _selected.Clear();
        if (check) foreach (var r in _rows) _selected.Add(r.Name);
        foreach (var r in _rows) r.IsSelected = _selected.Contains(r.Name);
        UpdateBulkBar();
    }

    private void OnItemSelect(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.Tag is string name)
        {
            if (cb.IsChecked == true) _selected.Add(name); else _selected.Remove(name);
            UpdateBulkBar();
        }
    }

    private void UpdateBulkBar()
    {
        BulkBar.Visibility = _selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BulkCount.Text = $"{_selected.Count} selected";
    }

    private void OnToggleFav(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        var favs = Storage.LoadPayloadFavs();
        if (favs.Contains(name)) favs.Remove(name); else favs.Add(name);
        Storage.SavePayloadFavs(favs);
        Refresh();
    }

    private void OnUpload(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Payloads (*.elf;*.lua;*.bin)|*.elf;*.lua;*.bin", Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames)
            File.Copy(f, Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(f)), true);
        LogBus.Log($"Uploaded {dlg.FileNames.Length} payload(s)", LogLevel.Success);
        Refresh();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        var ip = Storage.LoadPs5Ip();
        if (string.IsNullOrEmpty(ip)) { MessageBox.Show("Set PS5 IP in Settings first."); return; }
        var port = PayloadSender.ResolvePort(name);
        LogBus.Log($"Sending {name} → {ip}:{port}", LogLevel.Info);
        b.IsEnabled = false;
        var (ok, msg, _) = await PayloadSender.SendAsync(ip, port, name);
        LogBus.Log(msg, ok ? LogLevel.Success : LogLevel.Error);
        b.IsEnabled = true;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string name) return;
        if (MessageBox.Show($"Delete {name}?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var path = Path.Combine(AppPaths.PayloadsDir, name);
        if (File.Exists(path)) File.Delete(path);
        var meta = Storage.LoadPayloadMeta(); meta.Remove(name); Storage.SavePayloadMeta(meta);
        _selected.Remove(name);
        _updates.RemoveAll(u => u.Filename == name);
        LogBus.Log($"Deleted {name}", LogLevel.Info);
        Refresh();
    }

    private async void OnBulkDelete(object sender, RoutedEventArgs e)
    {
        if (_selected.Count == 0) return;
        if (MessageBox.Show($"Delete {_selected.Count} payload(s)?", "Confirm", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var meta = Storage.LoadPayloadMeta();
        var names = _selected.ToList();
        foreach (var name in names)
        {
            var path = Path.Combine(AppPaths.PayloadsDir, name);
            if (File.Exists(path)) File.Delete(path);
            meta.Remove(name);
            _updates.RemoveAll(u => u.Filename == name);
        }
        Storage.SavePayloadMeta(meta);
        _selected.Clear();
        LogBus.Log($"Deleted {names.Count} payload(s)", LogLevel.Info);
        Refresh();
    }

    private static string FormatBytes(long b) =>
        b < 1024 ? $"{b} B" : b < 1_048_576 ? $"{b / 1024.0:F1} KB" : $"{b / 1_048_576.0:F2} MB";
}

public class PayloadVM : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name       { get; set; } = "";
    public string ExtUpper   { get; set; } = "";
    public Brush  BadgeColor { get; set; } = Brushes.Gray;
    public string SizeText   { get; set; } = "";
    public string RepoText   { get; set; } = "";
    public bool   HasRepo    { get; set; }
    public bool   HasVersions { get; set; }
    public List<string> VersionTags { get; set; } = [];
    public string VersionText { get; set; } = "";
    public string SelectedVersion { get; set; } = "";
    public Visibility SingleVersionVisibility { get; set; } = Visibility.Collapsed;
    public Visibility LocalVisibility         { get; set; } = Visibility.Collapsed;
    public string FavStar  { get; set; } = "☆";
    public Brush  FavColor { get; set; } = Brushes.Gray;
    public string UpdateLabel { get; set; } = "";
    public Visibility UpdateBadgeVisibility { get; set; } = Visibility.Collapsed;
    public List<VersionEntry> AllVersions { get; set; } = [];
    public string DownloadUrl { get; set; } = "";
    public UpdateResult? UpdateResult { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
