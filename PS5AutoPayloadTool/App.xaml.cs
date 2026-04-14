using System.Windows;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.EnsureDirectories();
        base.OnStartup(e);
    }
}
