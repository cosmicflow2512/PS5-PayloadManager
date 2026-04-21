using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class SourcesView : UserControl
{
    private readonly GitHubClient _gh = new();
    public ObservableCollection<SourceRow> Rows { get; } = [];

    public SourcesView()
    {
        InitializeComponent();
        SourcesList.ItemsSource = Rows;
        Refresh();
    }

    public void Refresh()
    {
        Rows.Clear();
        foreach (var s in Storage.LoadSources())
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(s.Filter))     parts.Add(s.Filter);
            if (s.SourceType != "auto")              parts.Add(s.SourceType);
            if (!string.IsNullOrEmpty(s.Folder))     parts.Add(s.Folder);
            Rows.Add(new SourceRow
            {
                Repo    = string.IsNullOrEmpty(s.DisplayName) ? s.Repo : s.DisplayName,
                SubText = parts.Count > 0 ? string.Join(" · ", parts) : "auto",
            });
        }
    }

    private void OnAddSource(object sender, RoutedEventArgs e)
    {
        AddPanel.Visibility = AddPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        PanelTitle.Text = "Add Source";
        RepoInput.Text = ""; DisplayInput.Text = ""; FilterInput.Text = "";
        StatusText.Text = "";
    }

    private void OnCancel(object sender, RoutedEventArgs e) => AddPanel.Visibility = Visibility.Collapsed;

    private async void OnFetch(object sender, RoutedEventArgs e)
    {
        var repo = RepoInput.Text.Trim();
        if (string.IsNullOrEmpty(repo)) { StatusText.Text = "Enter a repository."; return; }

        StatusText.Text = "Scanning …";
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");

        try
        {
            var normalized = NormalizeRepo(repo);
            var sources = Storage.LoadSources();
            if (!sources.Any(s => s.Repo == normalized))
                sources.Add(new SourceEntry
                {
                    Repo = normalized,
                    Filter = FilterInput.Text.Trim(),
                    DisplayName = DisplayInput.Text.Trim(),
                });
            Storage.SaveSources(sources);

            var assets = await _gh.GetReleasesAsync(normalized, FilterInput.Text.Trim());
            StatusText.Text = $"Added. Found {assets.Count} asset(s). Import from Payloads page.";
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Success");

            // Auto-import detected payloads
            foreach (var a in assets)
            {
                try
                {
                    var (data, name) = await _gh.DownloadPayloadAsync(a.DownloadUrl, a.Name);
                    if (data == null) continue;
                    var dest = System.IO.Path.Combine(AppPaths.PayloadsDir, System.IO.Path.GetFileName(name));
                    await System.IO.File.WriteAllBytesAsync(dest, data);
                    var meta = Storage.LoadPayloadMeta();
                    meta[name] = new PayloadMeta
                    {
                        Repo = normalized,
                        Version = a.Tag,
                        DownloadUrl = a.DownloadUrl,
                        PublishedAt = a.PublishedAt,
                        AssetSize = a.Size,
                        ReleaseId = a.ReleaseId,
                    };
                    Storage.SavePayloadMeta(meta);
                }
                catch (Exception ex) { LogBus.Log($"Import {a.Name} failed: {ex.Message}", LogLevel.Error); }
            }

            LogBus.Log($"Source added: {normalized} ({assets.Count} payloads)", LogLevel.Success);
            Refresh();
            AddPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }
    }

    private void OnScan(object sender, RoutedEventArgs e)
    {
        OnCheckUpdates(sender, e);
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        try
        {
            var sources = Storage.LoadSources();
            var meta = Storage.LoadPayloadMeta();
            var updates = await _gh.CheckUpdatesAsync(sources, meta);
            if (updates.Count == 0)
            {
                LogBus.Log("All payloads up to date.", LogLevel.Success);
                MessageBox.Show("All payloads are up to date.", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"{updates.Count} update(s) available. Download now?",
                "Updates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            foreach (var u in updates)
            {
                var (data, name) = await _gh.DownloadPayloadAsync(u.DownloadUrl, u.Filename);
                if (data == null) continue;
                var dest = System.IO.Path.Combine(AppPaths.PayloadsDir, System.IO.Path.GetFileName(name));
                await System.IO.File.WriteAllBytesAsync(dest, data);
                if (meta.TryGetValue(name, out var m)) { m.Version = u.NewVersion; m.DownloadUrl = u.DownloadUrl; }
                LogBus.Log($"Updated {name} to {u.NewVersion}", LogLevel.Success);
            }
            Storage.SavePayloadMeta(meta);
        }
        catch (Exception ex) { LogBus.Log("Update check failed: " + ex.Message, LogLevel.Error); }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string repo) return;
        var sources = Storage.LoadSources();
        sources.RemoveAll(s => s.Repo == repo || s.DisplayName == repo);
        Storage.SaveSources(sources);
        Refresh();
    }

    private static string NormalizeRepo(string repo)
    {
        repo = repo.Trim().TrimEnd('/');
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repo = repo["https://github.com/".Length..];
        else if (repo.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase))    repo = repo["github.com/".Length..];
        return repo;
    }
}

public class SourceRow
{
    public string Repo    { get; set; } = "";
    public string SubText { get; set; } = "";
}
