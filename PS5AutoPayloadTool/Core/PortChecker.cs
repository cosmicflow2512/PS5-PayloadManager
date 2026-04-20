using System.Net.Sockets;

namespace PS5AutoPayloadTool.Core;

public static class PortChecker
{
    public static async Task<bool> CheckAsync(string host, int port, double timeoutSec = 2.0)
    {
        try
        {
            using var client = new TcpClient();
            var task = client.ConnectAsync(host, port);
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(timeoutSec))) == task
                && !task.IsFaulted)
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> WaitAsync(string host, int port,
        double totalTimeoutSec = 60.0, double intervalSec = 0.5,
        CancellationToken ct = default,
        Func<double, double, Task>? onProgress = null)
    {
        double elapsed = 0;
        while (elapsed < totalTimeoutSec)
        {
            ct.ThrowIfCancellationRequested();
            if (await CheckAsync(host, port, 2.0)) return true;
            if (onProgress != null) await onProgress(elapsed, totalTimeoutSec);
            await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
            elapsed += intervalSec;
        }
        return false;
    }
}
