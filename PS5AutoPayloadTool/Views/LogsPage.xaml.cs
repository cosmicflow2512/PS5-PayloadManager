using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PS5AutoPayloadTool.Modules.Core;

namespace PS5AutoPayloadTool.Views;

public partial class LogsPage : UserControl
{
    private readonly ObservableCollection<LogEntry> _displayed = new();
    private LogLevel _minLevel = LogLevel.DEBUG;

    public LogsPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _displayed;
        LogService.EntryAdded += OnEntryAdded;
    }

    public void Refresh()
    {
        _displayed.Clear();
        foreach (var e in LogService.GetAll())
            if (e.Level >= _minLevel)
                _displayed.Add(e);
        ScrollToBottom();
    }

    // ── Log feed ──────────────────────────────────────────────────────────────

    private void OnEntryAdded(LogEntry entry)
    {
        if (entry.Level < _minLevel) return;
        Dispatcher.InvokeAsync(() =>
        {
            _displayed.Add(entry);
            ScrollToBottom();
        });
    }

    private void ScrollToBottom()
    {
        if (LogList == null || _displayed.Count == 0) return;
        LogList.ScrollIntoView(_displayed[^1]);
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _minLevel = (CmbFilter.SelectedIndex) switch
        {
            1 => LogLevel.INFO,
            2 => LogLevel.WARN,
            3 => LogLevel.ERROR,
            _ => LogLevel.DEBUG
        };
        Refresh();
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        LogService.Clear();
        _displayed.Clear();
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = LogService.Export();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title    = "Export Log",
            Filter   = "Text files (*.txt)|*.txt",
            FileName = $"ps5apt-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, LogService.Export());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
