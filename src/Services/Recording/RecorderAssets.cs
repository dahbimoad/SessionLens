using System.IO;
using System.Reflection;

namespace SessionLens.Services.Recording;

/// <summary>Files shipped inside the assembly (see the EmbeddedResource items in the csproj).</summary>
internal static class RecorderAssets
{
    /// <summary>The script injected into every frame of the recorded browser.</summary>
    public static string PageScript { get; } = Read("SessionLens.recorder.js");

    /// <summary>The README.md written into every zip.</summary>
    public static string RecordingGuide { get; } = Read("SessionLens.recording-guide.md");

    private static string Read(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
