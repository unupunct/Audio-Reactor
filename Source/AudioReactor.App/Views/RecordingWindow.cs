using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioReactor.App.Services;
using AudioReactor.Core.Recording;
using Microsoft.Win32;

namespace AudioReactor.App.Views;

/// <summary>Recording controls: mode, resolution, frame rate, folder, start/pause/stop, duration and estimated size.</summary>
public sealed class RecordingWindow : Window
{
    private readonly RecordingService _rec; private readonly VisualizerView _viz;
    private readonly ComboBox _mode = new(), _quality = new(), _fps = new();
    private readonly TextBox _folder = new();
    private readonly TextBlock _time = new() { FontSize = 30, FontWeight = FontWeights.Bold }, _info = Ui.Text("", 12, true), _msg = Ui.Text("", 12);
    private readonly Button _start, _pause, _stop;
    private readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public RecordingWindow(RecordingService rec, VisualizerView viz)
    {
        _rec = rec; _viz = viz;
        Title = "Record"; Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B("PanelSolid"); Foreground = Ui.B("TextBrush"); _time.Foreground = Ui.B("TextBrush");
        _mode.ItemsSource = new[] { "Video + audio (MP4)", "Video only (MP4)", "Audio only (WAV)", "Audio only (MP3)" }; _mode.SelectedIndex = 0;
        _quality.ItemsSource = new[] { "720p", "1080p", "1440p", "4K (needs a strong GPU)" }; _quality.SelectedIndex = 1;
        _fps.ItemsSource = new[] { 24, 30, 60 }; _fps.SelectedItem = 30;
        _folder.Text = App.Settings.RecordingFolder; _folder.Margin = new Thickness(0, 0, 8, 0);
        var browse = Ui.Button("Browse…", (_, _) => { var d = new OpenFolderDialog(); if (d.ShowDialog() == true) _folder.Text = d.FolderName; });
        _start = Ui.Button("● Start", async (_, _) => await StartAsync(), true); _pause = Ui.Button("Pause", (_, _) => TogglePause()); _stop = Ui.Button("■ Stop", async (_, _) => await StopAsync());

        StackPanel L(string t, UIElement c) { var p = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; p.Children.Add(Ui.Text(t, 12, true)); p.Children.Add(c); return p; }
        var folderRow = new DockPanel(); DockPanel.SetDock(browse, Dock.Right); folderRow.Children.Add(browse); folderRow.Children.Add(_folder);
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(L("What to record", _mode)); root.Children.Add(L("Resolution", _quality)); root.Children.Add(L("Frame rate", _fps)); root.Children.Add(L("Save to", folderRow));
        root.Children.Add(_time); root.Children.Add(_info); root.Children.Add(_msg); _msg.Margin = new Thickness(0, 4, 0, 10);
        root.Children.Add(Ui.Row(_start, _pause, _stop));
        root.Children.Add(Ui.Text("Stay on Visualizer Studio while recording video. FFmpeg encodes after you press Stop; the audio engine is never blocked.", 11, true));
        ((TextBlock)root.Children[^1]).Margin = new Thickness(0, 10, 0, 0);
        Content = root; UpdateButtons();
        _t.Tick += (_, _) => Tick(); _t.Start();
        Closing += (_, e) => { if (_rec.IsRecording || _rec.IsEncoding) { e.Cancel = true; _msg.Text = "Stop the recording before closing."; } _t.Stop(); };
        if (!RecordingService.FfmpegAvailable) _msg.Text = "FFmpeg not found: only WAV recording is available.";
    }

    private RecordMode Mode => (RecordMode)_mode.SelectedIndex;
    private RecordQuality Q => (RecordQuality)_quality.SelectedIndex;

    private async Task StartAsync()
    {
        App.Settings.RecordingFolder = _folder.Text; _msg.Text = "";
        try { await _rec.StartAsync(Mode, Q, (int)_fps.SelectedItem, _folder.Text, _viz); }
        catch (Exception ex) { _msg.Text = ex.Message; }
        UpdateButtons();
    }

    private void TogglePause() { if (_rec.IsPaused) _rec.Resume(); else _rec.Pause(); UpdateButtons(); }

    private async Task StopAsync()
    {
        _stop.IsEnabled = false; _pause.IsEnabled = false;
        var path = await _rec.StopAsync();
        if (path != null) { _msg.Text = "Saved: " + path; App.Notifications.Recording("Recording completed: " + Path.GetFileName(path)); }
        UpdateButtons();
    }

    private void Tick()
    {
        _time.Text = _rec.Elapsed.ToString(@"hh\:mm\:ss");
        _info.Text = _rec.IsRecording || _rec.IsEncoding ? $"Estimated size: {_rec.EstimatedBytes / 1048576.0:0.0} MB  ·  {_rec.Status}" : $"Estimated size per minute: {Est() / 1048576.0:0.0} MB";
        if (_rec.IsEncoding) _msg.Text = _rec.Status; UpdateButtons();
    }

    private long Est() => Mode switch { RecordMode.AudioWav => 48000 * 4 * 60, RecordMode.AudioMp3 => 256000 / 8 * 60, RecordMode.VideoOnly => RecordProfiles.EstimateBytes(Q, TimeSpan.FromMinutes(1)) - 192000 / 8 * 60, _ => RecordProfiles.EstimateBytes(Q, TimeSpan.FromMinutes(1)) };

    private void UpdateButtons()
    {
        bool busy = _rec.IsRecording || _rec.IsEncoding;
        _start.IsEnabled = !busy; _mode.IsEnabled = _quality.IsEnabled = _fps.IsEnabled = !busy;
        _pause.IsEnabled = _rec.IsRecording; _pause.Content = _rec.IsPaused ? "Resume" : "Pause"; _stop.IsEnabled = _rec.IsRecording;
    }
}
