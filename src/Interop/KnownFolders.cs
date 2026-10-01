using System.Runtime.InteropServices;

namespace SessionRecorder.Interop;

/// <summary>Environment.SpecialFolder has no Downloads entry, so it is read from the shell.</summary>
public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string Downloads => SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero);

    // PreserveSig=false turns a failing HRESULT into an exception; the marshaller frees the
    // returned string with CoTaskMemFree as the API requires.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
    private static extern string SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken);
}
