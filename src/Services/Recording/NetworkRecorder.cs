using Microsoft.Playwright;

namespace SessionLens.Services.Recording;

internal sealed record CapturedResponse(int Status, string StatusText, string HttpVersion, IReadOnlyList<Header> Headers);

/// <summary>One request as it was seen. Filled in two passes: at start, then when it finishes or fails.</summary>
internal sealed class NetworkRecord(IRequest request, DateTimeOffset startedAt, int afterStep, int? tab)
{
    public IRequest Request { get; } = request;
    public DateTimeOffset StartedAt { get; } = startedAt;
    public int AfterStep { get; } = afterStep;
    public int? Tab { get; } = tab;
    public bool Completed { get; set; }
    public string? Failure { get; set; }
    public RequestTimingResult? Timing { get; set; }
    public IReadOnlyList<Header> RequestHeaders { get; set; } = [];
    public CapturedResponse? Response { get; set; }
    public byte[]? Body { get; set; }
    public string? BodyError { get; set; }
    public string? CaptureError { get; set; }
}

/// <summary>
/// Records every request of every tab and frame from Playwright's context events.
/// Playwright's own RecordHarPath is not used: that HAR is written only when the context
/// is closed through the API, and it is lost when the user closes the Chrome window.
/// </summary>
internal sealed class NetworkRecorder(StepLog steps, TabRegistry tabs, TrackedWork work)
{
    private const int MaxBodyBytes = 20 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly List<NetworkRecord> _records = [];
    private readonly Dictionary<IRequest, NetworkRecord> _inFlight = [];

    public void Attach(IBrowserContext context)
    {
        context.Request += (_, request) => OnRequest(request);
        context.RequestFinished += (_, request) => work.Track(CompleteAsync(request, failure: null));
        context.RequestFailed += (_, request) => work.Track(CompleteAsync(request, request.Failure));
    }

    public int Count
    {
        get { lock (_gate) return _records.Count; }
    }

    public IReadOnlyList<NetworkRecord> Snapshot()
    {
        lock (_gate) return [.. _records];
    }

    private void OnRequest(IRequest request)
    {
        var record = new NetworkRecord(request, DateTimeOffset.Now, steps.LastIndex, TabOf(request));
        lock (_gate)
        {
            _records.Add(record);
            _inFlight[request] = record;
        }
    }

    private async Task CompleteAsync(IRequest request, string? failure)
    {
        NetworkRecord? record;
        lock (_gate)
        {
            if (!_inFlight.Remove(request, out record)) return;
        }

        record.Completed = true;
        record.Failure = failure;
        record.Timing = request.Timing;
        try
        {
            await CaptureExchangeAsync(record);
        }
        catch (PlaywrightException ex)
        {
            record.CaptureError = ex.Message;
        }
    }

    private static async Task CaptureExchangeAsync(NetworkRecord record)
    {
        record.RequestHeaders = await record.Request.HeadersArrayAsync();
        var response = await record.Request.ResponseAsync();
        if (response is null) return;

        record.Response = new CapturedResponse(
            response.Status, response.StatusText, await response.HttpVersionAsync(), await response.HeadersArrayAsync());
        await CaptureBodyAsync(record, response);
    }

    private static async Task CaptureBodyAsync(NetworkRecord record, IResponse response)
    {
        if (response.Status is >= 300 and < 400) return;
        try
        {
            var body = await response.BodyAsync();
            if (body.Length > MaxBodyBytes) record.BodyError = $"Body of {body.Length} bytes is over the {MaxBodyBytes}-byte limit and was not kept.";
            else record.Body = body;
        }
        catch (PlaywrightException ex)
        {
            record.BodyError = ex.Message;
        }
    }

    // Service-worker requests have no frame, and Playwright throws when asked for it.
    private int? TabOf(IRequest request)
    {
        try
        {
            return tabs.IdOf(request.Frame.Page);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}
