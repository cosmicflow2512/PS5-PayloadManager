using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PS5AutoPayloadTool.Server;

public static class WsManager
{
    private static readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public static void Register(string id, WebSocket ws) => _sockets[id] = ws;
    public static void Unregister(string id) => _sockets.TryRemove(id, out _);

    public static async Task BroadcastAsync(string type, string message,
        string level = "info", string profile = "")
    {
        var payload = type switch
        {
            "exec_state" => JsonSerializer.Serialize(new
            {
                type,
                state   = message,
                profile
            }),
            _ => JsonSerializer.Serialize(new
            {
                type,
                message,
                level
            })
        };

        var bytes = Encoding.UTF8.GetBytes(payload);
        var dead  = new List<string>();

        foreach (var (id, ws) in _sockets)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                else
                    dead.Add(id);
            }
            catch { dead.Add(id); }
        }

        foreach (var id in dead) _sockets.TryRemove(id, out _);
    }

    public static async Task HandleAsync(WebSocket ws)
    {
        var id  = Guid.NewGuid().ToString("N");
        Register(id, ws);

        var buf = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buf, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
                // Handle ping
                var msg = Encoding.UTF8.GetString(buf, 0, result.Count);
                if (msg.Contains("\"ping\""))
                {
                    var pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                    await ws.SendAsync(pong, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
        catch { }
        finally { Unregister(id); }
    }
}
