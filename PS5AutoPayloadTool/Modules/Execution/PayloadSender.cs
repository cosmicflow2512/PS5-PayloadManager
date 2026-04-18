using System.IO;
using System.Net.Sockets;
using PS5AutoPayloadTool.Models;

namespace PS5AutoPayloadTool.Modules.Execution;

public record PayloadSendResult(bool Success, string Message, long BytesSent);

public static class PayloadSender
{
    private const int ChunkSize = 4096;

    /// <summary>
    /// Returns the default port for a given filename, respecting user-configured
    /// port settings when provided:
    ///   .lua → LuaPort  (default 9026)
    ///   .bin → BinPort if set, otherwise ElfPort  (default 9021)
    ///   .elf / other → ElfPort  (default 9021)
    /// </summary>
    public static int GetDefaultPort(string filename, PortSettings? ports = null)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ports != null)
        {
            return ext switch
            {
                ".lua" => ports.LuaPort,
                ".bin" => ports.EffectiveBinPort,
                _      => ports.ElfPort
            };
        }
        return ext == ".lua" ? 9026 : 9021;
    }

    /// <summary>
    /// Sends a payload file to the PS5 over TCP in 4 KB chunks.
    /// </summary>
    public static async Task<PayloadSendResult> SendAsync(
        string host,
        int port,
        string filePath,
        int timeoutMs = 10_000,
        IProgress<(long Sent, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return new(false, $"File not found: {filePath}", 0);

        var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
        long bytesSent = 0;

        try
        {
            using var client = new TcpClient();

            var connectTask = client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(timeoutMs, cancellationToken);
            var winner = await Task.WhenAny(connectTask, timeoutTask);

            if (winner == timeoutTask)
                return new(false, $"Connection timeout to {host}:{port}", 0);

            if (connectTask.IsFaulted)
                return new(false, connectTask.Exception?.InnerException?.Message ?? "Connection failed", 0);

            using var stream = client.GetStream();
            int offset = 0;

            while (offset < data.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(ChunkSize, data.Length - offset);
                await stream.WriteAsync(data.AsMemory(offset, count), cancellationToken);
                offset    += count;
                bytesSent += count;
                progress?.Report((bytesSent, data.Length));
            }

            await stream.FlushAsync(cancellationToken);
            return new(true, $"Sent {bytesSent:N0} bytes successfully", bytesSent);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Cancelled", bytesSent);
        }
        catch (Exception ex)
        {
            return new(false, ex.Message, bytesSent);
        }
    }
}
