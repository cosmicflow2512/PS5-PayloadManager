using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool.Views;

public partial class BuilderView : UserControl
{
    public ObservableCollection<StepRow> Rows { get; } = [];
    private readonly List<FlowStep> _steps = [];

    public BuilderView()
    {
        InitializeComponent();
        StepsList.ItemsSource = Rows;
        LogBus.OnStateChange += (state, _) => Dispatcher.Invoke(() =>
        {
            RunButton.Content = state is ExecEngine.Running or ExecEngine.Paused ? "Stop" : "Run";
        });
        Refresh();
    }

    public void Refresh()
    {
        PayloadCombo.ItemsSource = Storage.ListPayloads().Select(p => p.Name).ToList();
    }

    private void OnAddPayload(object sender, RoutedEventArgs e)  => ShowPanel("payload");
    private void OnAddDelay(object sender, RoutedEventArgs e)    => ShowPanel("delay");
    private void OnAddWait(object sender, RoutedEventArgs e)     => ShowPanel("wait");

    private void ShowPanel(string which)
    {
        AddPanel.Visibility = Visibility.Visible;
        PayloadAdd.Visibility = which == "payload" ? Visibility.Visible : Visibility.Collapsed;
        DelayAdd.Visibility   = which == "delay"   ? Visibility.Visible : Visibility.Collapsed;
        WaitAdd.Visibility    = which == "wait"    ? Visibility.Visible : Visibility.Collapsed;
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
        var interval = int.TryParse(WaitInterval.Text, out var i) ? i : 500;
        _steps.Add(new FlowStep { Type = "wait_port", Port = port, Timeout = timeout, IntervalMs = interval });
        AddPanel.Visibility = Visibility.Collapsed;
        Rebuild();
    }

    private void OnUp(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i > 0)
        {
            (_steps[i - 1], _steps[i]) = (_steps[i], _steps[i - 1]);
            Rebuild();
        }
    }

    private void OnDown(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i < _steps.Count - 1)
        {
            (_steps[i + 1], _steps[i]) = (_steps[i], _steps[i + 1]);
            Rebuild();
        }
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && int.TryParse(b.Tag?.ToString(), out var i) && i >= 0 && i < _steps.Count)
        {
            _steps.RemoveAt(i);
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Rows.Clear();
        for (int i = 0; i < _steps.Count; i++)
        {
            var s = _steps[i];
            Rows.Add(new StepRow
            {
                IndexNum = i,
                Index = $"{i + 1}.",
                Label = s.Type switch
                {
                    "payload"  => "▶ " + s.Filename,
                    "delay"    => "⏱ Delay",
                    "wait_port"=> "⧖ Wait for port",
                    _ => s.Type
                },
                Detail = s.Type switch
                {
                    "payload"   => $"Port {s.PortOverride ?? s.AutoPort}" + (string.IsNullOrEmpty(s.Version) ? "" : $" • {s.Version}"),
                    "delay"     => $"{s.Ms} ms",
                    "wait_port" => $"port {s.Port}, up to {s.Timeout}s (poll {s.IntervalMs}ms)",
                    _ => ""
                }
            });
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = ProfileName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { MessageBox.Show("Enter profile name."); return; }
        var content = AutoloadParser.StepsToContent(_steps);
        Storage.WriteProfile(name, content);
        LogBus.Log($"Saved profile '{name}'", LogLevel.Success);
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (ExecEngine.State is ExecEngine.Running or ExecEngine.Paused)
        {
            ExecEngine.RequestStop();
            return;
        }
        var ip = Storage.LoadUiState()["ps5_ip"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(ip)) { MessageBox.Show("Set PS5 IP in Settings first."); return; }
        var content = AutoloadParser.StepsToContent(_steps);
        if (string.IsNullOrWhiteSpace(content)) { MessageBox.Show("Empty flow."); return; }
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
}

public class StepRow
{
    public int    IndexNum { get; set; }
    public string Index    { get; set; } = "";
    public string Label    { get; set; } = "";
    public string Detail   { get; set; } = "";
}
