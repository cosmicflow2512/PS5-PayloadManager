using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class BuilderView : UserControl
{
    private readonly ObservableCollection<StepVM> _rows = [];
    private readonly List<FlowStep> _steps = [];
    private Point _dragStart;
    private StepVM? _dragging;
    private bool _isDragging;

    public BuilderView()
    {
        InitializeComponent();
        StepsList.ItemsSource = _rows;
        LogBus.OnStateChange += (state, _) => Dispatcher.Invoke(() =>
        {
            RunButton.Content = state is ExecEngine.Running or ExecEngine.Paused ? "⏹ Stop" : "▶ Run";
        });
        Refresh();
    }

    public void Refresh()
    {
        PayloadCombo.ItemsSource = Storage.ListPayloads().Select(p => p.Name).ToList();
    }

    private void OnAddPayload(object sender, RoutedEventArgs e) => ShowPanel("payload");
    private void OnAddDelay(object sender, RoutedEventArgs e)   => ShowPanel("delay");
    private void OnAddWait(object sender, RoutedEventArgs e)    => ShowPanel("wait");

    private void ShowPanel(string which)
    {
        AddPanel.Visibility      = Visibility.Visible;
        PayloadAddPanel.Visibility = which == "payload" ? Visibility.Visible : Visibility.Collapsed;
        DelayAddPanel.Visibility   = which == "delay"   ? Visibility.Visible : Visibility.Collapsed;
        WaitAddPanel.Visibility    = which == "wait"    ? Visibility.Visible : Visibility.Collapsed;
        if (which == "payload") Refresh();
    }

    private void OnAddPayloadConfirm(object sender, RoutedEventArgs e)
    {
        if (PayloadCombo.SelectedItem is not string name) return;
        _steps.Add(new FlowStep { Type = "payload", Filename = name, AutoPort = AutoloadParser.ResolveDefaultPort(name) });
        AddPanel.Visibility = Visibility.Collapsed;
        Rebuild();
    }

    private void OnDelayPreset(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var ms))
        {
            _steps.Add(new FlowStep { Type = "delay", Ms = ms });
            AddPanel.Visibility = Visibility.Collapsed;
            Rebuild();
        }
    }

    private void OnAddDelayConfirm(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(DelayMs.Text, out var ms) || ms <= 0) { MessageBox.Show("Enter valid ms."); return; }
        _steps.Add(new FlowStep { Type = "delay", Ms = ms });
        DelayMs.Text = "";
        AddPanel.Visibility = Visibility.Collapsed;
        Rebuild();
    }

    private void OnAddWaitConfirm(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(WaitPort.Text, out var port)) { MessageBox.Show("Enter a port."); return; }
        var timeout  = double.TryParse(WaitTimeout.Text,  out var t) ? t : 60.0;
        var interval = int.TryParse(WaitInterval.Text, out var iv) ? iv : 500;
        _steps.Add(new FlowStep { Type = "wait_port", Port = port, Timeout = timeout, IntervalMs = interval });
        AddPanel.Visibility = Visibility.Collapsed;
        Rebuild();
    }

    private void OnStepUp(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i > 0)
        {
            (_steps[i - 1], _steps[i]) = (_steps[i], _steps[i - 1]);
            Rebuild();
        }
    }

    private void OnStepDown(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i < _steps.Count - 1)
        {
            (_steps[i + 1], _steps[i]) = (_steps[i], _steps[i + 1]);
            Rebuild();
        }
    }

    private void OnStepRemove(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i >= 0 && i < _steps.Count)
        {
            _steps.RemoveAt(i);
            Rebuild();
        }
    }

    private void OnStepEdit(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out var i)) return;
        foreach (var r in _rows) r.EditVisible = Visibility.Collapsed;
        if (i < _rows.Count)
        {
            var row = _rows[i];
            row.EditPayloads = Storage.ListPayloads().Select(p => p.Name).ToList();
            row.EditFilename = _steps[i].Filename ?? "";
            row.EditPort     = _steps[i].PortOverride?.ToString() ?? "";
            row.EditVisible  = Visibility.Visible;
        }
    }

    private void OnStepEditConfirm(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || !int.TryParse(b.Tag?.ToString(), out var i)) return;
        if (i >= _steps.Count || i >= _rows.Count) return;
        var row = _rows[i];
        _steps[i].Filename = row.EditFilename;
        _steps[i].PortOverride = int.TryParse(row.EditPort, out var p) ? p : null;
        _steps[i].AutoPort     = AutoloadParser.ResolveDefaultPort(_steps[i].Filename ?? "");
        row.EditVisible = Visibility.Collapsed;
        Rebuild();
    }

    private void OnStepEditCancel(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i < _rows.Count)
            _rows[i].EditVisible = Visibility.Collapsed;
    }

    // ── Drag-drop reordering ──────────────────────────────────────────────────

    private void OnStepMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart  = e.GetPosition(StepsList);
        _dragging   = FindStepVM(e.OriginalSource as DependencyObject);
        _isDragging = false;
    }

    private void OnStepMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging || e.LeftButton != MouseButtonState.Pressed || _dragging == null) return;
        var pos = e.GetPosition(StepsList);
        if (Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance * 2 &&
            Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
        _isDragging = true;
        try
        {
            DragDrop.DoDragDrop(StepsList, new DataObject(typeof(StepVM), _dragging), DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
            _dragging   = null;
        }
    }

    private void OnStepDragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(typeof(StepVM));
        e.Effects = ok ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (!ok) return;

        var target = FindStepVM(e.OriginalSource as DependencyObject);
        foreach (var r in _rows)
            r.DropIndicatorVisibility = (target != null && r.Index == target.Index && _dragging != null && r.Index != _dragging.Index)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStepDragLeave(object sender, DragEventArgs e)
    {
        foreach (var r in _rows) r.DropIndicatorVisibility = Visibility.Collapsed;
    }

    private void OnStepDrop(object sender, DragEventArgs e)
    {
        foreach (var r in _rows) r.DropIndicatorVisibility = Visibility.Collapsed;
        if (!e.Data.GetDataPresent(typeof(StepVM))) return;
        var dragged = e.Data.GetData(typeof(StepVM)) as StepVM;
        var target  = FindStepVM(e.OriginalSource as DependencyObject);
        if (dragged == null || target == null || target.Index == dragged.Index) return;
        int from = dragged.Index;
        int to   = target.Index;
        if (from < 0 || from >= _steps.Count || to < 0 || to >= _steps.Count) return;
        var item = _steps[from];
        _steps.RemoveAt(from);
        _steps.Insert(to, item);
        Rebuild();
    }

    // Walk up the visual tree to find the nearest ancestor (or self) whose DataContext is StepVM
    private static StepVM? FindStepVM(DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is FrameworkElement fe && fe.DataContext is StepVM vm)
                return vm;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    // ── Save / Export / Run ───────────────────────────────────────────────────

    private void OnExportZip(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { MessageBox.Show("No steps to export."); return; }
        var dlg = new SaveFileDialog { Filter = "ZIP archive|*.zip", FileName = "autoload.zip" };
        if (dlg.ShowDialog() != true) return;

        var content = AutoloadParser.StepsToContent(_steps);
        using var ms  = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("autoload.txt");
            using var sw = new StreamWriter(entry.Open());
            sw.Write(content);

            foreach (var step in _steps.Where(s => s.Type == "payload" && !string.IsNullOrEmpty(s.Filename)))
            {
                var src = Path.Combine(AppPaths.PayloadsDir, step.Filename!);
                if (!File.Exists(src)) continue;
                var ze = zip.CreateEntry(step.Filename!);
                using var zs = ze.Open();
                using var fs = File.OpenRead(src);
                fs.CopyTo(zs);
            }
        }
        File.WriteAllBytes(dlg.FileName, ms.ToArray());
        LogBus.Log($"Exported ZIP → {dlg.FileName}", LogLevel.Success);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = ProfileName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { MessageBox.Show("Enter a profile name."); return; }
        var content = AutoloadParser.StepsToContent(_steps);
        Storage.WriteProfile(name, content);
        LogBus.Log($"Saved profile '{name}'", LogLevel.Success);
        MessageBox.Show($"Profile '{name}' saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.None);
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (ExecEngine.State is ExecEngine.Running or ExecEngine.Paused)
        {
            ExecEngine.RequestStop();
            return;
        }
        var ip = Storage.LoadPs5Ip();
        if (string.IsNullOrEmpty(ip)) { MessageBox.Show("Set PS5 IP in Settings first."); return; }
        var content = AutoloadParser.StepsToContent(_steps);
        if (string.IsNullOrWhiteSpace(content)) { MessageBox.Show("No steps in flow."); return; }
        await ExecEngine.RunAsync(ip, content, continueOnError: false, profileName: ProfileName.Text.Trim());
    }

    public void LoadProfile(string name)
    {
        var content = Storage.ReadProfile(name);
        if (content == null) return;
        ProfileName.Text = name.EndsWith(".txt") ? name[..^4] : name;
        _steps.Clear();
        var dirs = AutoloadParser.Parse(content);
        var pins = AutoloadParser.ParseVersionPins(content);
        foreach (var d in dirs)
        {
            switch (d)
            {
                case SendDirective s:
                    _steps.Add(new FlowStep
                    {
                        Type = "payload", Filename = s.Filename,
                        PortOverride = s.Port != s.AutoPort ? s.Port : null,
                        AutoPort = s.AutoPort,
                        Version = pins.TryGetValue(s.Filename, out var v) ? v : null
                    });
                    break;
                case DelayDirective dl:
                    _steps.Add(new FlowStep { Type = "delay", Ms = dl.Ms });
                    break;
                case WaitPortDirective w:
                    _steps.Add(new FlowStep { Type = "wait_port", Port = w.Port, Timeout = w.Timeout, IntervalMs = w.IntervalMs });
                    break;
            }
        }
        Rebuild();
    }

    // ── Rebuild ───────────────────────────────────────────────────────────────

    private static readonly Brush _payloadColor = new SolidColorBrush(Color.FromRgb(59, 130, 246));
    private static readonly Brush _delayColor   = new SolidColorBrush(Color.FromRgb(245, 158, 11));
    private static readonly Brush _waitColor    = new SolidColorBrush(Color.FromRgb(16, 185, 129));

    private void Rebuild()
    {
        _rows.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            var s = _steps[i];
            var (typeLabel, typeColor, mainText, subText) = s.Type switch
            {
                "payload" => (
                    "PAYLOAD",
                    _payloadColor,
                    s.Filename ?? "",
                    $"Port {s.PortOverride ?? s.AutoPort}" + (!string.IsNullOrEmpty(s.Version) ? $" • {s.Version}" : "")
                ),
                "delay" => (
                    "DELAY",
                    _delayColor,
                    $"{s.Ms} ms",
                    "pause between steps"
                ),
                "wait_port" => (
                    "WAIT",
                    _waitColor,
                    $"Port {s.Port}",
                    $"up to {s.Timeout}s · poll {s.IntervalMs}ms"
                ),
                _ => (s.Type.ToUpper(), _waitColor, s.Type, "")
            };

            _rows.Add(new StepVM
            {
                Index      = i,
                IndexLabel = $"{i + 1}.",
                TypeLabel  = typeLabel,
                TypeColor  = typeColor,
                MainText   = mainText,
                SubText    = subText,
                IsPayload  = s.Type == "payload",
                EditVisible = Visibility.Collapsed,
            });
        }
        StepCount.Text = _steps.Count > 0 ? $"{_steps.Count} step{(_steps.Count != 1 ? "s" : "")}" : "";
    }
}

public class StepVM : INotifyPropertyChanged
{
    private Visibility _editVisible = Visibility.Collapsed;
    private Visibility _dropIndicatorVisibility = Visibility.Collapsed;
    private string _editFilename = "";
    private string _editPort = "";

    public int    Index      { get; set; }
    public string IndexLabel { get; set; } = "";
    public Brush  TypeColor  { get; set; } = Brushes.Gray;
    public string TypeLabel  { get; set; } = "";
    public string MainText   { get; set; } = "";
    public string SubText    { get; set; } = "";
    public bool   IsPayload  { get; set; }

    public List<string> EditPayloads { get; set; } = [];

    public Visibility DropIndicatorVisibility
    {
        get => _dropIndicatorVisibility;
        set { _dropIndicatorVisibility = value; OnPropertyChanged(); }
    }

    public Visibility EditVisible
    {
        get => _editVisible;
        set { _editVisible = value; OnPropertyChanged(); }
    }

    public string EditFilename
    {
        get => _editFilename;
        set { _editFilename = value; OnPropertyChanged(); }
    }

    public string EditPort
    {
        get => _editPort;
        set { _editPort = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
