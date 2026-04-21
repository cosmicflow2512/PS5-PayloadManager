using System.Threading;

namespace PS5AutoPayloadTool.Core;

public static class ExecEngine
{
    public const string Idle      = "idle";
    public const string Running   = "running";
    public const string Paused    = "paused";
    public const string Stopped   = "stopped";
    public const string Completed = "completed";
    public const string Failed    = "failed";

    private static string  _state   = Idle;
    private static string  _profile = "";
    private static CancellationTokenSource? _cts;
    private static SemaphoreSlim _pauseGate = new(1, 1);
    private static bool _paused;

    public static string State   => _state;
    public static string Profile => _profile;

    public static async Task RunAsync(string host, string profileContent,
        bool continueOnError, string profileName)
    {
        if (_state == Running || _state == Paused) return;

        _cts    = new CancellationTokenSource();
        _paused = false;
        SetState(Running, profileName);

        var directives = AutoloadParser.Parse(profileContent);
        bool anyFail = false;

        for (int i = 0; i < directives.Count; i++)
        {
            if (_cts.Token.IsCancellationRequested)
            {
                SetState(Stopped, profileName);
                return;
            }

            if (_paused)
            {
                LogBus.Log($"[{i + 1}/{directives.Count}] Paused …", LogLevel.Warn);
                await _pauseGate.WaitAsync(_cts.Token);
                _pauseGate.Release();
            }

            var d = directives[i];
            bool ok = await ExecuteDirectiveAsync(d, host, i + 1, directives.Count, _cts.Token);

            if (!ok)
            {
                anyFail = true;
                if (!continueOnError)
                {
                    SetState(Failed, profileName);
                    return;
                }
            }

            if (_cts.Token.IsCancellationRequested)
            {
                SetState(Stopped, profileName);
                return;
            }
        }

        SetState(anyFail ? Failed : Completed, profileName);
    }

    public static void RequestStop()
    {
        _cts?.Cancel();
        ResumePause();
    }

    public static void RequestPause()
    {
        if (_state != Running) return;
        _paused = true;
        _pauseGate.Wait(0);
        SetState(Paused, _profile);
    }

    public static void RequestResume()
    {
        if (_state != Paused) return;
        _paused = false;
        try { _pauseGate.Release(); } catch { }
        SetState(Running, _profile);
    }

    private static void ResumePause()
    {
        _paused = false;
        try { _pauseGate.Release(); } catch { }
    }

    private static void SetState(string state, string profile)
    {
        _state   = state;
        _profile = profile;
        LogBus.StateChange(state, profile);
    }

    private static async Task<bool> ExecuteDirectiveAsync(Directive d, string host,
        int idx, int total, CancellationToken ct)
    {
        switch (d)
        {
            case SendDirective send:
            {
                LogBus.Log($"[{idx}/{total}] Sending {send.Filename} -> {host}:{send.Port}", LogLevel.Info);
                var (ok, msg, _) = await PayloadSender.SendAsync(host, send.Port, send.Filename, ct: ct);
                LogBus.Log($"[{idx}/{total}] {(ok ? "OK " : "FAIL ")}{msg}", ok ? LogLevel.Success : LogLevel.Error);
                return ok;
            }

            case DelayDirective delay:
            {
                LogBus.Log($"[{idx}/{total}] Delay {delay.Ms} ms", LogLevel.Info);
                try { await Task.Delay(delay.Ms, ct); } catch (OperationCanceledException) { return false; }
                return true;
            }

            case WaitPortDirective wait:
            {
                LogBus.Log($"[{idx}/{total}] Waiting for {host}:{wait.Port} (up to {wait.Timeout}s)", LogLevel.Info);
                bool reached = await PortChecker.WaitAsync(host, wait.Port,
                    wait.Timeout, wait.IntervalMs / 1000.0, ct,
                    (e, t) =>
                    {
                        LogBus.Log($"[{idx}/{total}] Port {wait.Port}: {e:F0}s / {t}s", LogLevel.Info);
                        return Task.CompletedTask;
                    });

                LogBus.Log($"[{idx}/{total}] {(reached ? "Port open" : "Port timeout")}", reached ? LogLevel.Success : LogLevel.Error);
                return reached;
            }

            default: return true;
        }
    }
}
