using System.Windows;
using PS5AutoPayloadTool.Core;

namespace PS5AutoPayloadTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Ensure %LOCALAPPDATA%\PS5Autopayload\ and sub-dirs exist before
        // anything else touches the filesystem.
        AppPaths.EnsureDirectories();
        base.OnStartup(e);
    }
}
