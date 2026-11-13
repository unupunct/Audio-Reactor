using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AudioReactor.App.Views;
using AudioReactor.Core.Audio;
using AudioReactor.Core.Data;

namespace AudioReactor.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _ui = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, UIElement> _pages = new();
    private bool _seeking, _loadingUi;
    public VisualizerPage Visualizer { get; }
    public Services.NotificationService Notifications { get; private set; } = null!;
    private MiniPlayerWindow? _mini;
    private bool _quit;
    private Window? _full;

    public MainWindow()
    {
        InitializeComponent();
        Visualizer = new VisualizerPage();
        var p = App.Player;
        _loadingUi = true;
        VolSlider.Value = App.Settings.Volume;
        var devs = new List<(string Id, string Name)> { ("", "Default device") };
        try { devs.AddRange(AudioEngine.GetOutputDevices()); } catch { }
        DeviceBox.ItemsSource = devs.Select(d => new DeviceItem(d.Id, d.Name)).ToList();
        DeviceBox.SelectedIndex = Math.Max(0, devs.FindIndex(d => d.Id == (App.Settings.OutputDeviceId ?? "")));
        _loadingUi = false;

        p.PropertyChanged += OnPlayerChanged;
        Notifications = new Services.NotificationService(ShowFromTray, () => p.TogglePlay(), () => p.Next(), () => { _quit = true; Close(); });
        Notifications.InAppToast += ShowToast;
        p.Notify += Notifications.FromPlayer;
        p.Engine.DeviceAdded += (_, name) => Dispatcher.BeginInvoke(() => Notifications.Device("New audio device detected: " + name));
        p.Engine.DeviceRemoved += _ => Dispatcher.BeginInvoke(RefreshDevices);
        p.Engine.DeviceRecovered += () => Dispatcher.BeginInvoke(() => { Notifications.Device("Output device disconnected — playback moved to the default device."); RefreshDevices(); });
        p.Engine.PlaybackSpeed = (float)App.Settings.PlaybackSpeed;
        try { p.Engine.StartDeviceWatching(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Device watcher unavailable"); }
        _ui.Tick += (_, _) => UpdateProgress();
        _ui.Start();
        _toastTimer.Tick += (_, _) => { Toast.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
        Navigate("Home");
        RefreshPlayerUi();
        Drop += OnDrop;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11) Fullscreen_Click(this, new RoutedEventArgs());
            else if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox) { p.TogglePlay(); e.Handled = true; }
        };
        Closing += (_, _) => { if (!App.Player.IsRadio) App.Settings.LastPositionSec = App.Player.Engine.Position.TotalSeconds; App.Settings.Save(); Notifications.Dispose(); _mini?.Close(); };
        Loaded += (_, _) => HandleCommandLine();
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && App.Settings.MinimizeToTray) Hide(); };
    }

    private sealed record DeviceItem(string Id, string Name);

    private void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }

    private void RefreshDevices()
    {
        var devs = new List<(string Id, string Name)> { ("", "Default device") };
        try { devs.AddRange(AudioEngine.GetOutputDevices()); } catch { }
        _loadingUi = true;
        DeviceBox.ItemsSource = devs.Select(d => new DeviceItem(d.Id, d.Name)).ToList();
        int i = devs.FindIndex(d => d.Id == (App.Settings.OutputDeviceId ?? "")); if (i < 0) { App.Settings.OutputDeviceId = null; i = 0; }
        DeviceBox.SelectedIndex = i; _loadingUi = false;
    }

    /// <summary>Switch to the floating mini player (audio keeps playing) and back.</summary>
    private void Mini_Click(object s, RoutedEventArgs e)
    {
        if (_mini != null) return;
        _mini = new MiniPlayerWindow();
        _mini.RestoreRequested += () => { _mini?.Close(); };
        _mini.Closed += (_, _) => { _mini = null; if (!_quit) ShowFromTray(); };
        Hide(); _mini.Show();
    }

    /// <summary>Supports "Open with" (audio files as arguments) and <c>--page "Visualizer Studio"</c> / <c>--preset "Name"</c>.</summary>
    private void HandleCommandLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        string? page = null, preset = null; (int secs, string folder)? selfTest = null; (string song, string folder)? ytTest = null; bool seamlessTest = false; var files = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--page" && i + 1 < args.Count) page = args[++i];
            else if (args[i] == "--preset" && i + 1 < args.Count) preset = args[++i];
            else if (args[i] == "--selftest-youtube" && i + 2 < args.Count) { ytTest = (args[i + 1], args[i + 2]); i += 2; }
            else if (args[i] == "--selftest-seamless") { seamlessTest = true; }
            else if (args[i] == "--selftest-record" && i + 2 < args.Count) { selfTest = (int.Parse(args[i + 1]), args[i + 2]); i += 2; }
            else if (File.Exists(args[i]) || Directory.Exists(args[i])) files.Add(args[i]);
        }
        if (files.Count == 0 && App.Settings.RestoreLastTrack && App.Settings.LastTrackId > 0)
        {
            var all = App.Db.GetTracks(); int i = all.FindIndex(t => t.Id == App.Settings.LastTrackId);
            if (i >= 0) App.Player.PrepareQueue(all, i, TimeSpan.FromSeconds(App.Settings.LastPositionSec));
        }
        if (files.Count > 0)
        {
            LibraryScanner.Import(App.Db, files);
            var tracks = App.Db.GetTracks();
            var first = files.FirstOrDefault(File.Exists);
            var idx = first == null ? 0 : Math.Max(0, tracks.FindIndex(t => string.Equals(t.Path, first, StringComparison.OrdinalIgnoreCase)));
            if (tracks.Count > 0) App.Player.PlayQueue(tracks, idx);
        }
        if (args.Contains("--show-youtube")) { Navigate("Visualizer Studio"); new YouTubeExportWindow(Visualizer.Viz) { Owner = this }.Show(); }
        if (args.Contains("--show-editor")) { Navigate("Visualizer Studio"); new EffectEditorWindow(Visualizer.Viz) { Owner = this }.Show(); }
        if (args.Contains("--mini")) Mini_Click(this, new RoutedEventArgs());
        if (preset != null) Visualizer.Viz.SetPreset(preset);
        if (page != null) Navigate(page);
        if (seamlessTest) _ = RunSeamlessSelfTest();
        if (ytTest != null) _ = RunYouTubeSelfTest(ytTest.Value.song, ytTest.Value.folder);
        if (selfTest != null) _ = RunRecordSelfTest(selfTest.Value.secs, selfTest.Value.folder);
    }

    /// <summary>Developer self-test: plays a 3-track queue with gapless + crossfade and logs when each track becomes current.</summary>
    private async Task RunSeamlessSelfTest()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ar_seamless_" + Guid.NewGuid().ToString("N")[..8]); System.IO.Directory.CreateDirectory(dir);
        try
        {
            var tracks = new List<Core.Models.Track>();
            for (int i = 0; i < 3; i++)
            {
                var path = System.IO.Path.Combine(dir, $"t{i + 1}.wav");
                using (var w = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(44100, 16, 2)))
                {
                    var pcm = new float[44100 * 4 * 2]; for (int k = 0; k < 44100 * 4; k++) { float v = (float)(0.25 * Math.Sin(2 * Math.PI * (300 + i * 200) * k / 44100.0)); pcm[k * 2] = v; pcm[k * 2 + 1] = v; }
                    w.WriteSamples(pcm, 0, pcm.Length);
                }
                var t = Core.Data.LibraryScanner.ReadTrack(path)!; t.Title = $"Test {i + 1}"; App.Db.UpsertTrack(t); tracks.Add(t);
            }
            App.Settings.Gapless = true; App.Settings.CrossfadeSeconds = 1; App.Settings.Shuffle = false; App.Settings.Repeat = "Off";
            App.Player.Shuffle = false; App.Player.Repeat = "Off"; App.Player.Engine.Volume = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew(); var log = new List<string>();
            App.Player.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Services.PlayerService.QueueIndex)) log.Add($"{sw.Elapsed.TotalSeconds:0.0}s idx={App.Player.QueueIndex} '{App.Player.NowTitle}'"); };
            App.Player.PlayQueue(tracks, 0); sw.Restart();
            for (int i = 0; i < 200 && (App.Player.IsPlaying || sw.Elapsed.TotalSeconds < 1); i++) await Task.Delay(100);
            var plays = tracks.Select(t => App.Db.GetTracks().First(x => x.Path == t.Path).PlayCount).ToList();
            Serilog.Log.Information("SELFTEST seamless: {Log} | finished={Fin:0.0}s playing={Playing} playcounts={Plays}", string.Join("; ", log), sw.Elapsed.TotalSeconds, App.Player.IsPlaying, string.Join(",", plays));
            foreach (var t in tracks) App.Db.DeleteTrack(t.Id);
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "SELFTEST seamless failed"); }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
        Application.Current.Shutdown();
    }

    private async Task RunYouTubeSelfTest(string song, string folder)
    {
        try
        {
            Navigate("Visualizer Studio"); await Task.Delay(5000);
            var w = new YouTubeExportWindow(Visualizer.Viz) { Owner = this }; w.Show();
            var (output, match, tpl) = await w.AutomateAsync(song, "Neon Pulse", "Audio Reactor Demo · 120 BPM", "Made with Audio Reactor #visualizer", 1, folder);
            Serilog.Log.Information("SELFTEST youtube: output={Out} match={Match} template={Tpl}", output, match, tpl);
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "SELFTEST youtube failed"); }
        Application.Current.Shutdown();
    }

    /// <summary>Developer self-test: records the visualizer + audio for N seconds, logs the result and exits.</summary>
    private async Task RunRecordSelfTest(int secs, string folder)
    {
        try
        {
            Navigate("Visualizer Studio"); await Task.Delay(5000);
            var rec = App.Recording; string? done = null; string? fail = null; rec.Completed += p => done = p; rec.Failed += m => fail = m;
            await rec.StartAsync(Services.RecordMode.VideoAudio, Core.Recording.RecordQuality.P720, 30, folder, Visualizer.Viz);
            await Task.Delay(secs * 1000);
            await rec.StopAsync();
            Serilog.Log.Information("SELFTEST record result: {Path} fail={Fail}", done, fail);
        }
        catch (Exception ex) { Serilog.Log.Error(ex, "SELFTEST record failed"); }
        Application.Current.Shutdown();
    }

    // ---------- navigation ----------
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && Page == null) return;
        var name = ((RadioButton)sender).Content.ToString()!;
        Navigate(name);
    }

    public void Navigate(string name)
    {
        if (!_pages.TryGetValue(name, out var page))
        {
            page = name switch
            {
                "Home" => new HomeView(this),
                "Music Library" => new LibraryView(),
                "Playlists" => new PlaylistsView(),
                "Internet Radio" => new RadioView(),
                "Visualizer Studio" => Visualizer,
                "RGB Lighting" => new LightingView(),
                "Settings" => new SettingsView(this),
                _ => new HomeView(this)
            };
            _pages[name] = page;
        }
        Page.Content = page;
        ModeText.Text = name switch { "Internet Radio" => "RADIO MODE", "Visualizer Studio" => "VISUALIZER MODE", _ => "LIBRARY MODE" };
        foreach (RadioButton rb in Nav.Children) if ((string)rb.Content == name && rb.IsChecked != true) rb.IsChecked = true;
    }

    // ---------- player bar ----------
    private void OnPlayerChanged(object? s, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(RefreshPlayerUi);

    private void RefreshPlayerUi()
    {
        var p = App.Player;
        TitleText.Text = p.NowTitle; ArtistText.Text = string.IsNullOrEmpty(p.NowAlbum) || p.IsRadio ? p.NowArtist : $"{p.NowArtist} · {p.NowAlbum}";
        PlayBtn.Content = p.IsPlaying ? "" : "";
        ShuffleBtn.Foreground = p.Shuffle ? (System.Windows.Media.Brush)FindResource("AccentBrush") : (System.Windows.Media.Brush)FindResource("TextBrush");
        RepeatBtn.Content = p.Repeat == "One" ? "" : "";
        RepeatBtn.Foreground = p.Repeat != "Off" ? (System.Windows.Media.Brush)FindResource("AccentBrush") : (System.Windows.Media.Brush)FindResource("TextBrush");
        SourceText.Text = p.IsRadio ? $"● {p.RadioStatus}" : p.CurrentTrack != null ? "● Local file" : "";
        Art.Source = null;
        if (p.Artwork is { Length: > 0 })
        {
            try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(p.Artwork); bi.DecodePixelWidth = 128; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); Art.Source = bi; } catch { }
        }
        Seek.IsEnabled = !p.IsRadio;
    }

    private void UpdateProgress()
    {
        var e = App.Player.Engine;
        if (App.Player.IsRadio) { PosText.Text = "LIVE"; DurText.Text = ""; Seek.Value = 0; return; }
        var dur = e.Duration.TotalSeconds;
        if (dur <= 0) { PosText.Text = "0:00"; DurText.Text = "0:00"; return; }
        if (!_seeking) Seek.Value = e.Position.TotalSeconds / dur;
        PosText.Text = Fmt(e.Position); DurText.Text = Fmt(e.Duration);
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    private void Seek_Down(object s, MouseButtonEventArgs e) => _seeking = true;
    private void Seek_Up(object s, MouseButtonEventArgs e)
    {
        _seeking = false;
        var eng = App.Player.Engine;
        Dispatcher.BeginInvoke(() => eng.Position = TimeSpan.FromSeconds(Seek.Value * eng.Duration.TotalSeconds), DispatcherPriority.Background);
    }

    private void Play_Click(object s, RoutedEventArgs e) => App.Player.TogglePlay();
    private void Stop_Click(object s, RoutedEventArgs e) => App.Player.Stop();
    private void Next_Click(object s, RoutedEventArgs e) => App.Player.Next();
    private void Prev_Click(object s, RoutedEventArgs e) => App.Player.Previous();
    private void Shuffle_Click(object s, RoutedEventArgs e) => App.Player.ToggleShuffle();
    private void Repeat_Click(object s, RoutedEventArgs e) => App.Player.CycleRepeat();
    private void Vol_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loadingUi) App.Player.SetVolume(e.NewValue); }

    private void Device_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loadingUi || DeviceBox.SelectedItem is not DeviceItem d) return;
        App.Settings.OutputDeviceId = string.IsNullOrEmpty(d.Id) ? null : d.Id;
        try { App.Player.Engine.SetDevice(App.Settings.OutputDeviceId); } catch (Exception ex) { ShowToast("Device error: " + ex.Message); }
    }

    // ---------- chrome ----------
    private void TopBar_MouseDown(object s, MouseButtonEventArgs e) { if (e.ClickCount == 2) Max_Click(s, e); else DragMove(); }
    private void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object s, RoutedEventArgs e) => Close();
    private void Settings_Click(object s, RoutedEventArgs e) => Navigate("Settings");

    private void Fullscreen_Click(object s, RoutedEventArgs e)
    {
        if (_full != null) { _full.Close(); return; }
        Navigate("Visualizer Studio");
        Visualizer.EnterFullscreen(w => _full = w, () => _full = null);
    }

    public void ShowToast(string msg)
    {
        Dispatcher.BeginInvoke(() => { ToastText.Text = msg; Toast.Visibility = Visibility.Visible; _toastTimer.Stop(); _toastTimer.Start(); });
    }

    private void OnDrop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        var n = LibraryScanner.Import(App.Db, paths);
        ShowToast($"Imported {n} track(s)");
        _pages.Remove("Music Library");
        if (n > 0) Navigate("Music Library");
    }
}
