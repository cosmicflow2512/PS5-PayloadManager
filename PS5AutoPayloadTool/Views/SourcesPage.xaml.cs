using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Sources;

namespace PS5AutoPayloadTool.Views;

public partial class SourcesPage : UserControl
{
    private readonly ObservableCollection<SourceConfig> _sources = new();
    private string? _editingId;
    private string? _scannedUrl;
    private bool    _hasReleases;

    public SourcesPage()
    {
        InitializeComponent();
        SourcesList.ItemsSource = _sources;
    }

    public void Refresh() => PopulateList();

    private void PopulateList()
    {
        _sources.Clear();
        foreach (var s in MainWindow.Config.Sources)
            _sources.Add(s);
        EmptyState.Visibility = _sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Form navigation ──────────────────────────────────────────────────────

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (ScanPanel.Visibility == Visibility.Visible
         || SelectPanel.Visibility == Visibility.Visible)
            CloseForm();
        else
            OpenScanPanel(null);
    }

    private void OpenScanPanel(SourceConfig? existing)
    {
        _editingId  = existing?.Id;
        _scannedUrl = null;
        FormTitle.Text  = existing == null ? "Add New Source" : "Edit Source";
        TxtRepoUrl.Text = existing?.Url ?? "";

        ScanPanel.Visibility   = Visibility.Visible;
        SelectPanel.Visibility = Visibility.Collapsed;
        TxtStatus.Text = "";
    }

    private void CloseForm()
    {
        _editingId  = null;
        _scannedUrl = null;
        ScanPanel.Visibility   = Visibility.Collapsed;
        SelectPanel.Visibility = Visibility.Collapsed;
        TxtStatus.Text = "";
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e) => CloseForm();

    private void BtnBackToScan_Click(object sender, RoutedEventArgs e)
    {
        SelectPanel.Visibility = Visibility.Collapsed;
        ScanPanel.Visibility   = Visibility.Visible;
    }

    // ── Step 1: Scan ─────────────────────────────────────────────────────────

    private async void BtnScanRepo_Click(object sender, RoutedEventArgs e)
    {
        var url = SourceService.ParseRepoUrl(TxtRepoUrl.Text);
        if (url == null)
        {
            TxtStatus.Text = "Invalid repo. Use: owner/repo  or  https://github.com/owner/repo";
            return;
        }

        _scannedUrl = url;
        var tmp = new SourceConfig { Url = url };
        TxtStatus.Text        = $"Scanning {tmp.DisplayName}…";
        BtnScanRepo.IsEnabled = false;

        try
        {
            var info = await MainWindow.SourceSvc.GetRepoDirInfoAsync(tmp);
            _hasReleases = info.HasReleases;

            CmbFolder.ItemsSource   = info.FolderItems;
            CmbFolder.SelectedIndex = 0;

            TxtReleasesInfo.Text       = _hasReleases ? "Payload releases found in this repository." : "";
            TxtReleasesInfo.Visibility  = _hasReleases ? Visibility.Visible   : Visibility.Collapsed;
            TxtNoReleasesWarn.Visibility = _hasReleases ? Visibility.Collapsed : Visibility.Visible;

            RbReleases.IsChecked = _hasReleases;
            RbFolder.IsChecked   = !_hasReleases;

            // Pre-fill if editing an existing source
            if (_editingId != null)
            {
                var existing = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == _editingId);
                if (existing != null)
                {
                    TxtFilter.Text = existing.Filter;
                    if (existing.Type == "release")
                    {
                        RbReleases.IsChecked = true;
                    }
                    else
                    {
                        RbFolder.IsChecked = true;
                        var fp = existing.FolderPath;
                        if (string.IsNullOrEmpty(fp))
                            CmbFolder.SelectedItem = "/ (root)";
                        else if (info.FolderItems.Contains(fp))
                            CmbFolder.SelectedItem = fp;
                        else
                        {
                            CmbFolder.SelectedItem = "Custom…";
                            TxtCustomFolder.Text   = fp;
                        }
                    }
                }
            }

            ScanResultTitle.Text   = $"Results for {tmp.DisplayName}";
            ScanPanel.Visibility   = Visibility.Collapsed;
            SelectPanel.Visibility = Visibility.Visible;
            TxtStatus.Text         = $"Scan complete. {info.FolderItems.Count - 2} folder(s) found.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Scan error: {ex.Message}";
        }
        finally
        {
            BtnScanRepo.IsEnabled = true;
        }
    }

    // ── Mode radio toggle ────────────────────────────────────────────────────

    private void RbMode_Checked(object sender, RoutedEventArgs e)
    {
        if (FolderSelectPanel == null) return;
        FolderSelectPanel.Visibility =
            RbFolder?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CmbFolder_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CustomFolderPanel == null) return;
        CustomFolderPanel.Visibility =
            CmbFolder.SelectedItem?.ToString() == "Custom…"
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    // ── Step 2: Save ─────────────────────────────────────────────────────────

    private async void BtnSaveSource_Click(object sender, RoutedEventArgs e)
    {
        if (_scannedUrl == null) return;

        bool   useReleases = RbReleases?.IsChecked == true;
        string folderPath  = "";

        if (!useReleases)
        {
            var sel = CmbFolder.SelectedItem?.ToString() ?? "/ (root)";
            if (sel == "Custom…")
                folderPath = TxtCustomFolder.Text.Trim().TrimStart('/');
            else if (sel != "/ (root)")
                folderPath = sel;
        }

        var source = new SourceConfig
        {
            Url        = _scannedUrl,
            Type       = useReleases ? "release" : "folder",
            Filter     = TxtFilter.Text.Trim(),
            FolderPath = folderPath
        };

        TxtStatus.Text          = $"Scanning {source.DisplayName}…";
        BtnSaveSource.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(msg => Dispatcher.Invoke(() => TxtStatus.Text = msg));
            var (found, error) = await MainWindow.SourceSvc.ScanAndUpdateConfigAsync(
                source, MainWindow.Config, progress);

            if (error != null)
            {
                TxtStatus.Text = $"Error: {error}";
                return;
            }

            // Replace if editing, add if new
            if (_editingId != null)
                MainWindow.Config.Sources.RemoveAll(s => s.Id == _editingId);
            if (!MainWindow.Config.Sources.Any(s => s.Url == source.Url))
                MainWindow.Config.Sources.Add(source);

            MainWindow.SaveConfig();
            PopulateList();
            CloseForm();
            TxtStatus.Text = $"Saved. Found {found} payload(s).";
        }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
        finally { BtnSaveSource.IsEnabled = true; }
    }

    // ── Rescan / Edit / Remove ────────────────────────────────────────────────

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        TxtStatus.Text = $"Rescanning {source.DisplayName}…";
        var progress = new Progress<string>(msg => Dispatcher.Invoke(() => TxtStatus.Text = msg));

        var (found, error) = await MainWindow.SourceSvc.ScanAndUpdateConfigAsync(
            source, MainWindow.Config, progress);

        if (error != null)
            TxtStatus.Text = $"Error: {error}";
        else
        {
            MainWindow.SaveConfig();
            TxtStatus.Text = $"Rescan complete — {found} payload(s).";
        }
    }

    private void BtnEditSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source != null) OpenScanPanel(source);
    }

    private void BtnRemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        if (MessageBox.Show($"Remove \"{source.DisplayName}\"?", "Confirm Remove",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        MainWindow.Config.Sources.RemoveAll(s => s.Id == id);
        MainWindow.SaveConfig();
        TxtStatus.Text = $"Removed {source.DisplayName}.";
        PopulateList();
    }
}
