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

    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    public FlowBuilderPage()
    {
        InitializeComponent();
        FlowStepsList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) => UpdateEmptyHint();
        _engine.ProgressChanged += OnEngineProgress;
    }

    public void Refresh()
    {
        _loading = true;

        // Flow name
        TxtFlowName.Text = MainWindow.Config.State.BuilderProfileName;

        // Device dropdown
        var devices = MainWindow.Config.Devices;
        CmbDevice.ItemsSource = null;
        CmbDevice.ItemsSource = devices;

        // Select the saved device, or default to first
        var selectedIp = MainWindow.Config.State.SelectedDeviceIp;
        var selected = devices.FirstOrDefault(d => d.Ip == selectedIp)
                    ?? devices.FirstOrDefault();
        CmbDevice.SelectedItem = selected;

        // Reload steps from config
        _steps.Clear();
        foreach (var s in MainWindow.Config.State.BuilderSteps)
            _steps.Add(s);

        // Populate payload combo with payload names
        CmbPayloadStep.ItemsSource = null;
        CmbPayloadStep.ItemsSource = MainWindow.Config.PayloadMeta.Keys.ToList();
        if (CmbPayloadStep.Items.Count > 0)
            CmbPayloadStep.SelectedIndex = 0;

        UpdateEmptyHint();
        _loading = false;
    }

    private void UpdateEmptyHint()
    {
        TxtFlowEmpty.Visibility = _steps.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetSelectedHost()
    {
        if (CmbDevice.SelectedItem is DeviceConfig dev)
            return dev.Ip;
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

        bool luaOpen = luaTask.Result;
        bool elfOpen = elfTask.Result;

        EllipseLua.Fill = luaOpen ? _dotOn : _dotOff;
        EllipseElf.Fill = elfOpen ? _dotOn : _dotOff;

        if (Window.GetWindow(this) is MainWindow mw)
            mw.SetPortIndicators(luaOpen, elfOpen);

        AppendLog($"Lua 9026: {(luaOpen ? "OPEN" : "closed")}   ELF 9021: {(elfOpen ? "OPEN" : "closed")}");
    }

    // ── Add steps ────────────────────────────────────────────────────────────

    private void BtnAddPayloadStep_Click(object sender, RoutedEventArgs e)
    {
        var name = CmbPayloadStep.SelectedItem?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            AppendLog("Select a payload first.");
            return;
        }

        if (!int.TryParse(TxtPayloadPort.Text.Trim(), out int port))
            port = PayloadSender.GetDefaultPort(name);
        if (port == 0) port = 9021;

        _steps.Add(new BuilderStep { Type = "payload", Payload = name, Port = port });
        SyncFlowToConfig();
    }

    private void BtnAddWaitStep_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtWaitPort.Text.Trim(), out int port))
            port = 9026;
        if (!int.TryParse(TxtWaitTimeout.Text.Trim(), out int timeout))
            timeout = 60;
        if (port == 0) port = 9026;
        if (timeout == 0) timeout = 60;

        _steps.Add(new BuilderStep { Type = "wait_port", Port = port, Timeout = timeout });
        SyncFlowToConfig();
    }

    private void BtnAddDelayStep_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtDelayMs.Text.Trim(), out int ms))
            ms = 500;
        if (ms == 0) ms = 500;

        _steps.Add(new BuilderStep { Type = "delay", Ms = ms });
        SyncFlowToConfig();
    }

    // ── Reorder / Remove ─────────────────────────────────────────────────────

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuilderStep step) return;
        int idx = _steps.IndexOf(step);
        if (idx <= 0) return;
        _steps.Move(idx, idx - 1);
        SyncFlowToConfig();
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BuilderStep step) return;
        int idx = _steps.IndexOf(step);
        if (idx < 0 || idx >= _steps.Count - 1) return;
        _steps.Move(idx, idx + 1);
        SyncFlowToConfig();
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
        if (_steps.Count == 0)
        {
            AppendLog("Flow is empty — nothing to save.");
            return;
        }

        var name = $"flow_{DateTime.Now:yyyyMMdd_HHmm}.txt";
        var lines = _steps.Select(s => s.ToProfileLine()).Where(l => !string.IsNullOrEmpty(l));
        var content = string.Join("\n", lines);
        var path = Path.Combine(AppPaths.ProfilesDir, name);
        File.WriteAllText(path, content);
        MainWindow.Config.Profiles[name] = content;
        MainWindow.SaveConfig();
        AppendLog($"Saved profile: {name}");
    }

    // ── Run / Stop ───────────────────────────────────────────────────────────

    private async void BtnRunFlow_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0)
        {
            AppendLog("Flow is empty — nothing to run.");
            return;
        }

        var host = GetSelectedHost();
        MainWindow.Config.PS5Host = host;

        var directives = _steps.Select(s => s.ToProfileLine())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();

        var content = string.Join("\n", directives);
        var parsed  = ProfileParser.Parse(content, AppPaths.PayloadsDir);

        BtnRun.IsEnabled  = false;
        BtnStop.IsEnabled = true;
        PrgFlow.Value     = 0;

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

            if (e.State is ExecState.Completed)
                PrgFlow.Value = 100;

            if (e.State is ExecState.Completed or ExecState.Failed or ExecState.Stopped)
            {
                BtnRun.IsEnabled  = true;
                BtnStop.IsEnabled = false;
            }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void AppendLog(string message)
    {
        TxtFlowLog.Text = string.IsNullOrEmpty(TxtFlowLog.Text)
            ? message
            : TxtFlowLog.Text + "\n" + message;
        LogScroll.ScrollToBottom();
    }

    private void SyncFlowToConfig()
    {
        MainWindow.Config.State.BuilderSteps = _steps.ToList();
        MainWindow.SaveConfig();
    }
}
