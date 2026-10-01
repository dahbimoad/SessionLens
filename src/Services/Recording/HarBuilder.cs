using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Playwright;

namespace SessionRecorder.Services.Recording;

// HAR 1.2 (http://www.softwareishard.com/blog/har-12-spec/). Fields starting with "_" are
// custom fields the spec allows; they tie each request to its tab and to the step it followed.
internal sealed record Har(HarLog Log);
internal sealed record HarLog(string Version, HarCreator Creator, IReadOnlyList<object> Pages, IReadOnlyList<HarEntry> Entries);
internal sealed record HarCreator(string Name, string Version);
internal sealed record HarNameValue(string Name, string Value);
internal sealed record HarPostData(string MimeType, string Text, [property: JsonPropertyName("_encoding")] string? Encoding)
{
    [JsonIgnore] public bool Redacted { get; init; }
}

internal sealed record HarContent(long Size, string MimeType, string? Text, string? Encoding)
{
    [JsonIgnore] public bool Redacted { get; init; }
}
internal sealed record HarTimings(double Blocked, double Dns, double Connect, double Ssl, double Send, double Wait, double Receive);

internal sealed record HarRequest(
    string Method, string Url, string HttpVersion, IReadOnlyList<object> Cookies, IReadOnlyList<HarNameValue> Headers,
    IReadOnlyList<HarNameValue> QueryString, HarPostData? PostData, long HeadersSize, long BodySize)
{
    [JsonIgnore] public bool Redacted { get; init; }
}

internal sealed record HarResponse(
    int Status, string StatusText, string HttpVersion, IReadOnlyList<object> Cookies, IReadOnlyList<HarNameValue> Headers,
    HarContent Content, [property: JsonPropertyName("redirectURL")] string RedirectUrl, long HeadersSize, long BodySize)
{
    [JsonIgnore] public bool Redacted { get; init; }
}

internal sealed record HarEntry(
    string StartedDateTime,
    double Time,
    HarRequest Request,
    HarResponse Response,
    object Cache,
    HarTimings Timings,
    [property: JsonPropertyName("_tab")] int? Tab,
    [property: JsonPropertyName("_afterStep")] int AfterStep,
    [property: JsonPropertyName("_resourceType")] string ResourceType,
    [property: JsonPropertyName("_redacted")] bool? Redacted,
    [property: JsonPropertyName("_incomplete")] bool? Incomplete,
    [property: JsonPropertyName("_error")] string? Error,
    [property: JsonPropertyName("_bodyError")] string? BodyError,
    [property: JsonPropertyName("_captureError")] string? CaptureError);

internal static class HarBuilder
{
    private static readonly string[] TextualMimeMarkers =
        ["text/", "json", "xml", "javascript", "ecmascript", "x-www-form-urlencoded", "graphql", "svg", "html", "css"];

    public static Har Build(IEnumerable<NetworkRecord> records, string recorderVersion) =>
        new(new HarLog("1.2", new HarCreator("Session Recorder", recorderVersion), [], [.. records.Select(ToEntry)]));

    private static HarEntry ToEntry(NetworkRecord record)
    {
        var timings = ToTimings(record.Timing);
        var total = new[] { timings.Blocked, timings.Dns, timings.Connect, timings.Send, timings.Wait, timings.Receive }
            .Where(part => part > 0).Sum();
        var started = record.Timing is { StartTime: > 0 } timing
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)timing.StartTime)
            : record.StartedAt;

        var request = ToRequest(record);
        var response = ToResponse(record);
        return new HarEntry(
            started.ToString("O"), total, request, response, new object(), timings,
            record.Tab, record.AfterStep, record.Request.ResourceType,
            request.Redacted || response.Redacted ? true : null,
            record.Completed ? null : true, record.Failure, record.BodyError, record.CaptureError);
    }

    private static HarRequest ToRequest(NetworkRecord record)
    {
        var rawHeaders = ToNameValues(record.RequestHeaders);
        var headers = RedactHeaders(rawHeaders);
        var url = SecretRedactor.RedactUrl(record.Request.Url);
        var body = record.Request.PostDataBuffer;
        var postData = body is null ? null : ToPostData(body, HeaderValue(rawHeaders, "content-type") ?? "");
        return new HarRequest(
            record.Request.Method, url, record.Response?.HttpVersion ?? "", [], headers,
            ToQueryString(url), postData, -1, body?.Length ?? 0)
        {
            Redacted = !headers.SequenceEqual(rawHeaders) || url != record.Request.Url || postData?.Redacted == true,
        };
    }

    private static HarResponse ToResponse(NetworkRecord record)
    {
        if (record.Response is not { } response)
            return new HarResponse(0, "", "", [], [], new HarContent(0, "", null, null), "", -1, -1);

        var rawHeaders = ToNameValues(response.Headers);
        var headers = RedactHeaders(rawHeaders);
        var content = ToContent(record.Body, HeaderValue(rawHeaders, "content-type") ?? "");
        return new HarResponse(
            response.Status, response.StatusText, response.HttpVersion, [], headers,
            content, HeaderValue(rawHeaders, "location") ?? "", -1, record.Body?.Length ?? -1)
        {
            Redacted = !headers.SequenceEqual(rawHeaders) || content.Redacted,
        };
    }

    private static HarContent ToContent(byte[]? body, string mimeType)
    {
        if (body is null) return new HarContent(0, mimeType, null, null);
        if (!IsTextual(mimeType)) return new HarContent(body.Length, mimeType, Convert.ToBase64String(body), "base64");

        var text = Encoding.UTF8.GetString(body);
        var safeText = SecretRedactor.RedactBody(text, mimeType);
        return new HarContent(body.Length, mimeType, safeText, null) { Redacted = safeText != text };
    }

    private static HarPostData ToPostData(byte[] body, string mimeType)
    {
        if (!IsTextual(mimeType) && mimeType.Length > 0) return new HarPostData(mimeType, Convert.ToBase64String(body), "base64");

        var text = Encoding.UTF8.GetString(body);
        var safeText = SecretRedactor.RedactBody(text, mimeType);
        return new HarPostData(mimeType, safeText, null) { Redacted = safeText != text };
    }

    private static List<HarNameValue> RedactHeaders(List<HarNameValue> headers) =>
        [.. headers.Select(SecretRedactor.RedactHeader)];

    // Playwright gives each phase as milliseconds relative to StartTime, -1 when it did not happen.
    private static HarTimings ToTimings(RequestTimingResult? timing)
    {
        if (timing is null) return new HarTimings(-1, -1, -1, -1, 0, 0, 0);
        return new HarTimings(
            Blocked: -1,
            Dns: Span(timing.DomainLookupStart, timing.DomainLookupEnd),
            Connect: Span(timing.ConnectStart, timing.ConnectEnd),
            Ssl: Span(timing.SecureConnectionStart, timing.ConnectEnd),
            Send: 0,
            Wait: Math.Max(0, Span(timing.RequestStart, timing.ResponseStart)),
            Receive: Math.Max(0, Span(timing.ResponseStart, timing.ResponseEnd)));
    }

    private static double Span(float start, float end) => start >= 0 && end >= start ? end - start : -1;

    private static bool IsTextual(string mimeType) =>
        TextualMimeMarkers.Any(marker => mimeType.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static List<HarNameValue> ToNameValues(IEnumerable<Header> headers) =>
        [.. headers.Select(header => new HarNameValue(header.Name, header.Value))];

    private static string? HeaderValue(IEnumerable<HarNameValue> headers, string name) =>
        headers.FirstOrDefault(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    // Split by hand: NameValueCollection would merge repeated parameters into one.
    private static List<HarNameValue> ToQueryString(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Query.Length <= 1) return [];
        return [.. uri.Query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries).Select(ToQueryParameter)];
    }

    private static HarNameValue ToQueryParameter(string pair)
    {
        var separator = pair.IndexOf('=');
        var name = separator < 0 ? pair : pair[..separator];
        var parameterValue = separator < 0 ? "" : pair[(separator + 1)..];
        return new HarNameValue(Unescape(name), Unescape(parameterValue));
    }

    private static string Unescape(string part) => Uri.UnescapeDataString(part.Replace('+', ' '));
}
