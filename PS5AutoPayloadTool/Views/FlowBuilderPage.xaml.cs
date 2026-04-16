using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
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

    // Port indicator dots
    private static readonly SolidColorBrush _dotOff = new(Color.FromRgb(69,  71,  90));
    private static readonly SolidColorBrush _dotOn  = new(Color.FromRgb(166, 227, 161));

    // Compatibility badge colours
    private static readonly SolidColorBrush _badgeBgCompat   = new(Color.FromRgb(30,  58,  47));
    private static readonly SolidColorBrush _badgeBgIncompat  = new(Color.FromRgb(58,  30,  30));
    private static readonly SolidColorBrush _badgeFgCompat   = new(Color.FromRgb(166, 227, 161));
    private static readonly SolidColorBrush _badgeFgIncompat  = new(Color.FromRgb(243, 139, 168));

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

        // Update port labels with configured values
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
        bool hasWait = _steps.Any(s => s.Type == "wait_port");
        bool hasLua  = _steps.Any(s => s.Type == "payload" &&
                       s.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));
        bool ok = !hasWait && !hasLua;

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
            step.Port = PayloadSender.GetDefaultPort(name, MainWindow.Config.Ports);
            UpdateCompatibilityBadge();
            UpdateVersionLabels();
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

    // ── Save as profile (with name dialog) ───────────────────────────────────

    private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppendLog("Flow is empty — nothing to save."); return; }
        TxtSaveError.Visibility = Visibility.Collapsed;
        SaveNamePanel.Visibility = Visibility.Visible;
        TxtSaveName.Focus();
        TxtSaveName.SelectAll();
    }

    private void BtnConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtSaveName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            TxtSaveError.Text = "Please enter a name for the flow.";
            TxtSaveError.Visibility = Visibility.Visible;
            return;
        }

        var fileName = $"{name}.txt";
        var content  = string.Join("\n", _steps.Select(s => s.ToProfileLine()).Where(l => l.Length > 0));
        Directory.CreateDirectory(AppPaths.ProfilesDir);
        File.WriteAllText(Path.Combine(AppPaths.ProfilesDir, fileName), content);
        MainWindow.Config.Profiles[fileName] = content;
        MainWindow.Config.State.BuilderProfileName = name;
        MainWindow.SaveConfig();

        SaveNamePanel.Visibility = Visibility.Collapsed;
        AppendLog($"Saved: {fileName}");
    }

    private void BtnCancelSave_Click(object sender, RoutedEventArgs e)
    {
        SaveNamePanel.Visibility = Visibility.Collapsed;
    }

    // ── Export Autoload ZIP ───────────────────────────────────────────────────

    private void BtnExportZip_Click(object sender, RoutedEventArgs e)
    {
        if (_steps.Count == 0) { AppendLog("Flow is empty — nothing to export."); return; }

        bool hasWait = _steps.Any(s => s.Type == "wait_port");
        bool hasLua  = _steps.Any(s => s.Type == "payload"
                           && s.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));

        if (hasWait || hasLua)
        {
            var what = new List<string>();
            if (hasWait) what.Add("WAIT step(s)");
            if (hasLua)  what.Add("Lua payload(s)");
            var whatStr = string.Join(" and ", what);

            var answer = MessageBox.Show(
                $"This flow contains {whatStr} which are not supported in autoload.txt.\n\n" +
                "Remove incompatible steps and export anyway?",
                "Incompatible Steps",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;

            var toRemove = _steps
                .Where(s => s.Type == "wait_port" ||
                            (s.Type == "payload" && s.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var s in toRemove) _steps.Remove(s);
            UpdateCompatibilityBadge();
            SyncFlowToConfig();
            AppendLog($"Removed {toRemove.Count} incompatible step(s) before export.");
        }

        var elfSteps = _steps
            .Where(s => s.Type == "payload"
                     && !s.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (elfSteps.Count == 0)
        {
            AppendLog("No valid payloads for autoload export (need .elf or .bin).");
            return;
        }

        var sb = new StringBuilder();
        foreach (var step in _steps)
        {
            if (step.Type == "delay")
                sb.AppendLine($"!{step.Ms}");
            else if (step.Type == "payload"
                  && !step.Payload.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(step.Payload);
        }

        var dlg = new SaveFileDialog
        {
            Title    = "Export PS5 Autoload ZIP",
            Filter   = "ZIP files (*.zip)|*.zip",
            FileName = "ps5-autoloader.zip"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var zip = ZipFile.Open(dlg.FileName, ZipArchiveMode.Create);

            var txtEntry = zip.CreateEntry("ps5_autoloader/autoload.txt");
            using (var writer = new StreamWriter(txtEntry.Open()))
                writer.Write(sb.ToString());

            int copied = 0;
            foreach (var step in elfSteps)
            {
                var src = Path.Combine(AppPaths.PayloadsDir, step.Payload);
                if (!File.Exists(src))
                {
                    AppendLog($"Warning: {step.Payload} not downloaded — skipped in ZIP.");
                    continue;
                }
                var fileEntry = zip.CreateEntry($"ps5_autoloader/{step.Payload}");
                using var dest = fileEntry.Open();
                using var srcStream = File.OpenRead(src);
                srcStream.CopyTo(dest);
                copied++;
            }

            AppendLog($"Exported: {Path.GetFileName(dlg.FileName)}  ({copied} payload(s), autoload.txt)");
        }
        catch (Exception ex)
        {
            AppendLog($"Export error: {ex.Message}");
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
    /// Refreshes the VersionLabel on every payload step so the badge next to
    /// the ComboBox stays current (e.g. "(Latest)" or "(v1.03)").
    /// </summary>
    private void UpdateVersionLabels()
    {
        foreach (var step in _steps)
        {
            if (step.Type != "payload" || string.IsNullOrEmpty(step.Payload))
            {
                step.VersionLabel = "";
                continue;
            }

            if (MainWindow.Config.PayloadMeta.TryGetValue(step.Payload, out var meta)
                && !string.IsNullOrEmpty(meta.Version))
            {
                var isLatest = meta.Versions.Count > 0 && meta.Versions[0] == meta.Version;
                step.VersionLabel = isLatest ? "(Latest)" : $"({meta.Version})";
            }
            else
            {
                step.VersionLabel = "";
            }
        }
    }

    private void SyncFlowToConfig()
    {
        MainWindow.Config.State.BuilderSteps = _steps.ToList();
        MainWindow.SaveConfig();
    }
}
