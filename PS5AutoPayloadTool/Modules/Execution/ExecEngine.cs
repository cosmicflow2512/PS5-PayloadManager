using System.Diagnostics;
using System.IO;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Modules.Execution;

public enum ExecState { Idle, Running, Paused, Stopped, Completed, Failed }

public class ExecProgressEventArgs : EventArgs
{
    public string    Message    { get; init; } = "";
    public ExecState State      { get; init; }
    public int       StepIndex  { get; init; }
    public int       TotalSteps { get; init; }
    public bool      IsError    => Message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Executes a list of profile directives sequentially with
/// pause / resume / stop support.
/// Belongs in the Execution module — the UI subscribes to
/// <see cref="ProgressChanged"/> for status updates.
/// </summary>
public class ExecEngine
{
    public ExecState State { get; private set; } = ExecState.Idle;

    public event EventHandler<ExecProgressEventArgs>? ProgressChanged;

    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _pauseGate = new(1, 1);
    private bool _paused;

    // ── Control ──────────────────────────────────────────────────────────────

    public void RequestStop()
    {
        _cts?.Cancel();
        _paused = false;
        _pauseGate.Release(Math.Max(0, 1 - _pauseGate.CurrentCount));
    }

    public void RequestPause()
    {
        if (State != ExecState.Running) return;
        _paused = true;
        State   = ExecState.Paused;
        Raise("Execution paused.", ExecState.Paused, -1, -1);
    }

    public void RequestResume()
    {
        if (State != ExecState.Paused) return;
        _paused = false;
        State   = ExecState.Running;
        _pauseGate.Release();
        Raise("Execution resumed.", ExecState.Running, -1, -1);
    }

    // ── Run ──────────────────────────────────────────────────────────────────

    public async Task RunAsync(
        string host,
        List<IDirective> directives,
        bool continueOnError = false,
        bool safeMode = false)
    {
        _cts?.Dispose();
        _cts    = new CancellationTokenSource();
        _paused = false;
        State   = ExecState.Running;

        var ct        = _cts.Token;
        var runId     = Guid.NewGuid().ToString("N")[..8];
        var startedAt = DateTime.UtcNow.ToString("O");
        var runSteps  = new List<FlowRunStep>();
        var totalSw   = Stopwatch.StartNew();

        if (safeMode)
            Raise("SAFE MODE — no payloads will be sent.", ExecState.Running, -1, -1);

        try
        {
            for (int i = 0; i < directives.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (_paused)
                    await _pauseGate.WaitAsync(ct);

                var stepSw = Stopwatch.StartNew();
                string result = directives[i] switch
                {
                    SendDirective     s => await RunSend(host, s, i, directives.Count, ct, safeMode),
                    DelayDirective    d => await RunDelay(d, i, directives.Count, ct),
                    WaitPortDirective w => await RunWaitPort(host, w, i, directives.Count, ct),
                    _                   => "Unknown directive — skipped."
                };
                stepSw.Stop();

                bool failed = result.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);

                runSteps.Add(new FlowRunStep(
                    Type:       StepType(directives[i]),
                    Label:      StepLabel(directives[i]),
                    DurationMs: (int)stepSw.ElapsedMilliseconds,
                    Success:    !failed,
                    Error:      failed ? result : null));

                if (failed && !continueOnError)
                {
                    State = ExecState.Failed;
                    return;
                }
            }

            State = ExecState.Completed;
            Raise("Profile completed successfully.", ExecState.Completed, directives.Count, directives.Count);
        }
        catch (OperationCanceledException)
        {
            State = ExecState.Stopped;
            Raise("Execution stopped by user.", ExecState.Stopped, -1, -1);
        }
        catch (Exception ex)
        {
            State = ExecState.Failed;
            Raise($"ERROR: {ex.Message}", ExecState.Failed, -1, -1);
        }
        finally
        {
            totalSw.Stop();
            FlowRunHistory.RecordRun(new FlowRunRecord(
                Id:        runId,
                StartedAt: startedAt,
                TotalMs:   (int)totalSw.ElapsedMilliseconds,
                SafeMode:  safeMode,
                Steps:     runSteps));
            _cts.Dispose();
            _cts = null;
        }
    }

    // ── Step handlers ─────────────────────────────────────────────────────────

    private async Task<string> RunSend(
        string host, SendDirective s, int idx, int total, CancellationToken ct, bool safeMode = false)
    {
        Raise($"[{idx + 1}/{total}] Sending {Path.GetFileName(s.FilePath)} → :{s.Port}",
              ExecState.Running, idx, total);

        if (safeMode)
        {
            var safeMsg = $"[SAFE] [{idx + 1}/{total}] Skipped send {Path.GetFileName(s.FilePath)} → :{s.Port}";
            Raise(safeMsg, ExecState.Running, idx, total);
            return safeMsg;
        }

        var result = await PayloadSender.SendAsync(host, s.Port, s.FilePath, cancellationToken: ct);

        var msg = result.Success
            ? $"[{idx + 1}/{total}] OK — sent {result.BytesSent:N0} bytes to :{s.Port}"
            : $"ERROR [{idx + 1}/{total}] {result.Message}";

        Raise(msg, ExecState.Running, idx, total);
        return msg;
    }

    private async Task<string> RunDelay(
        DelayDirective d, int idx, int total, CancellationToken ct)
    {
        Raise($"[{idx + 1}/{total}] Delay {d.DelayMs} ms…", ExecState.Running, idx, total);
        await Task.Delay(d.DelayMs, ct);
        return "Delay done.";
    }

    private async Task<string> RunWaitPort(
        string host, WaitPortDirective w, int idx, int total, CancellationToken ct)
    {
        Raise($"[{idx + 1}/{total}] Waiting for port {w.Port} (timeout {w.TimeoutSeconds}s)…",
              ExecState.Running, idx, total);

        var progress = new Progress<string>(msg => Raise(msg, ExecState.Running, idx, total));
        var sw = Stopwatch.StartNew();

        bool ok = await PortChecker.WaitForPortAsync(
            host, w.Port,
            totalTimeoutMs: w.TimeoutSeconds * 1_000,
            intervalMs: w.IntervalMs,
            progress: progress,
            cancellationToken: ct);
        sw.Stop();

        if (ok)
            PortTimingService.Record(w.Port, (int)sw.ElapsedMilliseconds);

        var msg = ok
            ? $"[{idx + 1}/{total}] Port {w.Port} is open."
            : $"ERROR [{idx + 1}/{total}] Timeout waiting for port {w.Port}.";

        Raise(msg, ExecState.Running, idx, total);
        return msg;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static string StepType(IDirective d) => d switch
    {
        SendDirective     => "send",
        DelayDirective    => "delay",
        WaitPortDirective => "wait_port",
        _                 => "unknown"
    };

    private static string StepLabel(IDirective d) => d switch
    {
        SendDirective     s => Path.GetFileName(s.FilePath),
        DelayDirective    dl => $"{dl.DelayMs} ms",
        WaitPortDirective w => $"port {w.Port}",
        _                   => "unknown"
    };

    private void Raise(string message, ExecState state, int stepIndex, int totalSteps)
        => ProgressChanged?.Invoke(this, new ExecProgressEventArgs
        {
            Message    = message,
            State      = state,
            StepIndex  = stepIndex,
            TotalSteps = totalSteps
        });
}
