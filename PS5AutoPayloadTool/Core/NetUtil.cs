using System.Net;
using System.Net.Sockets;

namespace PS5AutoPayloadTool.Core;

public static class NetUtil
{
    public static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
