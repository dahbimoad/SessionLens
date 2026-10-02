using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SessionLens.Interop;

namespace SessionLens.Services.Recording;

internal sealed record RecordingSummary(
    DateTimeOffset StartedAt, DateTimeOffset StoppedAt, string ChromeVersion, string RecorderVersion,
    RecordingCounts Counts, IReadOnlyList<RecordingWarning> Warnings);

internal sealed record RecordingCounts(int Steps, int Tabs, int Requests, int ConsoleEntries);

internal sealed record RecordingContents(
    RecordingSummary Summary, JsonArray Steps, Har Network, IReadOnlyList<ConsoleEntry> Console,
    IReadOnlyDictionary<string, byte[]> Files);

/// <summary>Writes one recording as a zip in the user's Downloads folder.</summary>
internal static class RecordingArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keeps HTML and quotes readable in the JSON; the files are read, never embedded in a page.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Returns the path of the written zip.</summary>
    public static string Write(RecordingContents contents)
    {
        var path = UniquePath(KnownFolders.Downloads, $"sessionlens_{contents.Summary.StartedAt:yyyy-MM-dd_HH-mm-ss}");
        // Written under a temporary name so a failure never leaves a truncated zip that looks complete.
        var partialPath = path + ".partial";
        try
        {
            WriteZip(partialPath, contents);
            File.Move(partialPath, path);
        }
        finally
        {
            File.Delete(partialPath);
        }
        return path;
    }

    private static void WriteZip(string path, RecordingContents contents)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        AddText(zip, "README.md", RecorderAssets.RecordingGuide);
        AddJson(zip, "session.json", contents.Summary);
        AddJson(zip, "steps.json", contents.Steps);
        AddJson(zip, "network.har", contents.Network);
        AddJson(zip, "console.json", contents.Console);
        foreach (var (name, bytes) in contents.Files) AddBytes(zip, name, bytes);
    }

    private static void AddJson<T>(ZipArchive zip, string name, T content) =>
        AddText(zip, name, JsonSerializer.Serialize(content, JsonOptions));

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open());
        writer.Write(text);
    }

    // JPEGs are already compressed; deflating them again only costs time.
    private static void AddBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var level = name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
        using var stream = zip.CreateEntry(name, level).Open();
        stream.Write(bytes);
    }

    private static string UniquePath(string folder, string baseName)
    {
        var path = Path.Combine(folder, baseName + ".zip");
        for (var copy = 2; File.Exists(path); copy++) path = Path.Combine(folder, $"{baseName} ({copy}).zip");
        return path;
    }
}
