using System.IO;

namespace SessionRecorder.Services.Platform;

/// <summary>Everything the app keeps on disk lives under %LocalAppData%\SessionRecorder.</summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SessionRecorder");

    /// <summary>The recording browser's own Chrome profile, so site logins survive between recordings.</summary>
    public static string ChromeProfileDir { get; } = Path.Combine(DataDir, "ChromeProfile");

    public static string LogFile { get; } = Path.Combine(DataDir, "session-recorder.log");
    public static string LogBackupFile { get; } = Path.Combine(DataDir, "session-recorder.old.log");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDir);
}
