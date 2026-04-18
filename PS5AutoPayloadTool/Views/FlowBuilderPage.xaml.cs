using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Models;
using PS5AutoPayloadTool.Modules.Builder;
using PS5AutoPayloadTool.Modules.Core;
using PS5AutoPayloadTool.Modules.Execution;

namespace PS5AutoPayloadTool.Views;

public partial class FlowBuilderPage : UserControl
{
    private readonly ObservableCollection<BuilderStep> _steps = new();
    private readonly ExecEngine _engine = new();
    private bool _loading;

    public List<string> PayloadNames { get; private set; } = new();

    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    private static readonly SolidColorBrush _badgeBgCompat  = new(Color.FromRgb(30,  58,  47));
    private static readonly SolidColorBrush _badgeBgIncompat = new(Color.FromRgb(58,  30,  30));
    private static readonly SolidColorBrush _badgeFgCompat  = new(Color.FromRgb(166, 227, 161));
    private static readonly SolidColorBrush _badgeFgIncompat = new(Color.FromRgb(243, 139, 168));

    public FlowBuilderPage()
    {
        DataContext = this;
        InitializeComponent();
        FlowStepsList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) =>
        {
            UpdateEmptyHint();
            UpdateCompatibilityBadge();
        };
        _engine.ProgressChanged += OnEngineProgress;
    }

    // ── Public: load steps from ProfilesPage edit ────────────────────────────

    public void LoadSteps(List<BuilderStep> steps, string? profileName = null)
    {
        _loading = true;
        _steps.Clear();
        foreach (var s in steps) _steps.Add(s);
        if (profileName != null)
            TxtSaveName.Text = profileName;
        UpdateEmptyHint();
        _loading = false;
        UpdateCompatibilityBadge();
        UpdateVersionLabels();
        SyncFlowToConfig();
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    public void Refresh()
    {
        _loading = true;

        PayloadNames = MainWindow.Config.PayloadMeta.Keys.ToList();

        var devices = MainWindow.Config.Devices;
        CmbDevice.ItemsSource = null;
        CmbDevice.ItemsSource = devices;
        var selIp = MainWindow.Config.State.SelectedDeviceIp;
        CmbDevice.SelectedItem = devices.FirstOrDefault(d => d.Ip == selIp)
                               ?? devices.FirstOrDefault();

        _steps.Clear();
        foreach (var s in MainWindow.Config.State.BuilderSteps)
            _steps.Add(s);

        var ports = MainWindow.Config.Ports;
        TxtLuaPortLabel.Text = $"Lua {ports.LuaPort}";
        TxtElfPortLabel.Text = $"ELF {ports.ElfPort}";

        UpdateEmptyHint();
        _loading = false;
        UpdateCompatibilityBadge();
        UpdateVersionLabels();
    }

    private void UpdateEmptyHint() =>
        TxtFlowEmpty.Visibility = _steps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private string GetSelectedHost()
    {
        if (CmbDevice.SelectedItem is DeviceConfig dev) return dev.Ip;
        return MainWindow.Config.PS5Host;
    }

    // ── Compatibility badge ──────────────────────────────────────────────────

    private void UpdateCompatibilityBadge()
    {
        bool ok = FlowService.IsAutoloadCompatible(_steps, out _);

        CompatBadge.Background    = ok ? _badgeBgCompat  : _badgeBgIncompat;
        TxtCompatBadge.Foreground = ok ? _badgeFgCompat  : _badgeFgIncompat;
        TxtCompatBadge.Text       = ok ? "✓ Autoload Compatible" : "✗ Not Compatible";
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
        var host  = GetSelectedHost();
        var ports = MainWindow.Config.Ports;

        var luaTask = PortChecker.CheckPortAsync(host, ports.LuaPort, 2_000);
        var elfTask = PortChecker.CheckPortAsync(host, ports.ElfPort, 2_000);
        await Task.WhenAll(luaTask, elfTask);

        bool luaOpen = luaTask.Result, elfOpen = elfTask.Result;
        EllipseLua.Fill = luaOpen ? _dotOn : _dotOff;
        EllipseElf.Fill = elfOpen ? _dotOn : _dotOff;

        if (Window.GetWindow(this) is MainWindow mw)
            mw.SetPortIndicators(luaOpen, elfOpen);

        AppendLog($"Lua {ports.LuaPort}: {(luaOpen ? "OPEN" : "closed")}   " +
                  $"ELF {ports.ElfPort}: {(elfOpen ? "OPEN" : "closed")}");
    }

    // ── Instant add step buttons ──────────────────────────────────────────────

    private void BtnAddPayload_Click(object sender, RoutedEventArgs e)
    {
        var first = PayloadNames.FirstOrDefault() ?? "";
        var port  = PayloadSender.GetDefaultPort(first, MainWindow.Config.Ports);
        _steps.Add(new BuilderStep { Type = "payload", Payload = first, Port = port });
        UpdateCompatibilityBadge();
        SyncFlowToConfig();
    }

    private void BtnAddWait_Click(object sender, RoutedEventArgs e)
    {
        _steps.Add(new BuilderStep { Type = "wait_port", Port = MainWindow.Config.Ports.ElfPort, Timeout = 60 });
        UpdateCompatibilityBadge();
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
        if (cb.DataContext is BuilderStep step && cb.SelectedItem is string name)
        {
            step.Port            = PayloadSender.GetDefaultPort(name, MainWindow.Config.Ports);
            step.SelectedVersion = "Latest";
            UpdateCompatibilityBadge();
            UpdateVersionLabels();
            SyncFlowToConfig();
        }
    }

    private void CmbStepVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (sender is not ComboBox cb) return;
        if (cb.DataContext is BuilderStep step && cb.SelectedItem is string version)
        {
            step.SelectedVersion = version;
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
        UpdateCompatibilityBadge();
        SyncFlowToConfig();
    }

    // ── Save as profile ───────────────────────────────────────────────────────

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppendLog("Flow is empty — nothing to save."); return; }
        TxtSaveError.Visibility  = Visibility.Collapsed;
        SaveNamePanel.Visibility = Visibility.Visible;
        TxtSaveName.Focus();
        TxtSaveName.SelectAll();
    }

    private void BtnConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtSaveName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            TxtSaveError.Text       = "Please enter a name for the flow.";
            TxtSaveError.Visibility = Visibility.Visible;
            return;
        }

        var fileName = $"{name}.txt";
        var content  = string.Join("\n", _steps.Select(s => s.ToProfileLine()).Where(l => l.Length > 0));
        Directory.CreateDirectory(AppPaths.ProfilesDir);
        File.WriteAllText(Path.Combine(AppPaths.ProfilesDir, fileName), content);
        MainWindow.Config.Profiles[fileName]          = content;
        MainWindow.Config.State.BuilderProfileName    = name;
        MainWindow.SaveConfig();

        SaveNamePanel.Visibility = Visibility.Collapsed;
        AppendLog($"Saved: {fileName}");
    }

    private void BtnCancelSave_Click(object sender, RoutedEventArgs e)
    {
        SaveNamePanel.Visibility = Visibility.Collapsed;
    }

    // ── Export Autoload ZIP ───────────────────────────────────────────────────

    private async void BtnExportZip_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppendLog("Flow is empty — nothing to export."); return; }

        // Compatibility check — block export for incompatible flows
        if (!FlowService.IsAutoloadCompatible(_steps, out var incompatible))
        {
            MessageBox.Show(
                "Invalid Autoload Configuration\n\nNot allowed:\n" +
                "• WAIT (port-based execution)\n" +
                "• LUA payloads\n\n" +
                "DELAY steps are allowed.\n\n" +
                $"Remove first: {string.Join(", ", incompatible)}",
                "Invalid Autoload Configuration",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var elfSteps = _steps
            .Where(s => s.Type == "payload" &&
                        !s.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (elfSteps.Count == 0)
        {
            AppendLog("No valid payloads for autoload export (need .elf or .bin).");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title    = "Export PS5 Autoload ZIP",
            Filter   = "ZIP files (*.zip)|*.zip",
            FileName = "ps5-autoloader.zip"
        };
        if (dlg.ShowDialog() != true) return;

        if (sender is Button exportBtn) exportBtn.IsEnabled = false;
        try
        {
            var progress    = new Progress<string>(AppendLog);
            var autoloadTxt = FlowService.BuildAutoloadTxt(_steps);

            var result = await MainWindow.ExportSvc.ExportAutoloadZipAsync(
                dlg.FileName, autoloadTxt, elfSteps, MainWindow.Config, progress);

            if (result.Error != null)
            {
                AppendLog($"Export error: {result.Error}");
            }
            else
            {
                var skipNote = result.Skipped > 0 ? $", {result.Skipped} skipped" : "";
                AppendLog($"Exported: {Path.GetFileName(dlg.FileName)}  " +
                          $"({result.Copied} payload(s){skipNote}, autoload.txt)");
            }
        }
        finally
        {
            if (sender is Button b) b.IsEnabled = true;
        }
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

    /// <summary>
    /// Delegates version data refresh to <see cref="FlowService.UpdateVersionData"/>.
    /// Keeps views free of version-resolution logic.
    /// </summary>
    private void UpdateVersionLabels() =>
        FlowService.UpdateVersionData(_steps, MainWindow.Config);

    private void SyncFlowToConfig()
    {
        MainWindow.Config.State.BuilderSteps = _steps.ToList();
        MainWindow.SaveConfig();
    }
}
