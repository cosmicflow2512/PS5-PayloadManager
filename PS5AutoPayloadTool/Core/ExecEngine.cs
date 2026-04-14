using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Core;

public enum ExecState { Idle, Running, Paused, Stopped, Completed, Failed }

public class ExecProgressEventArgs : EventArgs
{
    public string Message { get; init; } = "";
    public ExecState State { get; init; }
    public int StepIndex { get; init; }
    public int TotalSteps { get; init; }
    public bool IsError => Message.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Executes a list of profile directives sequentially with
/// pause / resume / stop support.
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
        State = ExecState.Paused;
        Raise("Execution paused.", ExecState.Paused, -1, -1);
    }

    public void RequestResume()
    {
        if (State != ExecState.Paused) return;
        _paused = false;
        State = ExecState.Running;
        _pauseGate.Release();
        Raise("Execution resumed.", ExecState.Running, -1, -1);
    }

    // ── Run ──────────────────────────────────────────────────────────────────

    public async Task RunAsync(
        string host,
        List<IDirective> directives,
        bool continueOnError = false)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _paused = false;
        State = ExecState.Running;

        var ct = _cts.Token;

        try
        {
            for (int i = 0; i < directives.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                // ── Pause gate ──────────────────────────────────────────────
                if (_paused)
                    await _pauseGate.WaitAsync(ct);

                string result = directives[i] switch
                {
                    SendDirective s    => await RunSend(host, s, i, directives.Count, ct),
                    DelayDirective d   => await RunDelay(d, i, directives.Count, ct),
                    WaitPortDirective w => await RunWaitPort(host, w, i, directives.Count, ct),
                    _                  => "Unknown directive — skipped."
                };

                bool failed = result.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
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
            _cts.Dispose();
            _cts = null;
        }
    }

    // ── Step handlers ─────────────────────────────────────────────────────────

    private async Task<string> RunSend(
        string host, SendDirective s, int idx, int total, CancellationToken ct)
    {
        Raise($"[{idx + 1}/{total}] Sending {Path.GetFileName(s.FilePath)} → :{s.Port}", ExecState.Running, idx, total);

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

        bool ok = await PortChecker.WaitForPortAsync(
            host, w.Port,
            totalTimeoutMs: w.TimeoutSeconds * 1_000,
            intervalMs: w.IntervalMs,
            progress: progress,
            cancellationToken: ct);

        var msg = ok
            ? $"[{idx + 1}/{total}] Port {w.Port} is open."
            : $"ERROR [{idx + 1}/{total}] Timeout waiting for port {w.Port}.";

        Raise(msg, ExecState.Running, idx, total);
        return msg;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private void Raise(string message, ExecState state, int stepIndex, int totalSteps)
        => ProgressChanged?.Invoke(this, new ExecProgressEventArgs
        {
            Message = message,
            State = state,
            StepIndex = stepIndex,
            TotalSteps = totalSteps
        });
}
