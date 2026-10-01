using System.Text;
using System.Text.Json.Nodes;

namespace SessionRecorder.Services.Recording;

internal sealed record StepLogSnapshot(JsonArray Steps, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>
/// The ordered user actions reported by recorder.js, plus the snapshot and screenshot
/// files each one points to. Steps keep the JSON shape the page script sent, enriched with
/// the index, tab, frame and file paths.
/// </summary>
internal sealed class StepLog
{
    private const string UnsettledNote =
        "The page navigated away or the recording stopped before the settled snapshot. " +
        "The next page-load step of this tab shows what followed.";

    private readonly Lock _gate = new();
    private readonly List<JsonObject> _steps = [];
    private readonly Dictionary<string, JsonObject> _unsettled = [];
    private readonly Dictionary<string, byte[]> _files = [];

    /// <summary>0 until the first step, so requests and console entries can say which step they followed.</summary>
    public int LastIndex
    {
        get { lock (_gate) return _steps.Count; }
    }

    public void Add(string stepKey, JsonObject reportedStep, string? domBefore, int tab, string frameUrl)
    {
        lock (_gate)
        {
            var index = _steps.Count + 1;
            var step = new JsonObject { ["index"] = index, ["tab"] = tab, ["frameUrl"] = SecretRedactor.RedactUrl(frameUrl) };
            foreach (var (name, field) in reportedStep) step[name] = field?.DeepClone();
            step["url"] = SecretRedactor.RedactUrl(reportedStep["url"]!.GetValue<string>());
            step["at"] = DateTimeOffset.FromUnixTimeMilliseconds((long)reportedStep["at"]!.GetValue<double>()).ToString("O");

            var files = new JsonObject();
            if (domBefore is not null) files["domBefore"] = AddFile($"dom/{StepName(index)}-before.html", Encoding.UTF8.GetBytes(domBefore));
            step["files"] = files;

            _steps.Add(step);
            _unsettled[stepKey] = step;
        }
    }

    /// <summary>Stores the settled snapshot and returns the step's index, for its screenshot.</summary>
    public int Settle(string stepKey, string domAfter)
    {
        lock (_gate)
        {
            if (!_unsettled.Remove(stepKey, out var step))
                throw new InvalidOperationException($"Settled snapshot for unknown step {stepKey}.");

            var index = step["index"]!.GetValue<int>();
            step["files"]!["domAfter"] = AddFile($"dom/{StepName(index)}-after.html", Encoding.UTF8.GetBytes(domAfter));
            return index;
        }
    }

    public void AddScreenshot(int index, byte[] jpeg)
    {
        lock (_gate) _steps[index - 1]["files"]!["screenshot"] = AddFile($"screenshots/{StepName(index)}.jpg", jpeg);
    }

    public StepLogSnapshot Snapshot()
    {
        lock (_gate)
        {
            var steps = new JsonArray();
            foreach (var step in _steps)
            {
                var copy = step.DeepClone().AsObject();
                if (_unsettled.ContainsValue(step)) copy["note"] = UnsettledNote;
                steps.Add(copy);
            }
            return new StepLogSnapshot(steps, new Dictionary<string, byte[]>(_files));
        }
    }

    private string AddFile(string path, byte[] content)
    {
        _files[path] = content;
        return path;
    }

    private static string StepName(int index) => $"step-{index:D4}";
}
