using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioReactor.App.Views;

/// <summary>Visualizer Studio: live preview, preset browser and quick controls.</summary>
public sealed class VisualizerPage : UserControl
{
    public VisualizerView Viz { get; } = new();
    private readonly Grid _host = new();
    private readonly ListBox _list = new();
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox _cat = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _desc = Ui.Text("", 12, true);
    private readonly TextBlock _fps = Ui.Text("", 11, true);
    private List<(string Name, string Category, string Description)> _all = new();
    private bool _loading;

    private static readonly Dictionary<string, object> Palettes = new()
    {
        ["Cyberpunk"] = new { primary = "#00e5ff", secondary = "#ff2bd6", background = "#07060d", accent = "#fcee09", particle = "#ffffff", glow = "#00e5ff" },
        ["Matrix Green"] = new { primary = "#00ff41", secondary = "#008f11", background = "#020a03", accent = "#b6ffb0", particle = "#c8ffc8", glow = "#00ff41" },
        ["Purple Galaxy"] = new { primary = "#b06cff", secondary = "#4b1fa8", background = "#07040f", accent = "#ff7be5", particle = "#e8d8ff", glow = "#9b5cff" },
        ["Electric Blue"] = new { primary = "#2e8bff", secondary = "#00e5ff", background = "#030814", accent = "#ffffff", particle = "#bfe0ff", glow = "#2e8bff" },
        ["Neon Pink"] = new { primary = "#ff2bd6", secondary = "#ff6a00", background = "#0d0310", accent = "#00f0ff", particle = "#ffd0f5", glow = "#ff2bd6" },
        ["Fire"] = new { primary = "#ff7a00", secondary = "#ff1f00", background = "#0c0402", accent = "#ffe066", particle = "#ffd199", glow = "#ff5a00" },
        ["Ocean"] = new { primary = "#00c6ff", secondary = "#0052d4", background = "#020b14", accent = "#7fffd4", particle = "#d0f4ff", glow = "#00c6ff" },
        ["Aurora"] = new { primary = "#3dffa0", secondary = "#7a5cff", background = "#030a10", accent = "#ff6ad5", particle = "#d9fff0", glow = "#3dffa0" },
        ["Synthwave"] = new { primary = "#ff3cac", secondary = "#784ba0", background = "#0b0221", accent = "#2b86c5", particle = "#ffd6f0", glow = "#ff3cac" },
        ["Rainbow"] = new { primary = "#ff3b30", secondary = "#5856d6", background = "#05060a", accent = "#34c759", particle = "#ffcc00", glow = "#ffffff" },
        ["Monochrome"] = new { primary = "#ffffff", secondary = "#8a8a8a", background = "#050505", accent = "#d0d0d0", particle = "#ffffff", glow = "#ffffff" },
    };

    public VisualizerPage()
    {
        _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedItem is string n) { Viz.SetPreset(n); ShowDesc(n); } };
        _search.TextChanged += (_, _) => Filter();
        _cat.SelectionChanged += (_, _) => Filter();
        Viz.PresetsLoaded += () => Dispatcher.BeginInvoke(() =>
        {
            _all = Viz.Presets;
            _cat.ItemsSource = new[] { "All categories" }.Concat(_all.Select(p => p.Category).Distinct()).ToList(); _loading = true; _cat.SelectedIndex = 0; _loading = false;
            Filter();
            Viz.ApplySettings();
        });
        Viz.FpsReported += f => Dispatcher.BeginInvoke(() => _fps.Text = $"{f} FPS");

        var s = App.Settings;
        var pal = new ComboBox { ItemsSource = Palettes.Keys.ToList(), Margin = new Thickness(0, 0, 0, 10), SelectedIndex = 0 };
        pal.SelectionChanged += (_, _) => { if (pal.SelectedItem is string k) Viz.SendConfig(new { palette = Palettes[k] }); };
        var quality = new ComboBox { ItemsSource = new[] { "Low", "Medium", "High", "Ultra" }, SelectedItem = s.QualityLevel, Margin = new Thickness(0, 0, 0, 10) };
        quality.SelectionChanged += (_, _) => { s.QualityLevel = (string)quality.SelectedItem; Viz.ApplySettings(); };

        UIElement Lab(string t, Slider sl) { var g = Ui.Col(Ui.Text(t, 11, true), sl); sl.Width = double.NaN; g.Margin = new Thickness(0, 0, 0, 8); return g; }

        var panel = new StackPanel();
        panel.Children.Add(Ui.Text("Presets", 16, false, true));
        panel.Children.Add(_fps);
        _search.Margin = new Thickness(0); var searchBox = Ui.WithHint(_search, "Search presets…"); searchBox.Margin = new Thickness(0, 0, 0, 8); searchBox.Width = double.NaN;
        panel.Children.Add(searchBox); panel.Children.Add(_cat);
        var side = new StackPanel();
        var controls = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        controls.Children.Add(Ui.Text("Palette", 11, true)); controls.Children.Add(pal);
        controls.Children.Add(Ui.Text("Quality", 11, true)); controls.Children.Add(quality);
        controls.Children.Add(Lab("Intensity", Ui.Slide(0.3, 2, s.VisualIntensity, v => { s.VisualIntensity = v; Viz.ApplySettings(); })));
        controls.Children.Add(Lab("Animation speed", Ui.Slide(0.2, 3, s.AnimationSpeed, v => { s.AnimationSpeed = v; Viz.ApplySettings(); })));
        controls.Children.Add(Lab("Beat sensitivity", Ui.Slide(0.2, 3, s.BeatSensitivity, v => { s.BeatSensitivity = v; Viz.ApplySettings(); })));
        var recBtn = Ui.Button("● Record…", (_, _) => { if (_recWin == null) { _recWin = new RecordingWindow(App.Recording, Viz); _recWin.Closed += (_, _) => _recWin = null; _recWin.Show(); } else _recWin.Activate(); });
        recBtn.Margin = new Thickness(0, 0, 0, 8);
        var ytBtn = Ui.Button("▶ Export for YouTube…", (_, _) => { if (_ytWin == null) { _ytWin = new YouTubeExportWindow(Viz); _ytWin.Closed += (_, _) => _ytWin = null; _ytWin.Show(); } else _ytWin.Activate(); }, true);
        ytBtn.Margin = new Thickness(0, 0, 0, 8);
        var edBtn = Ui.Button("✎ Effect & palette editor…", (_, _) => { if (_edWin == null) { _edWin = new EffectEditorWindow(Viz); _edWin.Closed += (_, _) => _edWin = null; _edWin.Show(); } else _edWin.Activate(); });
        edBtn.Margin = new Thickness(0, 0, 0, 8);
        controls.Children.Add(edBtn);
        controls.Children.Add(ytBtn);
        controls.Children.Add(recBtn);
        controls.Children.Add(Ui.Button("Fullscreen (F11)", (_, _) => EnterFullscreen(null, null), true));
        var top = new StackPanel(); foreach (var c in panel.Children.Cast<UIElement>().ToList()) { panel.Children.Remove(c); top.Children.Add(c); }
        side.Children.Add(top);
        _list.Height = 280; _desc.Margin = new Thickness(0, 6, 0, 0);
        side.Children.Add(_list); side.Children.Add(_desc); side.Children.Add(controls);

        var grid = new Grid { Margin = new Thickness(20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        var vizCard = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, BorderBrush = Ui.B("BorderBrushSoft"), BorderThickness = new Thickness(1), Child = _host };
        _host.Children.Add(Viz);
        grid.Children.Add(vizCard);
        var sideCard = Ui.Card(new ScrollViewer { Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn(sideCard, 1); sideCard.Margin = new Thickness(16, 0, 0, 0); grid.Children.Add(sideCard);
        Content = grid;
    }

    private void Filter()
    {
        if (_loading) return;
        var q = _search.Text.Trim(); var cat = _cat.SelectedIndex > 0 ? _cat.SelectedItem as string : null;
        var names = _all.Where(p => (cat == null || p.Category == cat) && (q == "" || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase))).Select(p => p.Name).ToList();
        _loading = true; _list.ItemsSource = names; _list.SelectedItem = names.Contains(App.Settings.VisualPreset) ? App.Settings.VisualPreset : null; _loading = false;
        ShowDesc(App.Settings.VisualPreset);
    }

    private void ShowDesc(string name) { var p = _all.FirstOrDefault(x => x.Name == name); _desc.Text = p.Name == null ? "" : $"{p.Category} — {p.Description}"; }

    public object? EnterFullscreen(Action<Window>? opened, Action? closed)
    {
        if (_fsWindow != null) { _fsWindow.Close(); return null; }
        var screens = System.Windows.Forms.Screen.AllScreens;
        var main = Application.Current.MainWindow;
        // Prefer a different monitor than the control window when several exist (second-monitor mode).
        var cur = System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(main).Handle);
        var target = screens.FirstOrDefault(s => s.DeviceName != cur.DeviceName) ?? cur;
        _host.Children.Remove(Viz);
        var w = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Brushes.Black, Content = Viz, ShowInTaskbar = false, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = target.Bounds.Left, Top = target.Bounds.Top, Width = target.Bounds.Width, Height = target.Bounds.Height };
        w.KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.F11) w.Close(); else if (e.Key == Key.Space) App.Player.TogglePlay(); };
        w.MouseDoubleClick += (_, _) => w.Close();
        w.Closed += (_, _) => { w.Content = null; _host.Children.Add(Viz); _fsWindow = null; closed?.Invoke(); };
        _fsWindow = w; opened?.Invoke(w);
        w.Show(); w.WindowState = WindowState.Maximized; w.Activate();
        return null;
    }
    private Window? _fsWindow;
    private RecordingWindow? _recWin;
    private YouTubeExportWindow? _ytWin;
    private EffectEditorWindow? _edWin;
}
