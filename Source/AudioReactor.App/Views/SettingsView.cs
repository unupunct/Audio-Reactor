using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioReactor.Core.Audio;

namespace AudioReactor.App.Views;

public sealed class SettingsView : UserControl
{
    public SettingsView(MainWindow main)
    {
        var s = App.Settings;
        var root = new StackPanel { Margin = new Thickness(36, 28, 36, 20), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(Ui.Title("Settings"));

        UIElement Section(string title, params UIElement[] kids)
        {
            var p = new StackPanel(); p.Children.Add(Ui.Text(title, 16, false, true)); ((TextBlock)p.Children[0]).Margin = new Thickness(0, 0, 0, 8);
            foreach (var k in kids) p.Children.Add(k);
            var c = Ui.Card(p); c.Margin = new Thickness(0, 0, 0, 16); return c;
        }
        UIElement Field(string label, UIElement ctl) { var g = Ui.Row(Ui.Text(label, 13, true), ctl); ((TextBlock)((StackPanel)g).Children[0]).Width = 190; ((StackPanel)g).Margin = new Thickness(0, 4, 0, 4); return g; }

        // Appearance
        var accents = new[] { "#00E5FF", "#A64DFF", "#FF2BD6", "#3DFFA0", "#2E8BFF", "#FF7A00", "#FFE066" };
        var sw = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var a in accents)
        {
            var b = new Button { Width = 30, Height = 30, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(a)), ToolTip = a };
            b.Click += (_, _) => { App.ApplyAccent(a); };
            sw.Children.Add(b);
        }
        root.Children.Add(Section("Appearance", Field("Accent colour", sw)));

        // Audio
        var devs = new List<(string Id, string Name)> { ("", "Windows default") }; try { devs.AddRange(AudioEngine.GetOutputDevices()); } catch { }
        var dev = new ComboBox { Width = 320, ItemsSource = devs.Select(d => d.Name).ToList(), SelectedIndex = Math.Max(0, devs.FindIndex(d => d.Id == (s.OutputDeviceId ?? ""))) };
        dev.SelectionChanged += (_, _) => { if (dev.SelectedIndex < 0) return; var id = devs[dev.SelectedIndex].Id; s.OutputDeviceId = id == "" ? null : id; try { App.Player.Engine.SetDevice(s.OutputDeviceId); } catch (Exception ex) { main.ShowToast(ex.Message); } };
        var lat = new ComboBox { Width = 120, ItemsSource = new[] { 50, 100, 200, 400 }, SelectedItem = s.LatencyMs };
        lat.SelectionChanged += (_, _) => { s.LatencyMs = (int)lat.SelectedItem; App.Player.Engine.LatencyMs = s.LatencyMs; };
        var buf = new ComboBox { Width = 120, ItemsSource = new[] { 4, 8, 15, 30 }, SelectedItem = s.RadioBufferSeconds };
        buf.SelectionChanged += (_, _) => s.RadioBufferSeconds = (int)buf.SelectedItem;
        root.Children.Add(Section("Audio", Field("Playback device", dev), Field("Output latency (ms)", lat), Field("Radio buffer (seconds)", buf),
            Ui.Text("Latency applies to the next track. Larger radio buffers survive weak connections but add delay when connecting.", 11, true)));

        // Equalizer
        var eqOn = new CheckBox { Content = "Enable audio processing (equalizer)", IsChecked = s.EqEnabled };
        eqOn.Click += (_, _) => { s.EqEnabled = eqOn.IsChecked == true; App.Player.Engine.EqEnabled = s.EqEnabled; };
        var bands = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var sliders = new List<Slider>(); bool loading = false;
        for (int i = 0; i < 10; i++)
        {
            int k = i; var sl = new Slider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, Height = 130, Value = s.EqGains[i], Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
            sl.ValueChanged += (_, e) => { if (loading) return; s.EqGains[k] = (float)e.NewValue; s.EqPreset = "Custom"; App.Player.Engine.SetEq(s.EqGains); };
            sliders.Add(sl);
            var f = Equalizer.Frequencies[i]; var lab = Ui.Text(f >= 1000 ? $"{f / 1000:0.#}k" : $"{f:0}", 11, true); lab.HorizontalAlignment = HorizontalAlignment.Center;
            bands.Children.Add(new StackPanel { Width = 54, Children = { sl, lab } });
        }
        var preset = new ComboBox { Width = 160, ItemsSource = Equalizer.Presets.Keys.Append("Custom").ToList(), SelectedItem = Equalizer.Presets.ContainsKey(s.EqPreset) ? s.EqPreset : "Custom", Margin = new Thickness(0, 4, 0, 0) };
        preset.SelectionChanged += (_, _) =>
        {
            if (preset.SelectedItem is string n && Equalizer.Presets.TryGetValue(n, out var g))
            { loading = true; for (int i = 0; i < 10; i++) { sliders[i].Value = g[i]; s.EqGains[i] = g[i]; } loading = false; s.EqPreset = n; App.Player.Engine.SetEq(s.EqGains); }
        };
        root.Children.Add(Section("Equalizer", eqOn, Field("Preset", preset), bands));

        // Playback speed + notifications
        var speedLabel = Ui.Text($"{s.PlaybackSpeed:0.00}×", 13);
        var speedSl = Ui.Slide(0.5, 2, s.PlaybackSpeed, v => { s.PlaybackSpeed = Math.Round(v * 20) / 20; App.Player.Engine.PlaybackSpeed = (float)s.PlaybackSpeed; speedLabel.Text = $"{s.PlaybackSpeed:0.00}×"; }, 240);
        var reset = Ui.Button("Reset", (_, _) => speedSl.Value = 1); reset.Margin = new Thickness(10, 0, 0, 0);
        root.Children.Add(Section("Playback speed", Ui.Row(speedSl, speedLabel, reset), Ui.Text("Speed and pitch change together (varispeed). Applies to local files, not radio.", 11, true)));
        CheckBox N(string t, bool v, Action<bool> set) { var c = new CheckBox { Content = t, IsChecked = v }; c.Click += (_, _) => set(c.IsChecked == true); return c; }
        root.Children.Add(Section("Notifications", Ui.Text("Windows notifications appear when the app is in the background.", 11, true),
            N("Track changed", s.NotifyTrack, v => s.NotifyTrack = v), N("Radio connection lost / restored", s.NotifyRadio, v => s.NotifyRadio = v),
            N("Playback errors", s.NotifyErrors, v => s.NotifyErrors = v), N("New audio device detected", s.NotifyDevices, v => s.NotifyDevices = v), N("Recording completed", s.NotifyRecording, v => s.NotifyRecording = v)));

        // Effects
        var fx = s.Effects; var eng = App.Player.Engine; eng.Effects = fx;
        CheckBox Chk(string t, bool v, Action<bool> set) { var c = new CheckBox { Content = t, IsChecked = v, Margin = new Thickness(0, 4, 18, 4) }; c.Click += (_, _) => set(c.IsChecked == true); return c; }
        UIElement Sl(string t, double min, double max, double val, Action<double> set) { var sl = Ui.Slide(min, max, val, set, 240); var g = Ui.Row(Ui.Text(t, 13, true), sl); ((TextBlock)((StackPanel)g).Children[0]).Width = 190; ((StackPanel)g).Margin = new Thickness(0, 3, 0, 3); return g; }
        root.Children.Add(Section("Audio effects",
            Chk("Enable effects processing (turn off for bit-perfect output)", fx.Enabled, v => fx.Enabled = v),
            Sl("Bass boost (dB)", 0, 12, fx.BassBoostDb, v => fx.BassBoostDb = (float)v), Sl("Treble enhancement (dB)", 0, 12, fx.TrebleBoostDb, v => fx.TrebleBoostDb = (float)v),
            Sl("Reverb amount", 0, 1, fx.ReverbMix, v => fx.ReverbMix = (float)v), Sl("Reverb room size", 0, 1, fx.ReverbRoom, v => fx.ReverbRoom = (float)v),
            Sl("Echo amount", 0, 1, fx.EchoMix, v => fx.EchoMix = (float)v), Sl("Echo delay (ms)", 50, 1000, fx.EchoDelayMs, v => fx.EchoDelayMs = (float)v), Sl("Echo feedback", 0, 0.8, fx.EchoFeedback, v => fx.EchoFeedback = (float)v),
            Sl("Stereo width", 0, 2, fx.StereoWidth, v => fx.StereoWidth = (float)v),
            Ui.Row(Chk("Compressor", fx.Compressor, v => fx.Compressor = v), Chk("Limiter", fx.Limiter, v => fx.Limiter = v), Chk("Volume normalization", fx.Normalize, v => fx.Normalize = v))));

        // Library / Visual / General
        var fps = new ComboBox { Width = 120, ItemsSource = new[] { 30, 60, 120 }, SelectedItem = s.FrameRateLimit };
        fps.SelectionChanged += (_, _) => { s.FrameRateLimit = (int)fps.SelectedItem; main.Visualizer.Viz.ApplySettings(); };
        var q = new ComboBox { Width = 120, ItemsSource = new[] { "Low", "Medium", "High", "Ultra" }, SelectedItem = s.QualityLevel };
        q.SelectionChanged += (_, _) => { s.QualityLevel = (string)q.SelectedItem; main.Visualizer.Viz.ApplySettings(); };
        root.Children.Add(Section("Visuals", Field("Quality", q), Field("Frame-rate limit", fps), Ui.Text("Use Low quality on older graphics cards.", 11, true)));

        var tray = new CheckBox { Content = "Minimize to system tray area (hide window)", IsChecked = s.MinimizeToTray };
        tray.Click += (_, _) => s.MinimizeToTray = tray.IsChecked == true;
        var restore = new CheckBox { Content = "Remember last played track", IsChecked = s.RestoreLastTrack };
        restore.Click += (_, _) => s.RestoreLastTrack = restore.IsChecked == true;
        var startup = new CheckBox { Content = "Start Audio Reactor when I sign in to Windows", IsChecked = s.StartWithWindows };
        startup.Click += (_, _) =>
        {
            s.StartWithWindows = startup.IsChecked == true;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)!;
                if (s.StartWithWindows) key.SetValue("AudioReactor", "\"" + Environment.ProcessPath + "\"");
                else key.DeleteValue("AudioReactor", false);
            }
            catch (Exception ex) { main.ShowToast("Could not change startup setting: " + ex.Message); startup.IsChecked = !s.StartWithWindows; s.StartWithWindows = startup.IsChecked == true; }
        };
        root.Children.Add(Section("General", startup, tray, restore));

        var folders = new ListBox { ItemsSource = s.LibraryFolders, MaxHeight = 100 };
        root.Children.Add(Section("Storage", Ui.Text("Library folders (add them from Music Library → Add folder)", 12, true), folders,
            Ui.Text("Settings and library: " + Core.Models.AppSettings.DataDir, 11, true)));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
