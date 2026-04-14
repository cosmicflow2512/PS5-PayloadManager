using System.IO;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class PayloadsPage : UserControl
{
    public PayloadsPage()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        PopulateList();
    }

    // ── List ─────────────────────────────────────────────────────────────────

    private void PopulateList()
    {
        // Rebuild the list view — set source to null first to force refresh
        PayloadsList.ItemsSource = null;
        PayloadsList.ItemsSource = MainWindow.Config.Payloads;

        EmptyState.Visibility = MainWindow.Config.Payloads.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Update source display names after data template binds
        // (done via Loaded event on each item's TextBlock — handled differently below)
    }

    // ── Check All Updates ────────────────────────────────────────────────────

    private async void BtnCheckAll_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Checking for updates...";

        var tasks = new List<Task>();
        foreach (var source in MainWindow.Config.Sources)
        {
            var progress = new Progress<string>(msg =>
                Dispatcher.Invoke(() => TxtStatus.Text = msg));

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var items = await MainWindow.PayloadMgr.ScanSourceAsync(
                        source, progress, CancellationToken.None);

                    Dispatcher.Invoke(() =>
                    {
                        foreach (var item in items)
                        {
                            var existing = MainWindow.Config.Payloads
                                .FirstOrDefault(p => p.Name == item.Name && p.SourceId == item.SourceId);
                            if (existing != null)
                            {
                                existing.AvailableVersions = item.AvailableVersions;
                            }
                            else
                            {
                                MainWindow.Config.Payloads.Add(item);
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
        if (sender is not Button btn || btn.Tag is not string id) return;

        var item = MainWindow.Config.Payloads.FirstOrDefault(p => p.Id == id);
        if (item == null) return;

        // Find the version selected in the ComboBox for this item
        // Walk up the visual tree to find the ComboBox named CmbVersion
        var parent = btn.Parent as StackPanel;
        var card   = parent?.Parent as Grid;
        var leftPanel = card?.Children.OfType<StackPanel>().FirstOrDefault();
        var versionStack = leftPanel?.Children.OfType<StackPanel>()
            .FirstOrDefault(sp => sp.Children.OfType<ComboBox>().Any());
        var cmb = versionStack?.Children.OfType<ComboBox>().FirstOrDefault();

        var selectedVersion = cmb?.SelectedItem?.ToString()
                              ?? item.CurrentVersion
                              ?? "latest";

        TxtStatus.Text    = $"Downloading {item.Name} ({selectedVersion})...";
        PrgDownload.Value = 0;
        PrgDownload.Visibility = Visibility.Visible;

        var progress = new Progress<(long Received, long Total)>(p =>
        {
            if (p.Total > 0)
                Dispatcher.Invoke(() =>
                    PrgDownload.Value = (double)p.Received / p.Total * 100.0);
        });

        try
        {
            await MainWindow.PayloadMgr.DownloadPayloadAsync(
                item, selectedVersion, progress, CancellationToken.None);

            MainWindow.SaveConfig();
            TxtStatus.Text    = $"Downloaded {item.Name} successfully.";
            PrgDownload.Value = 100;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            await Task.Delay(1500);
            PrgDownload.Visibility = Visibility.Collapsed;
        }

        PopulateList();
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    private void BtnDeletePayload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;

        var item = MainWindow.Config.Payloads.FirstOrDefault(p => p.Id == id);
        if (item == null) return;

        var result = MessageBox.Show(
            $"Remove \"{item.Name}\" from the payload list?\n\nThe local file will also be deleted if it exists.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // Delete local file if present
        if (!string.IsNullOrEmpty(item.LocalPath) && File.Exists(item.LocalPath))
        {
            try { File.Delete(item.LocalPath); }
            catch { /* ignore */ }
        }

        MainWindow.Config.Payloads.RemoveAll(p => p.Id == id);
        MainWindow.SaveConfig();

        TxtStatus.Text = $"Deleted {item.Name}.";
        PopulateList();
    }
}
