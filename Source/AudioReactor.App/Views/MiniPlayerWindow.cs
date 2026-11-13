using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace AudioReactor.App.Views;

/// <summary>Small always-on-top floating player. Playback is owned by <see cref="App.Player"/>, so switching modes never interrupts audio.</summary>
public sealed class MiniPlayerWindow : Window
{
    private readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _artist = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Image _art = new() { Stretch = Stretch.UniformToFill };
    private readonly Button _play;
    private readonly ProgressBar _bar = new() { Height = 3, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(0), Background = Ui.B("PanelHover"), Foreground = Ui.B("AccentBrush") };
    private readonly System.Windows.Threading.DispatcherTimer _t = new() { Interval = TimeSpan.FromMilliseconds(300) };
    public event Action? RestoreRequested;

    public MiniPlayerWindow()
    {
        Width = 380; Height = 124; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = true; Title = "Audio Reactor (mini)";
        AllowsTransparency = false; Background = Ui.B("PanelSolid"); Foreground = Ui.B("TextBrush");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0) });
        _title.Foreground = Ui.B("TextBrush"); _artist.Foreground = Ui.B("MutedBrush");

        Button B(string glyph, RoutedEventHandler h, bool accent = false, int size = 34)
        {
            var b = new Button { Content = glyph, Width = size, Height = size, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Padding = new Thickness(0), Margin = new Thickness(2, 0, 2, 0), Style = (Style)FindResource(accent ? "AccentButton" : "IconButton") };
            WindowChrome.SetIsHitTestVisibleInChrome(b, true); b.Click += h; return b;
        }
        _play = B("", (_, _) => App.Player.TogglePlay(), true, 38);
        var restore = B("", (_, _) => RestoreRequested?.Invoke(), false, 26); restore.ToolTip = "Back to full window";
        var close = B("", (_, _) => { Close(); Application.Current.Shutdown(); }, false, 26); close.ToolTip = "Quit";
        var vol = new Slider { Minimum = 0, Maximum = 1, Value = App.Settings.Volume, Width = 90, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        vol.ValueChanged += (_, e) => App.Player.SetVolume(e.NewValue);

        var artBox = new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(12), ClipToBounds = true, Background = Ui.B("PanelHover"), Margin = new Thickness(10, 0, 12, 0), Child = _art };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        controls.Children.Add(B("", (_, _) => App.Player.Previous())); controls.Children.Add(_play); controls.Children.Add(B("", (_, _) => App.Player.Next())); controls.Children.Add(vol);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 230 };
        info.Children.Add(_title); info.Children.Add(_artist); info.Children.Add(controls); info.Children.Add(_bar);
        var top = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        top.Children.Add(restore); top.Children.Add(close);
        var grid = new Grid(); grid.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { artBox, info } }); grid.Children.Add(top);
        Content = new Border { BorderBrush = Ui.B("BorderBrushSoft"), BorderThickness = new Thickness(1), Child = grid };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var area = SystemParameters.WorkArea; Left = area.Right - Width - 24; Top = area.Bottom - Height - 24;

        App.Player.PropertyChanged += OnChanged; _t.Tick += (_, _) => { var e = App.Player.Engine; _bar.Value = e.Duration.TotalSeconds > 0 ? e.Position.TotalSeconds / e.Duration.TotalSeconds : 0; };
        Loaded += (_, _) => { Refresh(); _t.Start(); }; Closing += (_, _) => { App.Player.PropertyChanged -= OnChanged; _t.Stop(); };
    }

    private void OnChanged(object? s, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        var p = App.Player; _title.Text = p.NowTitle; _artist.Text = p.NowArtist; _play.Content = p.IsPlaying ? "" : "";
        _art.Source = null;
        if (p.Artwork is { Length: > 0 }) { try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(p.Artwork); bi.DecodePixelWidth = 200; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); _art.Source = bi; } catch { } }
    }
}
