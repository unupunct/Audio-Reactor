using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AudioReactor.Core.Lighting;

namespace AudioReactor.App.Views;

/// <summary>RGB Lighting page: integrations, devices, modes and a live virtual light bar.</summary>
public sealed class LightingView : UserControl
{
    private readonly LightingEngine _eng = App.Analysis.Lighting;
    private readonly StackPanel _adapters = new(), _devices = new();
    private readonly List<Rectangle> _bar = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly TextBlock _status = Ui.Text("", 12, true);
    private readonly ToggleButton2 _power = new();
    private static readonly string[] Features = { "bass", "lowMid", "mid", "highMid", "treble", "energy", "volume", "beat" };

    private sealed class ToggleButton2 : System.Windows.Controls.Primitives.ToggleButton { }

    public LightingView()
    {
        var s = App.Settings.Lighting;
        var root = new StackPanel { Margin = new Thickness(36, 28, 36, 20) };
        root.Children.Add(Ui.Title("RGB Lighting"));
        root.Children.Add(Ui.Text("Sync compatible lights to the music. Only devices you tick below are controlled; hardware integrations need the vendor software described next to each.", 13, true));

        // Live preview bar
        var barPanel = new UniformGrid2 { Height = 44, Margin = new Thickness(0, 14, 0, 14) };
        for (int i = 0; i < LightingRenderer.Zones; i++) { var r = new Rectangle { Margin = new Thickness(2), RadiusX = 6, RadiusY = 6, Fill = Brushes.Black }; _bar.Add(r); barPanel.Children.Add(r); }
        root.Children.Add(Ui.Card(barPanel, 14, new Thickness(8)));

        // Power + mode
        _power.Content = "Lighting sync"; _power.IsChecked = App.Settings.LightingActive; _eng.Active = App.Settings.LightingActive;
        _power.Style = null; _power.Padding = new Thickness(18, 8, 18, 8); _power.Foreground = Ui.B("TextBrush"); _power.Background = Ui.B("PanelSolid"); _power.Margin = new Thickness(0, 0, 14, 0);
        void PowerText() { _power.Content = _power.IsChecked == true ? "● Lighting ON" : "○ Lighting OFF"; _power.Foreground = _power.IsChecked == true ? Ui.B("AccentBrush") : Ui.B("MutedBrush"); }
        PowerText();
        _power.Click += async (_, _) => { _eng.Active = _power.IsChecked == true; App.Settings.LightingActive = _eng.Active; PowerText(); if (!_eng.Active) await _eng.TurnOffAsync(); };

        var mode = new ComboBox { Width = 200, ItemsSource = Enum.GetValues<LightingMode>(), SelectedItem = s.Mode };
        var strobeWarn = Ui.Text("⚠ Strobe mode flashes lights. It is capped at 3 flashes per second, but may still affect people with photosensitive epilepsy.", 12); strobeWarn.Foreground = Brushes.Orange; strobeWarn.Visibility = s.Mode == LightingMode.Strobe ? Visibility.Visible : Visibility.Collapsed;
        mode.SelectionChanged += (_, _) => { s.Mode = (LightingMode)mode.SelectedItem; strobeWarn.Visibility = s.Mode == LightingMode.Strobe ? Visibility.Visible : Visibility.Collapsed; };
        root.Children.Add(Ui.Row(_power, Ui.Text("Mode ", 13, true), mode, new TextBlock { Width = 16 }, _status));
        root.Children.Add(strobeWarn);

        UIElement Lab(string t, Slider sl) { sl.Width = 260; var g = Ui.Row(Ui.Text(t, 13, true), sl); ((TextBlock)((StackPanel)g).Children[0]).Width = 150; ((StackPanel)g).Margin = new Thickness(0, 4, 0, 4); return g; }
        var controls = Ui.Col(
            Lab("Brightness", Ui.Slide(0, 1, s.Brightness, v => s.Brightness = (float)v)),
            Lab("Animation speed", Ui.Slide(0.2, 4, s.Speed, v => s.Speed = (float)v)),
            Lab("Audio sensitivity", Ui.Slide(0.3, 3, s.Sensitivity, v => s.Sensitivity = (float)v)),
            Lab("Strobe rate (Hz, max 3)", Ui.Slide(0.5, LightingSettings.MaxStrobeHz, Math.Min(s.StrobeHz, LightingSettings.MaxStrobeHz), v => s.StrobeHz = (float)v)));

        // Palette
        var palRow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var presets = new (string, string[])[] { ("Cyberpunk", new[] { "#00E5FF", "#A64DFF", "#FF2BD6" }), ("Matrix", new[] { "#003B00", "#00FF41", "#B6FFB0" }), ("Fire", new[] { "#FF1F00", "#FF7A00", "#FFE066" }), ("Ocean", new[] { "#0052D4", "#00C6FF", "#7FFFD4" }),
            ("Synthwave", new[] { "#784BA0", "#FF3CAC", "#2B86C5" }), ("Aurora", new[] { "#3DFFA0", "#7A5CFF", "#FF6AD5" }), ("Rainbow", new[] { "#FF3B30", "#FFCC00", "#34C759", "#2E8BFF", "#AF52DE" }), ("Monochrome", new[] { "#FFFFFF", "#8A8A8A" }) };
        foreach (var (name, cols) in presets)
        {
            var b = new Button { Content = name, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 5, 10, 5) };
            b.Background = new LinearGradientBrush(cols.Select((c, i) => new GradientStop((Color)ColorConverter.ConvertFromString(c), i / (double)Math.Max(1, cols.Length - 1))).ToArray().Aggregate(new GradientStopCollection(), (a, g) => { a.Add(g); return a; }), 0);
            b.Foreground = Brushes.White; b.FontWeight = FontWeights.SemiBold;
            b.Click += (_, _) => { s.Palette = cols.Select(Rgb.FromHex).ToList(); s.StaticColor = s.Palette[0]; };
            palRow.Children.Add(b);
        }
        var rnd = new Button { Content = "🎲 Random", Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 5, 10, 5) };
        rnd.Click += (_, _) => { var r = new Random(); float h = r.Next(360); s.Palette = new() { Rgb.FromHsv(h, 1, 1), Rgb.FromHsv(h + 40 + r.Next(60), 0.9f, 1), Rgb.FromHsv(h + 160 + r.Next(60), 0.9f, 1) }; s.StaticColor = s.Palette[0]; };
        palRow.Children.Add(rnd);

        // Custom reactive mapping
        ComboBox Map(string cur, Action<string> set) { var c = new ComboBox { Width = 90, ItemsSource = Features, SelectedItem = cur, Margin = new Thickness(0, 0, 10, 0) }; c.SelectionChanged += (_, _) => set((string)c.SelectedItem); return c; }
        var custom = Ui.Row(Ui.Text("Custom reactive:  R ←", 12, true), Map(s.CustomR, v => s.CustomR = v), Ui.Text("G ←", 12, true), Map(s.CustomG, v => s.CustomG = v), Ui.Text("B ←", 12, true), Map(s.CustomB, v => s.CustomB = v));
        foreach (var c in ((StackPanel)custom).Children.OfType<TextBlock>()) c.Margin = new Thickness(0, 0, 6, 0);
        var cs = Ui.Col(controls, Ui.Text("Colour palette (shared by modes; static colour uses the first colour)", 12, true), palRow, custom);
        root.Children.Add(Ui.Card(cs)); ((FrameworkElement)root.Children[^1]).Margin = new Thickness(0, 6, 0, 16);

        // Integrations + devices
        var hueIp = new TextBox { Width = 160, Text = App.Settings.HueBridgeIp ?? "", Margin = new Thickness(0, 0, 8, 0) };
        var huePair = Ui.Button("Pair Hue bridge", async (_, _) =>
        {
            var hue = _eng.Get<HueAdapter>()!; _status.Text = "Press the link button on the bridge, then wait…";
            bool ok = false; for (int i = 0; i < 5 && !ok; i++) { ok = await hue.PairAsync(hueIp.Text.Trim()); if (!ok) await Task.Delay(2000); }
            if (ok) { App.Settings.HueBridgeIp = hue.BridgeIp; App.Settings.HueUsername = hue.Username; }
            _status.Text = hue.Status; await Detect();
        });
        var detect = Ui.Button("Detect devices", async (_, _) => await Detect(), true);
        root.Children.Add(Ui.Card(Ui.Col(Ui.Row(Ui.Text("Integrations & devices  ", 16, false, true), detect), _adapters, Ui.Text("Philips Hue", 12, true), Ui.Row(Ui.Text("Bridge IP ", 12, true), hueIp, huePair), _devices,
            Ui.Text("Corsair iCUE: no open API is available to this app, so iCUE devices are not controlled directly. If you use OpenRGB, Corsair devices exposed there appear under “OpenRGB”.", 11, true))));
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _tick.Tick += (_, _) => { var f = _eng.CurrentFrame; for (int i = 0; i < _bar.Count; i++) _bar[i].Fill = new SolidColorBrush(Color.FromRgb(f[i].R, f[i].G, f[i].B)); };
        Loaded += async (_, _) => { _tick.Start(); if (_eng.Devices.Count == 0) await Detect(); };
        Unloaded += (_, _) => _tick.Stop();
    }

    private async Task Detect()
    {
        _status.Text = "Detecting…";
        await _eng.DetectAllAsync();
        // Restore which hardware devices the user enabled previously.
        foreach (var d in _eng.Devices) if (App.Settings.EnabledLightDevices.Contains(d.Adapter + "|" + d.Id)) d.Enabled = true;
        _adapters.Children.Clear();
        foreach (var a in _eng.Adapters) _adapters.Children.Add(Ui.Text($"{(a.Status.StartsWith("Connected") || a is VirtualAdapter ? "●" : "○")} {a.Name}: {a.Status}", 12, true));
        _devices.Children.Clear();
        foreach (var d in _eng.Devices)
        {
            var cb = new CheckBox { Content = d.Display, IsChecked = d.Enabled, Margin = new Thickness(0, 6, 12, 0) };
            var dev = d; var key = d.Adapter + "|" + d.Id;
            cb.Click += (_, _) => { dev.Enabled = cb.IsChecked == true; var l = App.Settings.EnabledLightDevices; l.Remove(key); if (dev.Enabled) l.Add(key); };
            var zs = Ui.Slide(0, 15, d.ZoneStart, v => dev.ZoneStart = (int)v, 90); var ze = Ui.Slide(1, 16, d.ZoneEnd, v => dev.ZoneEnd = (int)v, 90);
            _devices.Children.Add(Ui.Row(cb, Ui.Text("zones ", 11, true), zs, Ui.Text(" to ", 11, true), ze));
        }
        if (_eng.Devices.Count == 0) _devices.Children.Add(Ui.Text("No devices found.", 12, true));
        _status.Text = $"{_eng.Devices.Count} device(s) found";
    }
}

/// <summary>Simple equal-width horizontal grid for the preview bar.</summary>
public sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid2() { Rows = 1; } }
