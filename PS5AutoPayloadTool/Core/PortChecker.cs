using System.Net.Sockets;

namespace PS5AutoPayloadTool.Core;

public static class PortChecker
{
    /// <summary>Returns true if the TCP port is reachable within the timeout.</summary>
    public static async Task<bool> CheckPortAsync(string host, int port, int timeoutMs = 2_000)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port).AsTask();
            var winner = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            return winner == connectTask && !connectTask.IsFaulted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Polls the port until it opens or the total timeout is exceeded.
    /// Pass <see cref="CancellationToken"/> to stop early.
    /// </summary>
    public static async Task<bool> WaitForPortAsync(
        string host,
        int port,
        int totalTimeoutMs = 60_000,
        int intervalMs = 500,
        int perCheckTimeoutMs = 1_000,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(totalTimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool open = await CheckPortAsync(host, port, perCheckTimeoutMs);
            if (open) return true;

            try { progress?.Report($"Waiting for port {port} on {host}…"); } catch { }

            await Task.Delay(intervalMs, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Polls indefinitely until the port opens. Used by the Auto-Send loop.
    /// Only stops when the <see cref="CancellationToken"/> is triggered.
    /// </summary>
    public static async Task WaitForPortOpenIndefiniteAsync(
        string host,
        int port,
        int intervalMs = 1_000,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            bool open = await CheckPortAsync(host, port, 1_000);
            if (open) return;

            try { progress?.Report($"Waiting for port {port} on {host}…"); } catch { }
            await Task.Delay(intervalMs, cancellationToken);
        }
    }
}
