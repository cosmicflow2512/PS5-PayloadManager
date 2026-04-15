using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Views;

public partial class FlowBuilderPage : UserControl
{
    private readonly ObservableCollection<FlowStepModel> _steps = new();
    private readonly ExecEngine _engine = new();

    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    public FlowBuilderPage()
    {
        InitializeComponent();
        _engine.ProgressChanged += OnEngineProgress;
        FlowStepsList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) => UpdateEmptyHint();
    }

    public void Refresh()
    {
        TxtPS5Host.Text = MainWindow.Config.PS5Host;

        // Reload steps from config
        _steps.Clear();
        foreach (var s in MainWindow.Config.CurrentFlow)
            _steps.Add(s);

        // Populate payload combo
        CmbPayloadStep.ItemsSource = MainWindow.Config.Payloads;
        if (CmbPayloadStep.Items.Count > 0)
            CmbPayloadStep.SelectedIndex = 0;

        UpdateEmptyHint();
    }

    private void UpdateEmptyHint()
    {
        TxtFlowEmpty.Visibility = _steps.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ── PS5 host text box ────────────────────────────────────────────────────

    private void TxtPS5Host_TextChanged(object sender, TextChangedEventArgs e)
    {
        MainWindow.Config.PS5Host = TxtPS5Host.Text.Trim();
    }

    // ── Check ports ──────────────────────────────────────────────────────────

    private async void BtnCheckPorts_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtPS5Host.Text.Trim();
        var luaTask = PortChecker.CheckPortAsync(host, 9026, 2_000);
        var elfTask = PortChecker.CheckPortAsync(host, 9021, 2_000);
        await Task.WhenAll(luaTask, elfTask);

        bool luaOpen = luaTask.Result;
        bool elfOpen = elfTask.Result;

        EllipseLua.Fill = luaOpen ? _dotOn : _dotOff;
        EllipseElf.Fill = elfOpen ? _dotOn : _dotOff;

        // Update sidebar too
        if (Window.GetWindow(this) is MainWindow mw)
            mw.SetPortIndicators(luaOpen, elfOpen);

        TxtFlowLog.Text = $"Lua 9026: {(luaOpen ? "OPEN" : "closed")}   ELF 9021: {(elfOpen ? "OPEN" : "closed")}";
    }

    // ── Add steps ────────────────────────────────────────────────────────────

    private void BtnAddPayloadStep_Click(object sender, RoutedEventArgs e)
    {
        if (CmbPayloadStep.SelectedItem is not PayloadItem item)
        {
            TxtFlowLog.Text = "Select a payload first.";
            return;
        }

        if (!int.TryParse(TxtPayloadPort.Text.Trim(), out int port))
            port = 9021;

        var step = new FlowStepModel
        {
            Type        = FlowStepType.Payload,
            PayloadName = item.Name,
            Port        = port
        };

        _steps.Add(step);
        SyncFlowToConfig();
    }

    private void BtnAddWaitStep_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtWaitPort.Text.Trim(), out int port))
            port = 9021;
        if (!int.TryParse(TxtWaitTimeout.Text.Trim(), out int timeout))
            timeout = 60;

        var step = new FlowStepModel
        {
            Type           = FlowStepType.Wait,
            Port           = port,
            TimeoutSeconds = timeout,
            IntervalMs     = 500
        };

        _steps.Add(step);
        SyncFlowToConfig();
    }

    private void BtnAddDelayStep_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtDelayMs.Text.Trim(), out int ms))
            ms = 500;

        var step = new FlowStepModel
        {
            Type    = FlowStepType.Delay,
            DelayMs = ms
        };

        _steps.Add(step);
        SyncFlowToConfig();
    }

    // ── Reorder / Remove ─────────────────────────────────────────────────────

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not FlowStepModel step) return;
        int idx = _steps.IndexOf(step);
        if (idx <= 0) return;
        _steps.Move(idx, idx - 1);
        SyncFlowToConfig();
    }

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not FlowStepModel step) return;
        int idx = _steps.IndexOf(step);
        if (idx < 0 || idx >= _steps.Count - 1) return;
        _steps.Move(idx, idx + 1);
        SyncFlowToConfig();
    }

    private void BtnRemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not FlowStepModel step) return;
        _steps.Remove(step);
        SyncFlowToConfig();
    }

    // ── Save as profile ──────────────────────────────────────────────────────

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0)
        {
            TxtFlowLog.Text = "Flow is empty — nothing to save.";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title            = "Save Flow as Profile",
            Filter           = "Profile files (*.txt)|*.txt",
            InitialDirectory = AppPaths.ProfilesDir,
            FileName         = "flow_profile.txt"
        };

        if (dlg.ShowDialog() != true) return;

        var lines = _steps.Select(s => s.ToProfileLine()).Where(l => !string.IsNullOrEmpty(l));
        File.WriteAllLines(dlg.FileName, lines);

        TxtFlowLog.Text = $"Profile saved: {Path.GetFileName(dlg.FileName)}";
    }

    // ── Run / Stop ───────────────────────────────────────────────────────────

    private async void BtnRunFlow_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0)
        {
            TxtFlowLog.Text = "Flow is empty — nothing to run.";
            return;
        }

        var host       = TxtPS5Host.Text.Trim();
        var directives = BuildDirectives();

        BtnRun.IsEnabled  = false;
        BtnStop.IsEnabled = true;
        PrgFlow.Value     = 0;

        await _engine.RunAsync(host, directives);

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
            TxtFlowLog.Text = e.Message;

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

    private void SyncFlowToConfig()
    {
        MainWindow.Config.CurrentFlow = _steps.ToList();
        MainWindow.SaveConfig();
    }

    private List<IDirective> BuildDirectives()
    {
        var list = new List<IDirective>();
        foreach (var step in _steps)
        {
            switch (step.Type)
            {
                case FlowStepType.Payload:
                    var localPath = Path.Combine(AppPaths.PayloadsDir, step.PayloadName);
                    list.Add(new SendDirective { FilePath = localPath, Port = step.Port });
                    break;
                case FlowStepType.Wait:
                    list.Add(new WaitPortDirective
                    {
                        Port           = step.Port,
                        TimeoutSeconds = step.TimeoutSeconds,
                        IntervalMs     = step.IntervalMs
                    });
                    break;
                case FlowStepType.Delay:
                    list.Add(new DelayDirective { DelayMs = step.DelayMs });
                    break;
            }
        }
        return list;
    }
}
