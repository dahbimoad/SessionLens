using Microsoft.Playwright;

namespace SessionRecorder.Services.Recording;

/// <summary>Numbers tabs 1, 2, 3... in the order they are first seen.</summary>
internal sealed class TabRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<IPage, int> _ids = [];

    public int IdOf(IPage page)
    {
        lock (_gate)
        {
            if (!_ids.TryGetValue(page, out var id))
            {
                id = _ids.Count + 1;
                _ids[page] = id;
            }
            return id;
        }
    }

    public int Count
    {
        get { lock (_gate) return _ids.Count; }
    }
}
