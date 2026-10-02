using Microsoft.Playwright;

namespace SessionLens.Services.Recording;

internal sealed record ConsoleEntry(
    DateTimeOffset At, int? Tab, int AfterStep, string Source, string Level, string Text, string? Location);

/// <summary>Console calls and uncaught page errors from every tab and frame.</summary>
internal sealed class ConsoleLog(StepLog steps, TabRegistry tabs)
{
    private readonly Lock _gate = new();
    private readonly List<ConsoleEntry> _entries = [];

    public void Attach(IBrowserContext context)
    {
        context.Console += (_, message) => Add("console", message.Type, message.Text, message.Location, message.Page);
        context.WebError += (_, error) => Add("page-error", "error", error.Error, location: null, error.Page);
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public IReadOnlyList<ConsoleEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }

    // Worker messages have no page, so no tab number.
    private void Add(string source, string level, string text, string? location, IPage? page)
    {
        var entry = new ConsoleEntry(
            DateTimeOffset.Now, page is null ? null : tabs.IdOf(page), steps.LastIndex, source, level, text, location);
        lock (_gate) _entries.Add(entry);
    }
}
