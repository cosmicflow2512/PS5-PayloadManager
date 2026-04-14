using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class SourcesPage : UserControl
{
    public SourcesPage()
    {
        InitializeComponent();
    }

    // Called by MainWindow when this page becomes active
    public void Refresh()
    {
        PopulateList();
    }

    // ── List population ──────────────────────────────────────────────────────

    private void PopulateList()
    {
        SourcesList.ItemsSource = null;
        SourcesList.ItemsSource = MainWindow.Config.Sources;

        EmptyState.Visibility = MainWindow.Config.Sources.Count == 0
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
        var owner = TxtOwner.Text.Trim();
        var repo  = TxtRepo.Text.Trim();

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            TxtStatus.Text = "Owner and Repo are required.";
            return;
        }

        var source = new PayloadSource
        {
            Owner      = owner,
            Repo       = repo,
            Type       = CmbType.SelectedIndex == 1 ? SourceType.GitHubFolder : SourceType.GitHubRelease,
            Filter     = TxtFilter.Text.Trim(),
            FolderPath = TxtFolder.Text.Trim()
        };

        TxtStatus.Text = $"Scanning {source.DisplayName}...";

        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => TxtStatus.Text = msg));

        List<PayloadItem> found;
        try
        {
            found = await MainWindow.PayloadMgr.ScanSourceAsync(
                source, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
            return;
        }

        // Avoid duplicate sources (same owner/repo/type)
        bool exists = MainWindow.Config.Sources.Any(s =>
            s.Owner == source.Owner &&
            s.Repo  == source.Repo  &&
            s.Type  == source.Type);

        if (!exists)
            MainWindow.Config.Sources.Add(source);

        // Merge discovered payloads (avoid duplicates by name)
        foreach (var item in found)
        {
            bool dup = MainWindow.Config.Payloads.Any(p =>
                p.Name     == item.Name &&
                p.SourceId == item.SourceId);
            if (!dup)
                MainWindow.Config.Payloads.Add(item);
        }

        MainWindow.SaveConfig();

        TxtStatus.Text     = $"Found {found.Count} payload(s). Source added.";
        AddForm.Visibility = Visibility.Collapsed;
        PopulateList();
    }

    // ── Rescan ───────────────────────────────────────────────────────────────

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        TxtStatus.Text = $"Rescanning {source.DisplayName}...";

        var progress = new Progress<string>(msg =>
            Dispatcher.Invoke(() => TxtStatus.Text = msg));

        List<PayloadItem> found;
        try
        {
            found = await MainWindow.PayloadMgr.ScanSourceAsync(
                source, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
            return;
        }

        // Merge new payloads
        foreach (var item in found)
        {
            bool dup = MainWindow.Config.Payloads.Any(p =>
                p.Name     == item.Name &&
                p.SourceId == item.SourceId);
            if (!dup)
                MainWindow.Config.Payloads.Add(item);
        }

        MainWindow.SaveConfig();
        TxtStatus.Text = $"Rescan complete — {found.Count} payload(s) found.";
    }

    // ── Remove source ────────────────────────────────────────────────────────

    private void BtnRemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        var result = MessageBox.Show(
            $"Remove source \"{source.DisplayName}\" and its payloads from the list?",
            "Confirm Remove",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        MainWindow.Config.Sources.RemoveAll(s => s.Id == id);
        MainWindow.Config.Payloads.RemoveAll(p => p.SourceId == id);
        MainWindow.SaveConfig();

        TxtStatus.Text = $"Removed {source.DisplayName}.";
        PopulateList();
    }
}
