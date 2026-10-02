using System.IO;

namespace SessionLens.Services.Platform;

/// <summary>Everything the app keeps on disk lives under %LocalAppData%\SessionLens.</summary>
public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SessionLens");

    /// <summary>The recording browser's own Chrome profile, so site logins survive between recordings.</summary>
    public static string ChromeProfileDir { get; } = Path.Combine(DataDir, "ChromeProfile");

    public static string LogFile { get; } = Path.Combine(DataDir, "sessionlens.log");
    public static string LogBackupFile { get; } = Path.Combine(DataDir, "sessionlens.old.log");

    public static void EnsureCreated() => Directory.CreateDirectory(DataDir);
}
