using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using PS5AutoPayloadTool.Core;
using PS5AutoPayloadTool.Server;

namespace PS5AutoPayloadTool;

public partial class App : Application
{
    public static int ApiPort { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        if (!EnsureWebView2())
        {
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectories();
        ApiPort = NetUtil.FindFreePort();
        _ = ApiServer.StartAsync(ApiPort);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ExecEngine.RequestStop();
        base.OnExit(e);
    }

    private static bool EnsureWebView2()
    {
        try
        {
            var ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrEmpty(ver)) return true;
        }
        catch { }

        var result = MessageBox.Show(
            "WebView2 Runtime is required but not installed.\n\n" +
            "Click OK to download and install it automatically (requires internet access).\n" +
            "Click Cancel to exit.",
            "WebView2 Required",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.OK) return false;

        try
        {
            var bootstrapper = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
            using var http = new HttpClient();
            var data = http.GetByteArrayAsync("https://go.microsoft.com/fwlink/p/?LinkId=2124703").GetAwaiter().GetResult();
            File.WriteAllBytes(bootstrapper, data);

            var proc = Process.Start(new ProcessStartInfo(bootstrapper, "/silent /install") { UseShellExecute = true });
            proc?.WaitForExit();

            // Verify install succeeded
            var ver = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrEmpty(ver)) return true;

            MessageBox.Show("WebView2 installation may have failed. Please install it manually and restart.",
                "Install Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not install WebView2 automatically:\n{ex.Message}\n\n" +
                "Please download it manually from:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return false;
    }
}
