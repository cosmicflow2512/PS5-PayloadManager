using System.IO;
using System.Windows;
using PS5AutoPayloadTool.Modules.Core;

namespace PS5AutoPayloadTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureDirectories();
        LogService.Init(Path.Combine(AppPaths.LogsDir, "app.log"), debugMode: false);
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Close();
        base.OnExit(e);
    }
}
