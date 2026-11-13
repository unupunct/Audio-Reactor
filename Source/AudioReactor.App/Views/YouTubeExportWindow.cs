using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioReactor.App.Services;
using AudioReactor.Core.Audio;
using AudioReactor.Core.Data;
using AudioReactor.Core.Models;
using AudioReactor.Core.Recording;
using AudioReactor.Core.Video;
using Microsoft.Win32;
using Serilog;

namespace AudioReactor.App.Views;

/// <summary>
/// Make a YouTube-ready video: pick the sound source (auto = whatever is playing), auto-match or pick a template, edit the text and export.
/// The preview is the live Visualizer Studio page behind this window. Export captures in real time, so a 4-minute song takes about 4 minutes.
/// </summary>
public sealed class YouTubeExportWindow : Window
{
    private readonly VisualizerView _viz;
    private readonly ListBox _templates = new() { Height = 250 };
    private readonly TextBox _title = new(), _subtitle = new(), _footer = new(), _folder = new();
    private readonly ComboBox _layout = new(), _quality = new(), _titleMode = new(), _duration = new();
    private readonly CheckBox _auto = new() { Content = "Auto-match template to the music", IsChecked = true }, _progress = new() { Content = "Show progress bar", IsChecked = true }, _autoText = new() { Content = "Auto-fill title and artist from the song", IsChecked = true };
    private readonly Slider _size = new() { Minimum = 0.6, Maximum = 1.6, Value = 1, Width = 200 };
    private readonly TextBlock _source = Ui.Text("", 13), _match = Ui.Text("", 12, true), _status = Ui.Text("", 12), _time = Ui.Text("", 12, true);
    private readonly ProgressBar _bar = new() { Height = 8, Minimum = 0, Maximum = 1, Foreground = Ui.B("AccentBrush"), Background = Ui.B("PanelHover"), BorderThickness = new Thickness(0), Margin = new Thickness(0, 6, 0, 6) };
    private readonly Button _export, _cancel, _open;
    private Track? _track; private bool _isRadio, _exporting, _cancelRequested, _loading = true;
    private CancellationTokenSource? _profileCts; private string? _lastOutput;
    private int _restart;

    public YouTubeExportWindow(VisualizerView viz)
    {
        _viz = viz;
        Title = "Export for YouTube"; Width = 860; Height = 860; MinHeight = 640; Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B("PanelSolid"); Foreground = Ui.B("TextBrush");
        _templates.ItemsSource = VideoTemplates.All; _templates.DisplayMemberPath = "Name"; _templates.SelectedIndex = 0;
        _templates.SelectionChanged += (_, _) => { if (!_loading) OnTemplateChanged(); };
        _layout.ItemsSource = Enum.GetValues<TextLayout>(); _layout.SelectedIndex = 0;
        _quality.ItemsSource = YouTubeProfiles.All; _quality.SelectedIndex = 0;
        _titleMode.ItemsSource = new[] { "Show title the whole video", "Show title for the first 10 seconds" }; _titleMode.SelectedIndex = 1;
        _duration.ItemsSource = new[] { "Full song", "First 30 seconds (test)", "First 60 seconds", "First 3 minutes", "First 10 minutes" }; _duration.SelectedIndex = 0;
        _folder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Audio Reactor", "YouTube");
        foreach (var t in new TextBox[] { _title, _subtitle, _footer }) t.TextChanged += (_, _) => ApplyOverlay();
        _layout.SelectionChanged += (_, _) => ApplyOverlay(); _titleMode.SelectionChanged += (_, _) => ApplyOverlay(); _size.ValueChanged += (_, _) => ApplyOverlay();
        _progress.Click += (_, _) => ApplyOverlay();
        _auto.Click += (_, _) => { if (_auto.IsChecked == true) _ = AutoMatchAsync(); };

        var choose = Ui.Button("Choose a song…", (_, _) => ChooseFile());
        var useNow = Ui.Button("Use what's playing", (_, _) => UseCurrent(true));
        var browse = Ui.Button("Browse…", (_, _) => { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) _folder.Text = d.FolderName; });
        var folderRow = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); browse.Margin = new Thickness(8, 0, 0, 0); folderRow.Children.Add(browse); folderRow.Children.Add(_folder);
        _export = Ui.Button("▶  Export video", async (_, _) => await ExportAsync(), true); _cancel = Ui.Button("Stop and save", (_, _) => _cancelRequested = true); _cancel.IsEnabled = false;
        _open = Ui.Button("Show in folder", (_, _) => { if (_lastOutput != null) Process.Start("explorer.exe", $"/select,\"{_lastOutput}\""); }); _open.IsEnabled = false;

        StackPanel Field(string label, UIElement c) { var p = new StackPanel { Margin = new Thickness(0, 0, 0, 8) }; p.Children.Add(Ui.Text(label, 12, true)); p.Children.Add(c); return p; }

        var left = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        left.Children.Add(Ui.Text("1 · Sound source", 15, false, true));
        left.Children.Add(_source); _source.Margin = new Thickness(0, 6, 0, 8);
        left.Children.Add(Ui.Row(useNow, choose)); ((StackPanel)left.Children[^1]).Margin = new Thickness(0, 0, 0, 14);
        left.Children.Add(Ui.Text("2 · Template", 15, false, true));
        left.Children.Add(_auto); left.Children.Add(_match); _match.Margin = new Thickness(0, 0, 0, 6);
        left.Children.Add(_templates);
        var desc = Ui.Text("", 12, true); desc.Margin = new Thickness(0, 6, 0, 0); left.Children.Add(desc);
        _templates.SelectionChanged += (_, _) => desc.Text = (_templates.SelectedItem as VideoTemplate)?.Description ?? "";

        var right = new StackPanel();
        right.Children.Add(Ui.Text("3 · Text", 15, false, true)); ((TextBlock)right.Children[0]).Margin = new Thickness(0, 0, 0, 6);
        right.Children.Add(_autoText);
        right.Children.Add(Field("Title", _title)); right.Children.Add(Field("Subtitle (artist, album…)", _subtitle)); right.Children.Add(Field("Footer (channel, link, #hashtags…)", _footer));
        right.Children.Add(Ui.Row(Field("Layout", _layout), new TextBlock { Width = 12 }, Field("Text size", _size)));
        right.Children.Add(Field("Title timing", _titleMode)); right.Children.Add(_progress);
        right.Children.Add(Ui.Text("4 · Export", 15, false, true)); ((TextBlock)right.Children[^1]).Margin = new Thickness(0, 12, 0, 6);
        right.Children.Add(Field("YouTube quality", _quality)); right.Children.Add(Field("Length", _duration)); right.Children.Add(Field("Save to", folderRow));

        var cols = new Grid(); cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cols.Children.Add(left); Grid.SetColumn(right, 1); cols.Children.Add(right);

        var foot = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        foot.Children.Add(_bar); foot.Children.Add(_time); foot.Children.Add(_status); foot.Children.Add(Ui.Row(_export, _cancel, _open)); ((StackPanel)foot.Children[^1]).Margin = new Thickness(0, 8, 0, 6);
        foot.Children.Add(Ui.Text("The preview is the Visualizer Studio page behind this window. Export records in real time with the exact look you see, then FFmpeg encodes an H.264 + AAC MP4 (YouTube's recommended settings), a 1280×720 thumbnail and a description text file. Keep Visualizer Studio open while exporting.", 11, true));

        var root = new DockPanel { Margin = new Thickness(22) }; DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
        root.Children.Add(new ScrollViewer { Content = cols, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Closing += (_, e) => { if (_exporting) { e.Cancel = true; _status.Text = "Stop the export first (or wait for it to finish)."; return; } _profileCts?.Cancel(); _viz.SendConfig(new { overlay = new { on = false } }); };
        _loading = false;
        Loaded += (_, _) => { UseCurrent(false); OnTemplateChanged(applyOnly: true); };
        if (!RecordingService.FfmpegAvailable) _status.Text = "FFmpeg not found: video export is unavailable.";
    }

    private VideoTemplate Tpl => (VideoTemplate)_templates.SelectedItem;

    private void OnTemplateChanged(bool applyOnly = false)
    {
        var t = Tpl; if (t == null) return;
        _loading = true; _layout.SelectedItem = t.Layout; _size.Value = t.TextScale; _loading = false;
        var p = t.Palette; _viz.SetPreset(t.Preset);
        _viz.SendConfig(new { palette = new { primary = p.Primary, secondary = p.Secondary, background = p.Background, accent = p.Accent, particle = p.Particle, glow = p.Glow } });
        ApplyOverlay();
    }

    private void ApplyOverlay(bool restart = true)
    {
        if (_loading) return;
        _viz.SendConfig(new
        {
            overlay = new
            {
                on = true, title = _title.Text, subtitle = _subtitle.Text, footer = _footer.Text, layout = _layout.SelectedItem?.ToString() ?? "LowerThird", scale = _size.Value,
                mode = _titleMode.SelectedIndex == 1 ? "first10" : "always", progress = _progress.IsChecked == true, restart = restart ? ++_restart : (int?)null
            }
        });
    }

    /// <summary>Developer self-test: drives the same code path as the buttons.</summary>
    public async Task<(string? Output, string Match, string Template)> AutomateAsync(string songPath, string title, string subtitle, string footer, int durationIndex, string folder)
    {
        var t = LibraryScanner.ReadTrack(songPath)!; App.Db.UpsertTrack(t); SetTrack(t, true);
        for (int i = 0; i < 200 && _match.Text.StartsWith("Listening"); i++) await Task.Delay(100);
        _title.Text = title; _subtitle.Text = subtitle; _footer.Text = footer; _duration.SelectedIndex = durationIndex; _folder.Text = folder;
        string match = _match.Text, tpl = Tpl.Name;
        await ExportAsync();
        return (_lastOutput, match, tpl);
    }

    // ---------- sound source ----------
    private void UseCurrent(bool announce)
    {
        var p = App.Player;
        if (p.CurrentTrack != null) SetTrack(p.CurrentTrack, false);
        else if (p.IsRadio && p.CurrentStation != null) { _track = null; _isRadio = true; _source.Text = "● Internet radio: " + p.CurrentStation.Name + " (live; the video is limited to the length you choose)"; if (_autoText.IsChecked == true) { _title.Text = p.CurrentStation.Name; _subtitle.Text = p.CurrentStation.Genre; } _duration.SelectedIndex = 3; }
        else { _source.Text = announce ? "Nothing is playing. Start a song, or choose one." : "No song selected yet. Play a song, or choose one."; }
    }

    private void ChooseFile()
    {
        var dlg = new OpenFileDialog { Filter = "Audio files|*.mp3;*.wav;*.flac;*.ogg;*.aac;*.m4a;*.wma;*.opus|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        var t = LibraryScanner.ReadTrack(dlg.FileName);
        if (t == null) { _status.Text = "That file could not be read."; return; }
        App.Db.UpsertTrack(t); SetTrack(t, true);
    }

    private void SetTrack(Track t, bool chosen)
    {
        _track = t; _isRadio = false; _source.Text = $"● {t.Display}  ·  {t.DurationText}" + (chosen ? "" : "  (currently playing)");
        if (_autoText.IsChecked == true) { _title.Text = t.Title; _subtitle.Text = string.Join(" · ", new[] { t.Artist, t.Album }.Where(x => !string.IsNullOrWhiteSpace(x))); }
        if (_auto.IsChecked == true) _ = AutoMatchAsync();
    }

    /// <summary>Analyses the song faster than real time and selects the template whose mood fits best.</summary>
    private async Task AutoMatchAsync()
    {
        if (_track == null || !File.Exists(_track.Path)) { _match.Text = _isRadio ? "Auto-match works with local songs." : ""; return; }
        _profileCts?.Cancel(); var cts = _profileCts = new CancellationTokenSource(); var path = _track.Path;
        _match.Text = "Listening to the song to pick a template…";
        try
        {
            var prof = await Task.Run(() => AudioProfiler.Analyze(path, 45, 5, cts.Token), cts.Token);
            var (t, score) = TemplateMatcher.Suggest(prof);
            if (cts.IsCancellationRequested) return;
            _match.Text = $"Auto-matched: {t.Name}  ({prof.Describe()})";
            _templates.SelectedItem = t; _templates.ScrollIntoView(t);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warning(ex, "Auto-match failed"); _match.Text = "Could not analyse this song; pick a template yourself."; }
    }

    // ---------- export ----------
    private async Task ExportAsync()
    {
        if (_exporting) return;
        var p = App.Player; var eng = p.Engine;
        if (!RecordingService.FfmpegAvailable) { _status.Text = "FFmpeg was not found."; return; }
        if (!_viz.WebReady) { _status.Text = "Open Visualizer Studio first (the preview must be visible)."; return; }
        if (_track == null && !_isRadio) { _status.Text = "Choose a song first."; return; }
        var prof = (YouTubeProfile)_quality.SelectedItem; int limit = _duration.SelectedIndex switch { 1 => 30, 2 => 60, 3 => 180, 4 => 600, _ => 0 };
        if (_isRadio && limit == 0) limit = 180;
        double total = limit > 0 ? limit : (_track?.DurationSec ?? 0);
        if (limit > 0 && _track != null) total = Math.Min(limit, _track.DurationSec);
        var rec = App.Recording; bool shuffle = p.Shuffle; string repeat = p.Repeat; _exporting = true; _cancelRequested = false; _lastOutput = null;
        _export.IsEnabled = false; _cancel.IsEnabled = true; _open.IsEnabled = false; _status.Text = "Preparing…"; _bar.Value = 0;
        try
        {
            ApplyOverlay(restart: true);
            if (!_isRadio)
            {   // Load the song paused at 0:00 so the recording starts with the first beat.
                p.Shuffle = false; p.Repeat = "Off";
                p.PlayQueue(new[] { _track! }, 0); eng.Pause(); eng.Position = TimeSpan.Zero; await Task.Delay(300);
            }
            await rec.StartAsync(RecordMode.VideoAudio, RecordQuality.P1080, prof.Fps, _folder.Text, _viz, prof, onStarted: () => { if (!_isRadio) eng.Play(); });
            var sw = Stopwatch.StartNew(); _status.Text = "Recording… (this runs in real time)";
            while (true)
            {
                await Task.Delay(250);
                double el = rec.Elapsed.TotalSeconds;
                if (total > 0) _bar.Value = Math.Min(1, el / total);
                _time.Text = $"{TimeSpan.FromSeconds(el):mm\\:ss}" + (total > 0 ? $" / {TimeSpan.FromSeconds(total):mm\\:ss}" : "") + $"   ·   ~{rec.EstimatedBytes / 1048576.0:0} MB";
                if (_cancelRequested) break;
                if (limit > 0 && el >= limit) break;
                if (!_isRadio && eng.State == PlaybackState2.Stopped && el > 2) { await Task.Delay(800); break; }
                if (!rec.IsRecording) break;
            }
            _cancel.IsEnabled = false; _status.Text = "Encoding for YouTube…";
            var output = await rec.StopAsync();
            if (output == null) { _status.Text = "Export failed. See the log in %AppData%\\AudioReactor\\logs."; return; }
            if (_isRadio || limit > 0) eng.Stop();
            await FinishAsync(output);
        }
        catch (Exception ex) { Log.Error(ex, "YouTube export failed"); _status.Text = "Export failed: " + ex.Message; }
        finally { p.Shuffle = shuffle; p.Repeat = repeat; _exporting = false; _export.IsEnabled = true; _cancel.IsEnabled = false; _bar.Value = _lastOutput != null ? 1 : 0; }
    }

    private async Task FinishAsync(string mp4)
    {
        var ff = Ffmpeg.Find()!; string baseName = Path.Combine(Path.GetDirectoryName(mp4)!, Path.GetFileNameWithoutExtension(mp4));
        // Thumbnail: a frame a few seconds in, when the title card is fully visible.
        var jpg = baseName + "_thumbnail.jpg";
        await Ffmpeg.RunAsync(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-ss", "3", "-i", mp4, "-frames:v", "1", "-vf", "scale=1280:720:force_original_aspect_ratio=decrease,pad=1280:720:(ow-iw)/2:(oh-ih)/2", "-q:v", "2", jpg });
        // Description text, ready to paste into YouTube Studio.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.IsNullOrWhiteSpace(_subtitle.Text) ? _title.Text : $"{_title.Text} - {_subtitle.Text}"); sb.AppendLine();
        if (_track != null) { if (!string.IsNullOrWhiteSpace(_track.Artist)) sb.AppendLine("Artist: " + _track.Artist); if (!string.IsNullOrWhiteSpace(_track.Album)) sb.AppendLine("Album: " + _track.Album); if (!string.IsNullOrWhiteSpace(_track.Genre)) sb.AppendLine("Genre: " + _track.Genre); sb.AppendLine(); }
        if (!string.IsNullOrWhiteSpace(_footer.Text)) { sb.AppendLine(_footer.Text); sb.AppendLine(); }
        sb.AppendLine("Visuals made with Audio Reactor — template: " + Tpl.Name); sb.AppendLine("#music #visualizer #audioreactive");
        sb.AppendLine(); sb.AppendLine("Only upload music you own or have permission to use. YouTube may claim or block videos that use copyrighted recordings.");
        await File.WriteAllTextAsync(baseName + "_description.txt", sb.ToString());
        _lastOutput = mp4; _open.IsEnabled = true;
        var mb = new FileInfo(mp4).Length / 1048576.0;
        _status.Text = $"Done: {Path.GetFileName(mp4)} ({mb:0.#} MB), thumbnail and description saved next to it.";
        App.Notifications.Recording("YouTube video ready: " + Path.GetFileName(mp4));
    }
}
