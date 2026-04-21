using System.Windows;
using System.Windows.Threading;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        AppPaths.EnsureDirectories();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ExecEngine.RequestStop();
        base.OnExit(e);
    }
}
