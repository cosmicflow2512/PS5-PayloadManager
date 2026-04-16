using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class FlowBuilderPage : UserControl
{
    private readonly ObservableCollection<BuilderStep> _steps = new();
    private readonly ExecEngine _engine = new();
    private bool _loading;

    // Exposed for DataTemplate RelativeSource bindings
    public List<string> PayloadNames { get; private set; } = new();

    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    public FlowBuilderPage()
    {
        DataContext = this;
        InitializeComponent();
        FlowStepsList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) => UpdateEmptyHint();
        _engine.ProgressChanged += OnEngineProgress;
    }

    public void Refresh()
    {
        _loading = true;

        TxtFlowName.Text = MainWindow.Config.State.BuilderProfileName;

        // Payload names list for inline combos
        PayloadNames = MainWindow.Config.PayloadMeta.Keys.ToList();

        // Device dropdown
        var devices = MainWindow.Config.Devices;
        CmbDevice.ItemsSource = null;
        CmbDevice.ItemsSource = devices;
        var selIp = MainWindow.Config.State.SelectedDeviceIp;
        CmbDevice.SelectedItem = devices.FirstOrDefault(d => d.Ip == selIp)
                               ?? devices.FirstOrDefault();

        // Reload steps
        _steps.Clear();
        foreach (var s in MainWindow.Config.State.BuilderSteps)
            _steps.Add(s);

        UpdateEmptyHint();
        _loading = false;
    }

    private void UpdateEmptyHint() =>
        TxtFlowEmpty.Visibility = _steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private string GetSelectedHost()
    {
        if (CmbDevice.SelectedItem is DeviceConfig dev) return dev.Ip;
        return MainWindow.Config.PS5Host;
    }

    // ── Flow name ────────────────────────────────────────────────────────────

    private void TxtFlowName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        MainWindow.Config.State.BuilderProfileName = TxtFlowName.Text.Trim();
        MainWindow.SaveConfig();
    }

    // ── Device selection ─────────────────────────────────────────────────────

    private void CmbDevice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (CmbDevice.SelectedItem is DeviceConfig dev)
        {
            MainWindow.Config.PS5Host = dev.Ip;
            MainWindow.Config.State.SelectedDeviceIp = dev.Ip;
            MainWindow.SaveConfig();
        }
    }

    // ── Check ports ──────────────────────────────────────────────────────────

    private async void BtnCheckPorts_Click(object sender, RoutedEventArgs e)
    {
        var host = GetSelectedHost();
        var luaTask = PortChecker.CheckPortAsync(host, 9026, 2_000);
        var elfTask = PortChecker.CheckPortAsync(host, 9021, 2_000);
        await Task.WhenAll(luaTask, elfTask);

        bool luaOpen = luaTask.Result, elfOpen = elfTask.Result;
        EllipseLua.Fill = luaOpen ? _dotOn : _dotOff;
        EllipseElf.Fill = elfOpen ? _dotOn : _dotOff;

        if (Window.GetWindow(this) is MainWindow mw)
            mw.SetPortIndicators(luaOpen, elfOpen);

        AppendLog($"Lua 9026: {(luaOpen ? "OPEN" : "closed")}   ELF 9021: {(elfOpen ? "OPEN" : "closed")}");
    }

    // ── Instant add step buttons ──────────────────────────────────────────────

    private void BtnAddPayload_Click(object sender, RoutedEventArgs e)
    {
        var first = PayloadNames.FirstOrDefault() ?? "";
        var port  = string.IsNullOrEmpty(first) ? 9021 : PayloadSender.GetDefaultPort(first);
        _steps.Add(new BuilderStep { Type = "payload", Payload = first, Port = port });
        SyncFlowToConfig();
    }

    private void BtnAddWait_Click(object sender, RoutedEventArgs e)
    {
        _steps.Add(new BuilderStep { Type = "wait_port", Port = 9021, Timeout = 60 });
        SyncFlowToConfig();
    }

    private void BtnAddDelay_Click(object sender, RoutedEventArgs e)
    {
        _steps.Add(new BuilderStep { Type = "delay", Ms = 1000 });
        SyncFlowToConfig();
    }

    // ── Inline step editing ───────────────────────────────────────────────────

    private void CmbStepPayload_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        // The TwoWay binding already wrote to step.Payload — just update Port
        if (cb.DataContext is BuilderStep step && cb.SelectedItem is string name)
        {
            step.Port = PayloadSender.GetDefaultPort(name);
            SyncFlowToConfig();
        }
    }

    private void StepField_LostFocus(object sender, RoutedEventArgs e) =>
        SyncFlowToConfig();

    // ── Reorder / Remove ─────────────────────────────────────────────────────

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuilderStep step) return;
        int idx = _steps.IndexOf(step);
        if (idx > 0) { _steps.Move(idx, idx - 1); SyncFlowToConfig(); }
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuilderStep step) return;
        int idx = _steps.IndexOf(step);
        if (idx >= 0 && idx < _steps.Count - 1) { _steps.Move(idx, idx + 1); SyncFlowToConfig(); }
    }

    private void BtnRemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuilderStep step) return;
        _steps.Remove(step);
        SyncFlowToConfig();
    }

    // ── Save as profile ──────────────────────────────────────────────────────

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppendLog("Flow is empty — nothing to save."); return; }
        var name = string.IsNullOrWhiteSpace(TxtFlowName.Text)
            ? $"flow_{DateTime.Now:yyyyMMdd_HHmm}.txt"
            : $"{TxtFlowName.Text.Trim()}.txt";
        var content = string.Join("\n", _steps.Select(s => s.ToProfileLine()).Where(l => l.Length > 0));
        Directory.CreateDirectory(AppPaths.ProfilesDir);
        File.WriteAllText(Path.Combine(AppPaths.ProfilesDir, name), content);
        MainWindow.Config.Profiles[name] = content;
        MainWindow.SaveConfig();
        AppendLog($"Saved: {name}");
    }

    // ── Run / Stop ───────────────────────────────────────────────────────────

    private async void BtnRunFlow_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppendLog("Flow is empty — nothing to run."); return; }

        var host    = GetSelectedHost();
        var content = string.Join("\n", _steps.Select(s => s.ToProfileLine()).Where(l => l.Length > 0));
        var parsed  = ProfileParser.Parse(content, AppPaths.PayloadsDir);

        BtnRun.IsEnabled  = false;
        BtnStop.IsEnabled = true;
        PrgFlow.Value     = 0;

        MainWindow.Config.PS5Host = host;
        await _engine.RunAsync(host, parsed);

        BtnRun.IsEnabled  = true;
        BtnStop.IsEnabled = false;
    }

    private void BtnStopFlow_Click(object sender, RoutedEventArgs e)
    {
        _engine.RequestStop();
        BtnRun.IsEnabled  = true;
        BtnStop.IsEnabled = false;
    }

    // ── Engine progress ──────────────────────────────────────────────────────

    private void OnEngineProgress(object? sender, ExecProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            AppendLog(e.Message);
            if (e.TotalSteps > 0 && e.StepIndex >= 0)
                PrgFlow.Value = (double)e.StepIndex / e.TotalSteps * 100.0;
            if (e.State is ExecState.Completed) PrgFlow.Value = 100;
            if (e.State is ExecState.Completed or ExecState.Failed or ExecState.Stopped)
            { BtnRun.IsEnabled = true; BtnStop.IsEnabled = false; }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void AppendLog(string msg)
    {
        TxtFlowLog.Text = string.IsNullOrEmpty(TxtFlowLog.Text)
            ? msg : TxtFlowLog.Text + "\n" + msg;
        LogScroll.ScrollToBottom();
    }

    private void SyncFlowToConfig()
    {
        MainWindow.Config.State.BuilderSteps = _steps.ToList();
        MainWindow.SaveConfig();
    }
}
