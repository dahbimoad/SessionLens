using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Playwright;
using SessionLens.Services.Logging;
using SessionLens.Services.Recording;

namespace SessionLens.Views;

public partial class RecorderWindow : Window
{
    private const string RecordingHint = "Chrome is recording. Use your site, then press Stop here or just close Chrome.";

    private readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _readyHint;
    private RecordingSession? _session;
    private string? _savedPath;
    private bool _starting;
    private bool _closeWhenSaved;

    public RecorderWindow()
    {
        InitializeComponent();
        _readyHint = IntroText.Text;
        _statsTimer.Tick += (_, _) => RenderStats();
    }

    private async void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_session is null) await RecordAsync();
        else await StopAsync();
    }

    private async Task RecordAsync()
    {
        ShowMessage(null);
        ResultPanel.Visibility = Visibility.Collapsed;
        ShowBusy("OPENING CHROME", "Opening Chrome...");
        _starting = true;
        try
        {
            _session = await RecordingSession.StartAsync(StartUrlBox.Text.Trim());
        }
        catch (Exception ex)
        {
            // UI boundary: a missing Chrome, a locked profile or a bad URL all land here.
            AppLog.Error("Starting a recording failed.", ex);
            ShowReady();
            ShowMessage($"Chrome could not be started: {FirstLine(ex.Message)}");
            return;
        }
        finally
        {
            _starting = false;
        }

        ShowRecording();
        await AwaitSaveAsync(_session);
    }

    private async Task StopAsync()
    {
        ShowBusy("SAVING", "Saving...");
        try
        {
            await _session!.StopAsync();
        }
        catch (PlaywrightException ex)
        {
            // Chrome was already closing on its own; the save still reports through Completion.
            AppLog.Error("Closing Chrome from Stop failed.", ex);
        }
    }

    private async Task AwaitSaveAsync(RecordingSession session)
    {
        try
        {
            ShowSaved(await session.Completion);
        }
        catch (Exception ex)
        {
            // UI boundary: whatever broke the save is logged and shown, and the window stays usable.
            AppLog.Error("The recording could not be saved.", ex);
            ShowReady();
            ShowMessage($"The recording could not be saved: {ex.Message}");
        }
        finally
        {
            _statsTimer.Stop();
            _session = null;
            await session.DisposeAsync();
            if (_closeWhenSaved) Close();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing mid-recording saves first, then closes (see AwaitSaveAsync).
        if (_starting || _session is not null)
        {
            e.Cancel = true;
            if (_session is not null && !_closeWhenSaved) StopThenClose();
        }
        base.OnClosing(e);
    }

    private async void StopThenClose()
    {
        _closeWhenSaved = true;
        await StopAsync();
    }

    private void ShowReady()
    {
        SetStatus("READY", "Well", "InkSoft");
        IntroText.Text = _readyHint;
        StartUrlPanel.Visibility = Visibility.Visible;
        StatsPanel.Visibility = Visibility.Collapsed;
        SetToggle("Record", isStop: false, enabled: true);
    }

    private void ShowRecording()
    {
        SetStatus("RECORDING", "AccentSoft", "Accent");
        IntroText.Text = RecordingHint;
        StartUrlPanel.Visibility = Visibility.Collapsed;
        StatsPanel.Visibility = Visibility.Visible;
        SetToggle("Stop and save", isStop: true, enabled: true);
        RenderStats();
        _statsTimer.Start();
    }

    private void ShowBusy(string status, string buttonText)
    {
        SetStatus(status, "Well", "InkSoft");
        ToggleText.Text = buttonText;
        ToggleButton.IsEnabled = false;
    }

    private void ShowSaved(string path)
    {
        ShowReady();
        SetStatus("SAVED", "PositiveSoft", "Positive");
        _savedPath = path;
        ResultText.Text = Path.GetFileName(path);
        ResultPanel.Visibility = Visibility.Visible;
    }

    private void SetStatus(string text, string backgroundKey, string foregroundKey)
    {
        StatusText.Text = text;
        StatusChip.Background = (Brush)FindResource(backgroundKey);
        StatusText.Foreground = (Brush)FindResource(foregroundKey);
    }

    private void SetToggle(string text, bool isStop, bool enabled)
    {
        ToggleText.Text = text;
        RecordIcon.Visibility = isStop ? Visibility.Collapsed : Visibility.Visible;
        StopIcon.Visibility = isStop ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton.IsEnabled = enabled;
    }

    private void ShowMessage(string? text)
    {
        MessageText.Text = text ?? "";
        MessageText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderStats()
    {
        if (_session?.Stats is not { } stats) return;
        var elapsed = DateTimeOffset.Now - stats.StartedAt;
        ElapsedText.Text = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";
        StepsText.Text = stats.Steps.ToString();
        RequestsText.Text = stats.Requests.ToString();
        ConsoleText.Text = stats.ConsoleEntries.ToString();
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (_savedPath is null) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_savedPath}\"") { UseShellExecute = true });
    }

    private void OnStartUrlChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        StartUrlPlaceholder.Visibility = StartUrlBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();
}
