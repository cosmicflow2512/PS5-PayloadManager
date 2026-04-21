using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class LogsView : UserControl
{
    private readonly ObservableCollection<LogRow> _rows = [];
    private const int MaxRows = 500;

    public LogsView()
    {
        InitializeComponent();
        LogList.ItemsSource = _rows;
        LogBus.OnLog += OnLogEntry;
    }

    private void OnLogEntry(LogEvent e)
    {
        Dispatcher.Invoke(() =>
        {
            var color = e.Level switch
            {
                LogLevel.Success => Brushes.LightGreen,
                LogLevel.Error   => Brushes.Salmon,
                LogLevel.Warn    => Brushes.Goldenrod,
                _                => Brushes.LightGray,
            };
            _rows.Add(new LogRow
            {
                Text  = $"[{e.When:HH:mm:ss}]  {e.Message}",
                Color = color,
            });
            if (_rows.Count > MaxRows) _rows.RemoveAt(0);
            Scroller.ScrollToBottom();
        });
    }

    private void OnClear(object sender, RoutedEventArgs e) => _rows.Clear();

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter   = "Text|*.txt",
            FileName = $"ps5_log_{DateTime.Now:yyyyMMddHHmmss}.txt"
        };
        if (dlg.ShowDialog() != true) return;
        var lines = _rows.Select(r => r.Text);
        File.WriteAllLines(dlg.FileName, lines);
        LogBus.Log("Log exported.", LogLevel.Success);
    }
}

public class LogRow
{
    public string Text  { get; set; } = "";
    public Brush  Color { get; set; } = Brushes.LightGray;
}
