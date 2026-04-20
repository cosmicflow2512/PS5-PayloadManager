using PS5AutoPayloadTool.Server;

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
        await SetState(Running, profileName);

        var directives = AutoloadParser.Parse(profileContent);
        bool anyFail = false;

        for (int i = 0; i < directives.Count; i++)
        {
            if (_cts.Token.IsCancellationRequested)
            {
                await SetState(Stopped, profileName);
                return;
            }

            // Pause support
            if (_paused)
            {
                await WsManager.BroadcastAsync("status",
                    $"[{i + 1}/{directives.Count}] Paused …", "warn");
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
                    await SetState(Failed, profileName);
                    return;
                }
            }

            if (_cts.Token.IsCancellationRequested)
            {
                await SetState(Stopped, profileName);
                return;
            }
        }

        await SetState(anyFail ? Failed : Completed, profileName);
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
        _pauseGate.Wait(0); // drain one slot → gate closed
        _ = SetState(Paused, _profile);
    }

    public static void RequestResume()
    {
        if (_state != Paused) return;
        _paused = false;
        try { _pauseGate.Release(); } catch { }
        _ = SetState(Running, _profile);
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private static void ResumePause()
    {
        _paused = false;
        try { _pauseGate.Release(); } catch { }
    }

    private static async Task SetState(string state, string profile)
    {
        _state   = state;
        _profile = profile;
        await WsManager.BroadcastAsync("exec_state", state, profile: profile);
    }

    private static async Task<bool> ExecuteDirectiveAsync(Directive d, string host,
        int idx, int total, CancellationToken ct)
    {
        switch (d)
        {
            case SendDirective send:
            {
                await WsManager.BroadcastAsync("status",
                    $"[{idx}/{total}] Sending {send.Filename} → {host}:{send.Port}", "info");
                var (ok, msg, _) = await PayloadSender.SendAsync(host, send.Port, send.Filename, ct: ct);
                await WsManager.BroadcastAsync("status",
                    $"[{idx}/{total}] {(ok ? "✔ " : "✗ ")}{msg}", ok ? "success" : "error");
                return ok;
            }

            case DelayDirective delay:
            {
                await WsManager.BroadcastAsync("status",
                    $"[{idx}/{total}] Delay {delay.Ms} ms …", "info");
                try { await Task.Delay(delay.Ms, ct); } catch (OperationCanceledException) { return false; }
                return true;
            }

            case WaitPortDirective wait:
            {
                await WsManager.BroadcastAsync("status",
                    $"[{idx}/{total}] Waiting for {host}:{wait.Port} (up to {wait.Timeout}s) …", "info");
                bool reached = await PortChecker.WaitAsync(host, wait.Port,
                    wait.Timeout, wait.IntervalMs / 1000.0, ct,
                    async (e, t) => await WsManager.BroadcastAsync("status",
                        $"[{idx}/{total}] Port {wait.Port}: {e:F0}s / {t}s …", "info"));

                await WsManager.BroadcastAsync("status",
                    $"[{idx}/{total}] {(reached ? "✔ Port open" : "✗ Port timeout")}", reached ? "success" : "error");
                return reached;
            }

            default: return true;
        }
    }
}
