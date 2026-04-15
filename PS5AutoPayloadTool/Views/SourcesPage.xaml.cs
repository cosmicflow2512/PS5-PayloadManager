using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class SourcesPage : UserControl
{
    private readonly ObservableCollection<SourceConfig> _sources = new();

    public SourcesPage()
    {
        InitializeComponent();
        SourcesList.ItemsSource = _sources;
    }

    // Called by MainWindow when this page becomes active
    public void Refresh() => PopulateList();

    // ── List population ──────────────────────────────────────────────────────

    private void PopulateList()
    {
        _sources.Clear();
        foreach (var s in MainWindow.Config.Sources)
            _sources.Add(s);

        EmptyState.Visibility = _sources.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── Toggle add form ──────────────────────────────────────────────────────

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = AddForm.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (AddForm.Visibility == Visibility.Visible)
        {
            TxtOwner.Text  = "";
            TxtRepo.Text   = "";
            TxtFilter.Text = "";
            TxtFolder.Text = "";
            CmbType.SelectedIndex = 0;
            FolderPathPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e)
    {
        AddForm.Visibility = Visibility.Collapsed;
        TxtStatus.Text     = "";
    }

    // ── ComboBox changed: show/hide folder path field ────────────────────────

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderPathPanel == null) return;
        FolderPathPanel.Visibility = CmbType.SelectedIndex == 1
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── Scan & Add ───────────────────────────────────────────────────────────

    private async void BtnScanAdd_Click(object sender, RoutedEventArgs e)
    {
        var owner  = TxtOwner.Text.Trim();
        var repo   = TxtRepo.Text.Trim();
        var type   = (CmbType.SelectedIndex == 1) ? "folder" : "release";
        var filter = TxtFilter.Text.Trim();
        var folder = TxtFolder.Text.Trim();

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            TxtStatus.Text = "Owner and Repo are required.";
            return;
        }

        var source = new SourceConfig
        {
            Url        = $"https://github.com/{owner}/{repo}",
            Type       = type,
            Filter     = filter,
            FolderPath = folder
        };

        TxtStatus.Text = $"Scanning {source.DisplayName}...";
        BtnScanAdd.IsEnabled = false;

        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => TxtStatus.Text = msg));

        try
        {
            var found = await MainWindow.PayloadMgr.ScanSourceAsync(
                source, progress, CancellationToken.None);

            // Add source if not already present (match by URL)
            if (!MainWindow.Config.Sources.Any(s => s.Url == source.Url))
                MainWindow.Config.Sources.Add(source);

            // Add/update payload_meta entries
            foreach (var (name, url, version, size) in found)
            {
                if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                {
                    MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                    {
                        SourceUrl = source.Url,
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

            MainWindow.SaveConfig();
            PopulateList();
            AddForm.Visibility = Visibility.Collapsed;
            TxtStatus.Text = $"Found {found.Count} payload(s). Source added.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnScanAdd.IsEnabled = true;
        }
    }

    // ── Rescan ───────────────────────────────────────────────────────────────

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        // Tag is bound to Id which equals Url in SourceConfig
        if (sender is not Button btn || btn.Tag is not string id) return;

        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        TxtStatus.Text = $"Rescanning {source.DisplayName}...";

        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => TxtStatus.Text = msg));

        try
        {
            var found = await MainWindow.PayloadMgr.ScanSourceAsync(
                source, progress, CancellationToken.None);

            foreach (var (name, url, version, size) in found)
            {
                if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                {
                    MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                    {
                        SourceUrl = source.Url,
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

            MainWindow.SaveConfig();
            TxtStatus.Text = $"Rescan complete — {found.Count} payload(s) found.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── Remove source ────────────────────────────────────────────────────────

    private void BtnRemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        var result = MessageBox.Show(
            $"Remove source \"{source.DisplayName}\"?",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        MainWindow.Config.Sources.RemoveAll(s => s.Id == id);
        MainWindow.SaveConfig();

        TxtStatus.Text = $"Removed {source.DisplayName}.";
        PopulateList();
    }
}
