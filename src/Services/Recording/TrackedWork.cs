namespace SessionRecorder.Services.Recording;

/// <summary>
/// Playwright raises its events synchronously, but reading a body or taking a screenshot
/// is async. Every such follow-up is tracked here so the archive is written only after
/// they finish, and so a failure becomes a warning in the recording instead of vanishing.
/// </summary>
internal sealed class TrackedWork
{
    private readonly Lock _gate = new();
    private readonly HashSet<Task> _pending = [];
    private readonly List<Exception> _failures = [];

    public void Track(Task task)
    {
        lock (_gate) _pending.Add(task);
        task.ContinueWith(Complete, TaskScheduler.Default);
    }

    /// <summary>
    /// Waits until nothing is pending (work started meanwhile is waited for too) or the
    /// timeout passes. Returns one message per failure and per task still running.
    /// </summary>
    public async Task<IReadOnlyList<string>> DrainAsync(TimeSpan timeout)
    {
        var deadline = Task.Delay(timeout);
        while (Snapshot() is { Length: > 0 } pending)
        {
            if (await Task.WhenAny(Task.WhenAll(pending), deadline) == deadline) break;
        }

        lock (_gate)
        {
            var problems = _failures.Select(ex => $"A capture failed: {ex.GetType().Name}: {ex.Message}").ToList();
            if (_pending.Count > 0) problems.Add($"{_pending.Count} capture(s) were still running at save time and are missing.");
            return problems;
        }
    }

    private Task[] Snapshot()
    {
        lock (_gate) return [.. _pending];
    }

    private void Complete(Task task)
    {
        lock (_gate)
        {
            _pending.Remove(task);
            if (task.Exception is not null) _failures.Add(task.Exception.GetBaseException());
        }
    }
}
