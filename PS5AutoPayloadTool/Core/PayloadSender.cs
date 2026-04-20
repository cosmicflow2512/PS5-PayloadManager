using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace PS5AutoPayloadTool.Core;

public static class PayloadSender
{
    public static async Task<(bool ok, string message, long bytes)> SendAsync(
        string host, int port, string filename,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        var path = Path.Combine(AppPaths.PayloadsDir, Path.GetFileName(filename));
        if (!File.Exists(path))
            return (false, $"File not found: {filename}", 0);

        try
        {
            using var client = new TcpClient();
            var connTask = client.ConnectAsync(host, port, ct).AsTask();
            if (await Task.WhenAny(connTask, Task.Delay(10_000, ct)) != connTask)
                return (false, $"Connection to {host}:{port} timed out", 0);
            if (connTask.IsFaulted)
                return (false, connTask.Exception?.InnerException?.Message ?? "Connection failed", 0);

            await using var stream = client.GetStream();
            await using var fs = File.OpenRead(path);

            var buf   = new byte[4096];
            long total = 0;
            int  read;
            while ((read = await fs.ReadAsync(buf, ct)) > 0)
            {
                await stream.WriteAsync(buf.AsMemory(0, read), ct);
                total += read;
                progress?.Report(total);
            }

            return (true, $"Sent {total} bytes to {host}:{port}", total);
        }
        catch (OperationCanceledException)
        {
            return (false, "Cancelled", 0);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }

    public static int ResolvePort(string filename, int? portOverride = null)
    {
        if (portOverride.HasValue) return portOverride.Value;
        return AutoloadParser.ResolveDefaultPort(filename);
    }
}
