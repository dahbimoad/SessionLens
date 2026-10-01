using System.Windows;
using System.Windows.Threading;
using SessionRecorder.Services.Logging;
using SessionRecorder.Services.Platform;
using SessionRecorder.Views;

namespace SessionRecorder;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();
        AppLog.Info($"Session Recorder starting. os={Environment.OSVersion.VersionString} runtime={Environment.Version} pid={Environment.ProcessId}");

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        new RecorderWindow().Show();
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Keep the window alive; the failed action has already reported itself in the UI or the log.
        AppLog.Error("Unhandled exception on the UI thread.", e.Exception);
        e.Handled = true;
    }

    private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error($"Unhandled exception outside the UI thread. terminating={e.IsTerminating}", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("A background task failed and nobody observed it.", e.Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Info($"Session Recorder exiting. exitCode={e.ApplicationExitCode}");
        base.OnExit(e);
    }
}
