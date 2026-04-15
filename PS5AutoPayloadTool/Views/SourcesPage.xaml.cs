using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class SourcesPage : UserControl
{
    private readonly ObservableCollection<SourceConfig> _sources = new();
    private string? _editingId;

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

    private static string? ParseRepoUrl(string input)
    {
        input = input.Trim().TrimEnd('/');
        var m = Regex.Match(input,
            @"(?:https?://)?(?:www\.)?github\.com/([^/\s]+)/([^/\s]+)",
            RegexOptions.IgnoreCase);
        if (m.Success)
            return $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}";
        m = Regex.Match(input, @"^([^/\s]+)/([^/\s]+)$");
        if (m.Success)
            return $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}";
        return null;
    }

    private void BtnToggleAdd_Click(object sender, RoutedEventArgs e)
    {
        if (AddForm.Visibility == Visibility.Visible) CloseForm();
        else OpenForm(null);
    }

    private void OpenForm(SourceConfig? existing)
    {
        _editingId = existing?.Id;
        FormTitle.Text = existing == null ? "Add New Source" : "Edit Source";
        BtnScanAdd.Content = existing == null ? "Scan & Save" : "Rescan & Save";
        TxtRepoUrl.Text = existing?.Url ?? "";
        TxtFilter.Text  = existing?.Filter ?? "";
        TxtFolder.Text  = existing?.FolderPath ?? "";
        CmbType.SelectedIndex = existing?.Type == "folder" ? 1 : 0;
        FolderPathPanel.Visibility = CmbType.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        AddForm.Visibility = Visibility.Visible;
        TxtStatus.Text = "";
    }

    private void CloseForm()
    {
        _editingId = null;
        AddForm.Visibility = Visibility.Collapsed;
        TxtStatus.Text = "";
    }

    private void BtnCancelAdd_Click(object sender, RoutedEventArgs e) => CloseForm();

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderPathPanel == null) return;
        FolderPathPanel.Visibility = CmbType.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void BtnScanAdd_Click(object sender, RoutedEventArgs e)
    {
        var url = ParseRepoUrl(TxtRepoUrl.Text);
        if (url == null)
        {
            TxtStatus.Text = "Invalid repo. Use: owner/repo  or  https://github.com/owner/repo";
            return;
        }

        var source = new SourceConfig
        {
            Url        = url,
            Type       = CmbType.SelectedIndex == 1 ? "folder" : "release",
            Filter     = TxtFilter.Text.Trim(),
            FolderPath = TxtFolder.Text.Trim()
        };

        TxtStatus.Text = $"Scanning {source.DisplayName}...";
        BtnScanAdd.IsEnabled = false;
        var progress = new Progress<string>(msg => Dispatcher.Invoke(() => TxtStatus.Text = msg));

        try
        {
            var found = await MainWindow.PayloadMgr.ScanSourceAsync(source, progress, CancellationToken.None);

            if (_editingId != null)
                MainWindow.Config.Sources.RemoveAll(s => s.Id == _editingId);
            if (!MainWindow.Config.Sources.Any(s => s.Url == source.Url))
                MainWindow.Config.Sources.Add(source);

            foreach (var (name, _, version, size) in found)
            {
                if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                    MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                        { SourceUrl = source.Url, Versions = new() { version }, Version = version, Size = size };
                else if (!MainWindow.Config.PayloadMeta[name].Versions.Contains(version))
                    MainWindow.Config.PayloadMeta[name].Versions.Add(version);
            }

            MainWindow.SaveConfig();
            PopulateList();
            CloseForm();
            TxtStatus.Text = $"Found {found.Count} payload(s). Source saved.";
        }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
        finally { BtnScanAdd.IsEnabled = true; }
    }

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source == null) return;

        TxtStatus.Text = $"Rescanning {source.DisplayName}...";
        var progress = new Progress<string>(msg => Dispatcher.Invoke(() => TxtStatus.Text = msg));
        try
        {
            var found = await MainWindow.PayloadMgr.ScanSourceAsync(source, progress, CancellationToken.None);
            foreach (var (name, _, version, size) in found)
            {
                if (!MainWindow.Config.PayloadMeta.ContainsKey(name))
                    MainWindow.Config.PayloadMeta[name] = new PayloadMeta
                        { SourceUrl = source.Url, Versions = new() { version }, Version = version, Size = size };
                else if (!MainWindow.Config.PayloadMeta[name].Versions.Contains(version))
                    MainWindow.Config.PayloadMeta[name].Versions.Add(version);
            }
            MainWindow.SaveConfig();
            TxtStatus.Text = $"Rescan complete — {found.Count} payload(s).";
        }
        catch (Exception ex) { TxtStatus.Text = $"Error: {ex.Message}"; }
    }

    private void BtnEditSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var source = MainWindow.Config.Sources.FirstOrDefault(s => s.Id == id);
        if (source != null) OpenForm(source);
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
