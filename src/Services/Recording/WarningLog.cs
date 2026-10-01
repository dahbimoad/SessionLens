namespace SessionRecorder.Services.Recording;

internal sealed record RecordingWarning(DateTimeOffset At, string Message);

/// <summary>Everything that could not be captured, so the zip says so instead of silently lacking it.</summary>
internal sealed class WarningLog
{
    private readonly Lock _gate = new();
    private readonly List<RecordingWarning> _warnings = [];

    public void Add(string message)
    {
        lock (_gate) _warnings.Add(new RecordingWarning(DateTimeOffset.Now, message));
    }

    public int Count
    {
        get { lock (_gate) return _warnings.Count; }
    }

    public IReadOnlyList<RecordingWarning> Snapshot()
    {
        lock (_gate) return [.. _warnings];
    }
}
