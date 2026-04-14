using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool;

public partial class MainWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────
    private readonly ExecEngine _engine = new();
    private CancellationTokenSource? _autoSendCts;

    // ── Catppuccin log colours ─────────────────────────────────────────────────
    private static readonly SolidColorBrush ClrDefault = new(Color.FromRgb(205, 214, 244)); // #CDD6F4
    private static readonly SolidColorBrush ClrGreen   = new(Color.FromRgb(166, 227, 161)); // #A6E3A1
    private static readonly SolidColorBrush ClrRed     = new(Color.FromRgb(243, 139, 168)); // #F38BA8
    private static readonly SolidColorBrush ClrYellow  = new(Color.FromRgb(249, 226, 175)); // #F9E2AF
    private static readonly SolidColorBrush ClrBlue    = new(Color.FromRgb(137, 180, 250)); // #89B4FA
    private static readonly SolidColorBrush ClrSubtle  = new(Color.FromRgb(108, 112, 134)); // #6C7086
    private static readonly SolidColorBrush ClrIndicatorOff = new(Color.FromRgb(69, 71, 90));
    private static readonly SolidColorBrush ClrIndicatorOn  = new(Color.FromRgb(166, 227, 161));

    // ── Ctor ──────────────────────────────────────────────────────────────────
    public MainWindow()
    {
        InitializeComponent();
        _engine.ProgressChanged += OnEngineProgress;
        RefreshPayloads();
        RefreshProfiles();
        Log($"Data directory: {AppPaths.Base}", ClrSubtle);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  LIST HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private void RefreshPayloads()
    {
        LstPayloads.Items.Clear();
        foreach (var f in Directory.GetFiles(AppPaths.PayloadsDir)
            .Where(IsPayloadFile)
            .OrderBy(Path.GetFileName))
        {
            LstPayloads.Items.Add(Path.GetFileName(f));
        }
    }

    private void RefreshProfiles()
    {
        LstProfiles.Items.Clear();
        foreach (var f in Directory.GetFiles(AppPaths.ProfilesDir, "*.txt")
            .OrderBy(Path.GetFileName))
        {
            LstProfiles.Items.Add(Path.GetFileName(f));
        }
    }

    private static bool IsPayloadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".lua" or ".elf" or ".bin";
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  LOGGING
    // ══════════════════════════════════════════════════════════════════════════

    private void Log(string message, SolidColorBrush? colour = null)
    {
        Dispatcher.Invoke(() =>
        {
            var ts   = DateTime.Now.ToString("HH:mm:ss");
            var item = new ListBoxItem
            {
                Content    = $"[{ts}] {message}",
                Foreground = colour ?? ClrDefault
            };
            LstLog.Items.Add(item);
            LstLog.ScrollIntoView(item);
        });
    }

    private void SetStatus(string text) =>
        Dispatcher.Invoke(() => TxtStatus.Text = text);

    private void SetProgress(double pct) =>
        Dispatcher.Invoke(() => PrgProgress.Value = Math.Clamp(pct, 0, 100));

    // ══════════════════════════════════════════════════════════════════════════
    //  ENGINE EVENTS
    // ══════════════════════════════════════════════════════════════════════════

    private void OnEngineProgress(object? sender, ExecProgressEventArgs e)
    {
        var colour = e.IsError      ? ClrRed    :
                     e.Message.Contains("OK —") ? ClrGreen  :
                     e.Message.Contains("Wait") ? ClrYellow :
                     null;

        Log(e.Message, colour);
        SetStatus(e.State.ToString());

        if (e.TotalSteps > 0 && e.StepIndex >= 0)
            SetProgress((double)e.StepIndex / e.TotalSteps * 100.0);

        if (e.State is ExecState.Completed or ExecState.Failed or ExecState.Stopped)
        {
            Dispatcher.Invoke(() =>
            {
                BtnPause.IsEnabled = false;
                BtnStop.IsEnabled  = false;
                BtnPause.Content   = "Pause";
                if (e.State == ExecState.Completed) SetProgress(100);
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PAYLOAD PANEL
    // ══════════════════════════════════════════════════════════════════════════

    private void BtnAddPayload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Select PS5 Payload(s)",
            Filter      = "PS5 Payloads (*.lua;*.elf;*.bin)|*.lua;*.elf;*.bin|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var src in dlg.FileNames)
        {
            var dst = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
            Log($"Added: {Path.GetFileName(src)}", ClrBlue);
        }
        RefreshPayloads();
    }

    private void BtnRemovePayload_Click(object sender, RoutedEventArgs e)
    {
        if (LstPayloads.SelectedItem is not string name) return;
        var path = Path.Combine(AppPaths.PayloadsDir, name);
        if (!File.Exists(path)) { RefreshPayloads(); return; }

        var res = MessageBox.Show(
            $"Remove \"{name}\" from the payload library?",
            "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        File.Delete(path);
        Log($"Removed: {name}", ClrYellow);
        RefreshPayloads();
    }

    private async void BtnSendPayload_Click(object sender, RoutedEventArgs e)
    {
        if (LstPayloads.SelectedItem is not string name)
        {
            MessageBox.Show("Select a payload first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var host     = TxtHost.Text.Trim();
        var filePath = Path.Combine(AppPaths.PayloadsDir, name);
        var port     = PayloadSender.GetDefaultPort(name);

        Log($"Sending {name} → {host}:{port}…", ClrBlue);
        BtnSend.IsEnabled = false;
        SetStatus("Sending…");
        SetProgress(0);

        var progress = new Progress<(long Sent, long Total)>(p =>
            SetProgress((double)p.Sent / p.Total * 100.0));

        var result = await PayloadSender.SendAsync(host, port, filePath, progress: progress);

        if (result.Success)
        {
            Log($"Success! {result.BytesSent:N0} bytes sent to :{port}", ClrGreen);
            SetProgress(100);
            SetStatus("Done");
        }
        else
        {
            Log($"Failed: {result.Message}", ClrRed);
            SetProgress(0);
            SetStatus("Failed");
        }

        BtnSend.IsEnabled = true;
    }

    // ── Drag & drop onto payload list ─────────────────────────────────────────

    private void LstPayloads_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void LstPayloads_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        int added = 0;
        foreach (var src in files)
        {
            if (!IsPayloadFile(src)) continue;
            var dst = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
            Log($"Dropped: {Path.GetFileName(src)}", ClrBlue);
            added++;
        }
        if (added > 0) RefreshPayloads();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PROFILE PANEL
    // ══════════════════════════════════════════════════════════════════════════

    private void BtnLoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Load Autoload Profile(s)",
            Filter      = "Profile files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var src in dlg.FileNames)
        {
            var dst = Path.Combine(AppPaths.ProfilesDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
            Log($"Profile loaded: {Path.GetFileName(src)}", ClrBlue);
        }
        RefreshProfiles();
    }

    private async void BtnRunProfile_Click(object sender, RoutedEventArgs e)
    {
        if (LstProfiles.SelectedItem is not string name)
        {
            MessageBox.Show("Select a profile first.", "No Selection",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var host        = TxtHost.Text.Trim();
        var profilePath = Path.Combine(AppPaths.ProfilesDir, name);

        List<IDirective> directives;
        try
        {
            directives = ProfileParser.ParseFile(profilePath, AppPaths.PayloadsDir);
        }
        catch (Exception ex)
        {
            Log($"Profile parse error: {ex.Message}", ClrRed);
            return;
        }

        if (directives.Count == 0)
        {
            Log("Profile is empty — nothing to run.", ClrYellow);
            return;
        }

        Log($"Running profile \"{name}\" ({directives.Count} step(s))…", ClrBlue);
        BtnPause.IsEnabled = true;
        BtnStop.IsEnabled  = true;
        BtnRun.IsEnabled   = false;
        SetProgress(0);

        await _engine.RunAsync(host, directives);

        BtnRun.IsEnabled = true;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  AUTO-SEND
    // ══════════════════════════════════════════════════════════════════════════

    private async void ChkAutoSend_Checked(object sender, RoutedEventArgs e)
    {
        if (LstPayloads.SelectedItem is not string name)
        {
            ChkAutoSend.IsChecked = false;
            MessageBox.Show("Select a payload before enabling Auto-Send.",
                "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var host     = TxtHost.Text.Trim();
        var filePath = Path.Combine(AppPaths.PayloadsDir, name);
        var port     = PayloadSender.GetDefaultPort(name);

        _autoSendCts = new CancellationTokenSource();
        Log($"Auto-Send ON — watching port {port} for {name}", ClrBlue);
        SetStatus("Auto-Send: monitoring…");

        try
        {
            await AutoSendLoop(host, port, filePath, _autoSendCts.Token);
        }
        catch (OperationCanceledException) { }

        SetStatus("Auto-Send: off");
    }

    private void ChkAutoSend_Unchecked(object sender, RoutedEventArgs e)
    {
        _autoSendCts?.Cancel();
        Log("Auto-Send OFF", ClrYellow);
    }

    private async Task AutoSendLoop(string host, int port, string filePath, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1. Wait for port to open
            Log($"Waiting for port {port} on {host}…", ClrYellow);
            var waitProgress = new Progress<string>(msg => Log(msg, ClrSubtle));
            await PortChecker.WaitForPortOpenIndefiniteAsync(host, port, 1_000, waitProgress, ct);

            if (ct.IsCancellationRequested) break;

            // 2. Send
            Log($"Port {port} open — sending {Path.GetFileName(filePath)}…", ClrGreen);
            var result = await PayloadSender.SendAsync(host, port, filePath, cancellationToken: ct);

            if (result.Success)
                Log($"Auto-Send OK — {result.BytesSent:N0} bytes", ClrGreen);
            else
                Log($"Auto-Send failed: {result.Message}", ClrRed);

            // 3. Wait for port to close before the next attempt
            Log("Waiting for port to close…", ClrSubtle);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(2_000, ct);
                bool stillOpen = await PortChecker.CheckPortAsync(host, port, 1_000);
                if (!stillOpen) break;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  TOOLBAR BUTTONS
    // ══════════════════════════════════════════════════════════════════════════

    private async void BtnCheckPorts_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtHost.Text.Trim();
        Log($"Checking ports on {host}…", ClrBlue);

        var luaTask = PortChecker.CheckPortAsync(host, 9026, 2_000);
        var elfTask = PortChecker.CheckPortAsync(host, 9021, 2_000);
        await Task.WhenAll(luaTask, elfTask);

        bool luaOpen = luaTask.Result;
        bool elfOpen = elfTask.Result;

        Dispatcher.Invoke(() =>
        {
            EllipseLua.Fill = luaOpen ? ClrIndicatorOn : ClrIndicatorOff;
            EllipseElf.Fill = elfOpen ? ClrIndicatorOn : ClrIndicatorOff;
        });

        Log($"Lua  9026: {(luaOpen ? "OPEN" : "closed")}", luaOpen ? ClrGreen : ClrSubtle);
        Log($"ELF  9021: {(elfOpen ? "OPEN" : "closed")}", elfOpen ? ClrGreen : ClrSubtle);
    }

    private void BtnOpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        // Opens %LOCALAPPDATA%\PS5Autopayload\ in Explorer
        Process.Start(new ProcessStartInfo
        {
            FileName        = AppPaths.Base,
            UseShellExecute = true
        });
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e) =>
        LstLog.Items.Clear();

    // ── Pause / Stop ─────────────────────────────────────────────────────────

    private void BtnPause_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.State == ExecState.Running)
        {
            _engine.RequestPause();
            BtnPause.Content = "Resume";
        }
        else if (_engine.State == ExecState.Paused)
        {
            _engine.RequestResume();
            BtnPause.Content = "Pause";
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _engine.RequestStop();
        _autoSendCts?.Cancel();
        ChkAutoSend.IsChecked = false;
        BtnPause.IsEnabled    = false;
        BtnStop.IsEnabled     = false;
        BtnPause.Content      = "Pause";
        Log("Stop requested.", ClrYellow);
    }
}
