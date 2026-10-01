using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SessionRecorder.Services.Recording;

/// <summary>
/// Removes credentials and session secrets from what goes into network.har, so a recording
/// of a login does not carry the password or a usable session. Names are kept and only
/// values are replaced, so the flow stays readable.
/// </summary>
internal static class SecretRedactor
{
    private const string MaskedValue = "********";
    private const string RemovedHeaderValue = "[removed]";

    // Keep in sync with SECRET_NAME in Assets/recorder.js.
    private static readonly Regex SecretName = new(
        @"password|passwd|secret|token|api[-_]?key|credential|session[-_]?id|^(pwd|pass|otp|sysparm_ck)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> SecretHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "proxy-authorization", "cookie", "set-cookie",
    };

    // Headers that carry a URL can leak a secret query parameter of the previous page.
    private static readonly HashSet<string> UrlHeaders = new(StringComparer.OrdinalIgnoreCase) { "referer", "location" };

    public static HarNameValue RedactHeader(HarNameValue header)
    {
        if (SecretHeaders.Contains(header.Name) || SecretName.IsMatch(header.Name)) return header with { Value = RemovedHeaderValue };
        if (UrlHeaders.Contains(header.Name)) return header with { Value = RedactUrl(header.Value) };
        return header;
    }

    public static string RedactUrl(string url)
    {
        var queryStart = url.IndexOf('?');
        return queryStart < 0 ? url : url[..(queryStart + 1)] + RedactPairs(url[(queryStart + 1)..]);
    }

    /// <summary>Form and JSON bodies get their secret-named fields masked; other formats pass through.</summary>
    public static string RedactBody(string text, string mimeType)
    {
        if (mimeType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) return RedactPairs(text);
        if (mimeType.Contains("json", StringComparison.OrdinalIgnoreCase)) return RedactJson(text);
        return text;
    }

    private static string RedactPairs(string pairs) => string.Join('&', pairs.Split('&').Select(RedactPair));

    private static string RedactPair(string pair)
    {
        var separator = pair.IndexOf('=');
        if (separator < 0) return pair;
        var name = Uri.UnescapeDataString(pair[..separator].Replace('+', ' '));
        return SecretName.IsMatch(name) ? pair[..(separator + 1)] + MaskedValue : pair;
    }

    // A body labelled JSON that does not parse has no field names to judge, so it is kept as sent.
    private static string RedactJson(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return text;
        }
        return MaskSecretMembers(root) ? root!.ToJsonString() : text;
    }

    /// <summary>Returns whether anything was masked, so untouched bodies keep their original formatting.</summary>
    private static bool MaskSecretMembers(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject members:
                var masked = false;
                foreach (var name in members.Select(member => member.Key).ToList())
                {
                    if (SecretName.IsMatch(name) && members[name] is not null)
                    {
                        members[name] = MaskedValue;
                        masked = true;
                    }
                    else
                    {
                        masked |= MaskSecretMembers(members[name]);
                    }
                }
                return masked;
            case JsonArray elements:
                return elements.Aggregate(false, (masked, element) => MaskSecretMembers(element) | masked);
            default:
                return false;
        }
    }
}
