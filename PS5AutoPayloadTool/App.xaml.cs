using System.IO;
using System.Reflection;
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
            ShowError("PS5 AutoPayload Tool — Startup Error", ex);
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
        try { LogService.Error("App", $"Unhandled UI exception: {e.Exception}"); } catch { }
        ShowError("PS5 AutoPayload Tool — Error", e.Exception);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        try { LogService.Error("App", $"Fatal domain exception: {ex}"); } catch { }
        if (ex != null)
            ShowError("PS5 AutoPayload Tool — Fatal Error", ex);
        else
            MessageBox.Show(e.ExceptionObject?.ToString(), "PS5 AutoPayload Tool — Fatal Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try { LogService.Error("App", $"Unobserved task exception: {e.Exception.Message}"); } catch { }
        e.SetObserved();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Unwraps TargetInvocationException / AggregateException chains to expose
    /// the real root cause, writes it to a fallback file, then shows a dialog.
    /// </summary>
    private static void ShowError(string title, Exception outer)
    {
        var root = Unwrap(outer);

        // Build a readable message: root cause first, then full chain.
        var text =
            $"Root cause:\n{root.GetType().Name}: {root.Message}\n\n" +
            $"Stack trace:\n{root.StackTrace}";

        if (root != outer)
            text += $"\n\nFull exception chain:\n{outer}";

        // Write to fallback file in case this is a startup crash and the log
        // file hasn't been opened yet.
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PS5Autopayload");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "startup-error.txt"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}\n\n{text}\n");
        }
        catch { }

        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Unwraps TargetInvocationException and single-inner AggregateException.</summary>
    private static Exception Unwrap(Exception ex)
    {
        while (ex.InnerException != null &&
               (ex is TargetInvocationException ||
                ex is AggregateException ae && ae.InnerExceptions.Count == 1))
            ex = ex.InnerException;
        return ex;
    }
}
