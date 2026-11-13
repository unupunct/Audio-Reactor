using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioReactor.Core.Models;
using AudioReactor.Core.Video;
using Microsoft.Win32;

namespace AudioReactor.App.Views;

/// <summary>Visual effect editor + colour palette studio + custom preset manager. Every change is previewed live on the Visualizer Studio page.</summary>
public sealed class EffectEditorWindow : Window
{
    private readonly VisualizerView _viz;
    private readonly PresetStore _store = new(Path.Combine(AppSettings.DataDir, "presets"));
    private readonly ListBox _list = new() { Height = 220 };
    private readonly ComboBox _base = new(), _palette = new();
    private readonly Dictionary<string, Slider> _sliders = new();
    private readonly Dictionary<string, Button> _swatches = new();
    private readonly Slider _intensity = new() { Minimum = 0.3, Maximum = 2, Value = 1 }, _speed = new() { Minimum = 0.2, Maximum = 3, Value = 1 };
    private readonly TextBox _desc = new();
    private CustomPreset _cur = new();
    private bool _loading;

    private static readonly (string Key, string Label, double Min, double Max)[] Adjustments =
    {
        ("brightness", "Brightness", 0.4, 1.8), ("contrast", "Contrast", 0.6, 1.6), ("saturation", "Saturation", 0, 2), ("glow", "Glow", 0, 3),
        ("particles", "Particle density", 0.2, 2), ("particleSpeed", "Particle speed", 0.2, 3), ("rotation", "Rotation speed", 0, 3), ("camera", "Camera movement", 0, 3),
        ("zoom", "Zoom", 0.5, 2), ("depth", "Depth", 0.3, 2.5), ("motion", "Motion intensity", 0.2, 3), ("sens", "Audio sensitivity", 0.3, 3),
        ("bassSens", "Bass sensitivity", 0.3, 3), ("trebleSens", "Treble sensitivity", 0.3, 3),
    };

    public EffectEditorWindow(VisualizerView viz)
    {
        _viz = viz; Title = "Effect & palette editor"; Width = 960; Height = 820; MinHeight = 600; Owner = Application.Current.MainWindow; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.B("PanelSolid"); Foreground = Ui.B("TextBrush");
        void FillBases() { var sel = _cur?.BasePreset ?? App.Settings.VisualPreset; var names = viz.Presets.Select(p => p.Name).ToList(); bool l = _loading; _loading = true; _base.ItemsSource = names; _base.SelectedItem = sel; _loading = l; }
        FillBases(); viz.PresetsLoaded += () => Dispatcher.BeginInvoke(FillBases);
        _palette.ItemsSource = PaletteLibrary.Presets.Keys.ToList();

        // --- left: presets ---
        _list.DisplayMemberPath = "Name"; _list.SelectionChanged += (_, _) => { if (!_loading && _list.SelectedItem is CustomPreset p) Load(p.Clone(), keepName: p.Name); };
        var left = new StackPanel { Width = 270, Margin = new Thickness(0, 0, 18, 0) };
        left.Children.Add(Ui.Text("My presets", 15, false, true)); left.Children.Add(_list); ((FrameworkElement)left.Children[^1]).Margin = new Thickness(0, 6, 0, 8);
        left.Children.Add(Ui.Row(Ui.Button("Save…", (_, _) => Save(), true), Ui.Button("Duplicate", (_, _) => Duplicate()), Ui.Button("★", (_, _) => ToggleFav())));
        left.Children.Add(Ui.Row(Ui.Button("Delete", (_, _) => Delete()), Ui.Button("Export…", (_, _) => Export()), Ui.Button("Import…", (_, _) => Import()))); ((FrameworkElement)left.Children[^1]).Margin = new Thickness(0, 6, 0, 0);
        left.Children.Add(Ui.Text("Description", 11, true)); _desc.Margin = new Thickness(0, 2, 0, 0); _desc.TextChanged += (_, _) => _cur.Description = _desc.Text; left.Children.Add(_desc);
        ((TextBlock)left.Children[^2]).Margin = new Thickness(0, 14, 0, 0);
        left.Children.Add(Ui.Text("Presets are plain JSON files in %AppData%\\AudioReactor\\presets — share them by exporting.", 11, true)); ((TextBlock)left.Children[^1]).Margin = new Thickness(0, 12, 0, 0);

        // --- right: editor ---
        var right = new StackPanel();
        right.Children.Add(Ui.Text("Base visualizer", 12, true)); _base.Margin = new Thickness(0, 2, 0, 12); _base.SelectionChanged += (_, _) => { if (!_loading) { _cur.BasePreset = (string)_base.SelectedItem; Push(); } }; right.Children.Add(_base);

        right.Children.Add(Ui.Text("Colour palette", 15, false, true));
        _palette.Margin = new Thickness(0, 6, 8, 8); _palette.SelectionChanged += (_, _) => { if (!_loading && _palette.SelectedItem is string k) { _cur.Palette = PaletteLibrary.Presets[k]; RefreshSwatches(); Push(); } };
        var rnd = Ui.Button("🎲 Random", (_, _) => { _cur.Palette = PaletteLibrary.Random(); RefreshSwatches(); Push(); });
        right.Children.Add(Ui.Row(_palette, rnd)); ((StackPanel)right.Children[^1]).Children[0].SetValue(WidthProperty, 180.0);
        var sw = new WrapPanel(); string[] names = { "Primary", "Secondary", "Background", "Accent", "Particle", "Glow" };
        foreach (var n in names)
        {
            var b = new Button { Width = 90, Height = 46, Margin = new Thickness(0, 0, 8, 8), Content = n, FontSize = 11, VerticalContentAlignment = VerticalAlignment.Bottom };
            var name = n; b.Click += (_, _) => PickColor(name); _swatches[n] = b; sw.Children.Add(b);
        }
        right.Children.Add(sw);

        right.Children.Add(Ui.Text("Effect adjustments", 15, false, true)); ((TextBlock)right.Children[^1]).Margin = new Thickness(0, 8, 0, 4);
        var grid = new UniformGrid2 { Rows = 0, Columns = 2 };
        grid.Rows = 0; grid.Columns = 2;
        foreach (var (key, label, min, max) in Adjustments)
        {
            var s = new Slider { Minimum = min, Maximum = max, Value = 1, Margin = new Thickness(0, 0, 14, 6) }; var k = key;
            s.ValueChanged += (_, e) => { if (!_loading) { _cur.Adjust[k] = e.NewValue; Push(); } }; _sliders[key] = s;
            var cell = new StackPanel(); cell.Children.Add(Ui.Text(label, 11, true)); cell.Children.Add(s); grid.Children.Add(cell);
        }
        foreach (var (lbl, sl, set) in new (string, Slider, Action<double>)[] { ("Overall intensity", _intensity, v => _cur.Intensity = v), ("Animation speed", _speed, v => _cur.Speed = v) })
        { sl.Margin = new Thickness(0, 0, 14, 6); sl.ValueChanged += (_, e) => { if (!_loading) { set(e.NewValue); Push(); } }; var cell = new StackPanel(); cell.Children.Add(Ui.Text(lbl, 11, true)); cell.Children.Add(sl); grid.Children.Add(cell); }
        right.Children.Add(grid);
        right.Children.Add(Ui.Row(Ui.Button("Reset adjustments", (_, _) => { _cur.Adjust = CustomPreset.DefaultAdjust(); _cur.Intensity = 1; _cur.Speed = 1; Load(_cur, _cur.Name); })));
        ((StackPanel)right.Children[^1]).Margin = new Thickness(0, 8, 0, 0);
        right.Children.Add(Ui.Text("The preview is the Visualizer Studio page behind this window. Bloom and per-frequency routing are not editable in this version.", 11, true)); ((TextBlock)right.Children[^1]).Margin = new Thickness(0, 10, 0, 0);

        var root = new Grid { Margin = new Thickness(22) }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); root.ColumnDefinitions.Add(new ColumnDefinition());
        root.Children.Add(left); Grid.SetColumn(right, 1); root.Children.Add(new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Grid.SetColumn((UIElement)root.Children[1], 1);
        Content = root;
        _cur.BasePreset = App.Settings.VisualPreset; Load(_cur, "(unsaved)"); ReloadList();
        Closing += (_, _) => { };
    }

    private void ReloadList() { _loading = true; _list.ItemsSource = null; _list.ItemsSource = _store.List(); _loading = false; }

    private void Load(CustomPreset p, string keepName)
    {
        _loading = true; _cur = p; if (keepName != "(unsaved)") _cur.Name = keepName;
        _base.SelectedItem = p.BasePreset; _desc.Text = p.Description; _intensity.Value = p.Intensity; _speed.Value = p.Speed;
        foreach (var (key, _, _, _) in Adjustments) _sliders[key].Value = p.Adjust.TryGetValue(key, out var v) ? v : 1;
        _palette.SelectedItem = PaletteLibrary.Presets.FirstOrDefault(kv => kv.Value == p.Palette).Key;
        RefreshSwatches(); _loading = false; Push();
    }

    private void RefreshSwatches()
    {
        var p = _cur.Palette; var map = new Dictionary<string, string> { ["Primary"] = p.Primary, ["Secondary"] = p.Secondary, ["Background"] = p.Background, ["Accent"] = p.Accent, ["Particle"] = p.Particle, ["Glow"] = p.Glow };
        foreach (var (n, hex) in map)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex); _swatches[n].Background = new SolidColorBrush(c);
            _swatches[n].Foreground = (c.R * 0.3 + c.G * 0.59 + c.B * 0.11) > 140 ? Brushes.Black : Brushes.White;
        }
    }

    private void PickColor(string which)
    {
        var cur = (SolidColorBrush)_swatches[which].Background;
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(cur.Color.R, cur.Color.G, cur.Color.B) };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        string hex = $"#{dlg.Color.R:x2}{dlg.Color.G:x2}{dlg.Color.B:x2}"; var p = _cur.Palette;
        _cur.Palette = which switch { "Primary" => p with { Primary = hex }, "Secondary" => p with { Secondary = hex }, "Background" => p with { Background = hex }, "Accent" => p with { Accent = hex }, "Particle" => p with { Particle = hex }, _ => p with { Glow = hex } };
        RefreshSwatches(); Push();
    }

    /// <summary>Send the current look to the live visualizer.</summary>
    private void Push()
    {
        if (_loading) return;
        var p = _cur.Palette; _viz.SetPreset(_cur.BasePreset);
        _viz.SendConfig(new
        {
            palette = new { primary = p.Primary, secondary = p.Secondary, background = p.Background, accent = p.Accent, particle = p.Particle, glow = p.Glow },
            adj = _cur.Adjust, intensity = _cur.Intensity, speed = _cur.Speed
        });
    }

    private static string? Ask(string title, string initial)
    {
        var w = new Window { Title = title, Width = 360, Height = 150, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.B("PanelSolid"), ResizeMode = ResizeMode.NoResize };
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) }; string? r = null;
        w.Content = new StackPanel { Margin = new Thickness(18), Children = { tb, Ui.Button("OK", (_, _) => { r = tb.Text; w.Close(); }, true) } };
        tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); }; w.ShowDialog(); return string.IsNullOrWhiteSpace(r) ? null : r.Trim();
    }

    private void Save()
    {
        var n = Ask("Save preset as", _cur.Name == "My preset" || _cur.Name == "(unsaved)" ? "My preset" : _cur.Name); if (n == null) return;
        var p = _cur.Clone(); p.Name = n; p.Description = _desc.Text; _store.Save(p); _cur = p; ReloadList();
        _loading = true; _list.SelectedItem = ((List<CustomPreset>)_list.ItemsSource).FirstOrDefault(x => x.Name == n); _loading = false;
    }

    private void Duplicate() { if (_list.SelectedItem is not CustomPreset p) return; var c = p.Clone(); c.Name = p.Name + " copy"; _store.Save(c); ReloadList(); }
    private void ToggleFav() { if (_list.SelectedItem is not CustomPreset p) return; p.Favorite = !p.Favorite; _store.Save(p); ReloadList(); }
    private void Delete() { if (_list.SelectedItem is not CustomPreset p) return; if (MessageBox.Show($"Delete preset “{p.Name}”?", "Delete", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _store.Delete(p.Name); ReloadList(); } }
    private void Export() { if (_list.SelectedItem is not CustomPreset p) return; var d = new SaveFileDialog { Filter = "Audio Reactor preset|*.json", FileName = p.Name + ".arpreset.json" }; if (d.ShowDialog() == true) _store.Export(p, d.FileName); }
    private void Import()
    {
        var d = new OpenFileDialog { Filter = "Audio Reactor preset|*.json", Multiselect = true }; if (d.ShowDialog() != true) return;
        foreach (var f in d.FileNames) { try { _store.Import(f); } catch (Exception ex) { MessageBox.Show($"{Path.GetFileName(f)}: {ex.Message}", "Import failed"); } }
        ReloadList();
    }
}
