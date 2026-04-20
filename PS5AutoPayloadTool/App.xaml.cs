using System.Windows;
using System.Windows.Threading;
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
}
