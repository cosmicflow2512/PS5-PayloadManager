using System.IO;
using System.Windows;
using System.Windows.Threading;
using PS5AutoPayloadTool.Modules.Core;

namespace PS5AutoPayloadTool;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Wire up global exception handlers before anything else so startup
        // failures show a dialog instead of silently closing the process.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            AppPaths.EnsureDirectories();
            LogService.Init(Path.Combine(AppPaths.LogsDir, "app.log"), debugMode: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Startup failed during initialisation:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                "PS5 AutoPayload Tool — Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Close();
        base.OnExit(e);
    }

    // ── Global exception handlers ────────────────────────────────────────────

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("App", $"Unhandled UI exception: {e.Exception.Message}\n{e.Exception.StackTrace}");
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "PS5 AutoPayload Tool — Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        var msg = ex != null ? $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}" : e.ExceptionObject?.ToString();
        try { LogService.Error("App", $"Fatal domain exception: {msg}"); } catch { }
        MessageBox.Show(
            $"A fatal error occurred and the application must close:\n\n{msg}",
            "PS5 AutoPayload Tool — Fatal Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.Error("App", $"Unobserved task exception: {e.Exception.Message}");
        e.SetObserved();
    }
}
