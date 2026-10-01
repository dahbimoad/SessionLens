using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using SessionRecorder.Services.Logging;
using SessionRecorder.Services.Platform;

namespace SessionRecorder.Services.Recording;

internal sealed record RecordingStats(DateTimeOffset StartedAt, int Steps, int Requests, int ConsoleEntries, int Warnings);

/// <summary>
/// One recording: a Chrome window driven by Playwright, from Record until the zip is written.
/// It ends either through <see cref="StopAsync"/> or when the user closes Chrome; both
/// paths go through the context's Close event, which writes the archive exactly once.
/// </summary>
internal sealed class RecordingSession : IAsyncDisposable
{
    private const string ReportBinding = "__sessionRecorderReport";
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);
    private static readonly string RecorderVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";

    private readonly IPlaywright _playwright;
    private readonly IBrowserContext _context;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly TrackedWork _work = new();
    private readonly TabRegistry _tabs = new();
    private readonly WarningLog _warnings = new();
    private readonly StepLog _steps = new();
    private readonly NetworkRecorder _network;
    private readonly ConsoleLog _console;
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _chromeVersion;
    private int _finishing;

    private RecordingSession(IPlaywright playwright, IBrowserContext context)
    {
        _playwright = playwright;
        _context = context;
        _chromeVersion = context.Browser?.Version ?? "unknown";
        _network = new NetworkRecorder(_steps, _tabs, _work);
        _console = new ConsoleLog(_steps, _tabs);
    }

    /// <summary>Completes with the zip path once the recording is saved, whichever way it ended.</summary>
    public Task<string> Completion => _completion.Task;

    public RecordingStats Stats => new(_startedAt, _steps.LastIndex, _network.Count, _console.Count, _warnings.Count);

    /// <param name="startUrl">Page to open first; empty opens a blank tab.</param>
    public static async Task<RecordingSession> StartAsync(string startUrl)
    {
        var playwright = await Playwright.CreateAsync();
        IBrowserContext? context = null;
        try
        {
            context = await playwright.Chromium.LaunchPersistentContextAsync(AppPaths.ChromeProfileDir, new()
            {
                Channel = "chrome",
                Headless = false,
                ViewportSize = ViewportSize.NoViewport,
            });
            var session = new RecordingSession(playwright, context);
            await session.AttachAsync(startUrl);
            AppLog.Info($"Recording started. chrome={session._chromeVersion}");
            return session;
        }
        catch
        {
            if (context is not null) await context.CloseAsync();
            playwright.Dispose();
            throw;
        }
    }

    /// <summary>Waits for in-flight captures, then closes Chrome, which triggers the save.</summary>
    public async Task StopAsync()
    {
        foreach (var problem in await _work.DrainAsync(DrainTimeout)) _warnings.Add(problem);
        await _context.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _context.DisposeAsync();
        _playwright.Dispose();
    }

    private async Task AttachAsync(string startUrl)
    {
        _context.Close += (_, _) => _ = FinishAsync();
        _network.Attach(_context);
        _console.Attach(_context);
        await _context.ExposeBindingAsync(ReportBinding, (BindingSource source, string report) => _work.Track(OnReportAsync(source, report)));
        await _context.AddInitScriptAsync(RecorderAssets.PageScript);

        var page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        if (startUrl.Length > 0) await page.GotoAsync(WithScheme(startUrl));
    }

    private async Task OnReportAsync(BindingSource source, string reportJson)
    {
        var report = JsonNode.Parse(reportJson)!.AsObject();
        var stepKey = report["stepKey"]!.GetValue<string>();
        switch (report["type"]!.GetValue<string>())
        {
            case "step":
                var step = report["step"]!.AsObject();
                _steps.Add(stepKey, step, report["domBefore"]?.GetValue<string>(), _tabs.IdOf(source.Page), source.Frame.Url);
                break;
            case "settled":
                var index = _steps.Settle(stepKey, report["domAfter"]!.GetValue<string>());
                await CaptureScreenshotAsync(index, source.Page);
                break;
            default:
                throw new InvalidOperationException($"Unknown recorder report type '{report["type"]}'.");
        }
    }

    private async Task CaptureScreenshotAsync(int stepIndex, IPage page)
    {
        try
        {
            var jpeg = await page.ScreenshotAsync(new() { Type = ScreenshotType.Jpeg, Quality = 75, Timeout = 5000 });
            _steps.AddScreenshot(stepIndex, jpeg);
        }
        catch (PlaywrightException ex)
        {
            _warnings.Add($"Step {stepIndex}: no screenshot ({ex.Message})");
        }
    }

    // Runs once, from the context's Close event. Never throws: the outcome goes to Completion.
    private async Task FinishAsync()
    {
        if (Interlocked.Exchange(ref _finishing, 1) == 1) return;
        try
        {
            foreach (var problem in await _work.DrainAsync(DrainTimeout)) _warnings.Add(problem);
            var path = RecordingArchive.Write(CollectContents());
            AppLog.Info($"Recording saved. steps={_steps.LastIndex} requests={_network.Count} warnings={_warnings.Count}");
            _completion.SetResult(path);
        }
        catch (Exception ex)
        {
            AppLog.Error("Saving the recording failed.", ex);
            _completion.SetException(ex);
        }
    }

    private RecordingContents CollectContents()
    {
        var steps = _steps.Snapshot();
        var network = _network.Snapshot();
        var console = _console.Snapshot();
        var counts = new RecordingCounts(steps.Steps.Count, _tabs.Count, network.Count, console.Count);
        var summary = new RecordingSummary(_startedAt, DateTimeOffset.Now, _chromeVersion, RecorderVersion, counts, _warnings.Snapshot());
        return new RecordingContents(summary, steps.Steps, HarBuilder.Build(network, RecorderVersion), console, steps.Files);
    }

    private static string WithScheme(string url) =>
        url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
}
