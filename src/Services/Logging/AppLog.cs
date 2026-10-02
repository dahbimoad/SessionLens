using System.IO;
using System.Text;
using SessionLens.Services.Platform;

namespace SessionLens.Services.Logging;

/// <summary>
/// Small append-only log. Deliberately dumb: no framework, no sinks, no async.
///
/// PRIVACY CONTRACT: never pass page content, URLs typed by the user, form values,
/// headers or request/response bodies to this class. Those belong in the recording zip
/// only. Log state, counts, durations and exception types.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private static readonly Lock Gate = new();

    public static void Info(string message) => Write("INF", message);

    public static void Error(string message, Exception? ex = null)
    {
        // Type, HResult and stack only. An exception Message can echo a URL or page content.
        var detail = ex is null ? message : $"{message} | {ex.GetType().FullName} hresult=0x{ex.HResult:X8} | {FirstFrames(ex)}";
        Write("ERR", detail);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();
                Rotate();
                var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [T{Environment.CurrentManagedThreadId}] {Flatten(message)}{Environment.NewLine}";
                File.AppendAllText(AppPaths.LogFile, line, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: a read-only or locked log file is not worth a crash.
        }
    }

    private static void Rotate()
    {
        var info = new FileInfo(AppPaths.LogFile);
        if (!info.Exists || info.Length < MaxBytes) return;

        File.Delete(AppPaths.LogBackupFile);
        File.Move(AppPaths.LogFile, AppPaths.LogBackupFile);
    }

    /// <summary>Keeps one line per entry so a stray newline cannot forge log records.</summary>
    private static string Flatten(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ');
        return flat.Length > 800 ? flat[..800] + "..." : flat;
    }

    private static string FirstFrames(Exception ex)
    {
        var stack = ex.StackTrace;
        if (string.IsNullOrEmpty(stack)) return "no-stack";

        var frames = stack.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(4);
        return string.Join(" <= ", frames.Select(f => f.Trim()));
    }
}
