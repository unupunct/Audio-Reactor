using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AudioReactor.App.Views;

/// <summary>Small helpers to build consistent, themed controls in code.</summary>
public static class Ui
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    public static TextBlock Text(string t, double size = 14, bool muted = false, bool bold = false) => new()
    {
        Text = t, FontSize = size, Foreground = B(muted ? "MutedBrush" : "TextBrush"), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap
    };

    public static TextBlock Title(string t) => new() { Text = t, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = B("TextBrush"), Margin = new Thickness(0, 0, 0, 4) };

    public static Button Button(string text, RoutedEventHandler click, bool accent = false, string? tip = null)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0) };
        if (accent) b.Style = (Style)Application.Current.FindResource("AccentButton");
        if (tip != null) b.ToolTip = tip;
        b.Click += click; return b;
    }

    public static Border Card(UIElement child, double radius = 16, Thickness? pad = null) => new()
    {
        Background = B("PanelBrush"), BorderBrush = B("BorderBrushSoft"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius),
        Padding = pad ?? new Thickness(16), Child = child
    };

    public static StackPanel Row(params UIElement[] kids)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var k in kids) p.Children.Add(k); return p;
    }

    public static StackPanel Col(params UIElement[] kids)
    {
        var p = new StackPanel();
        foreach (var k in kids) p.Children.Add(k); return p;
    }

    public static Slider Slide(double min, double max, double val, Action<double> changed, double width = 220)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = val, Width = width, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, e) => changed(e.NewValue); return s;
    }

    /// <summary>Wraps a TextBox with a grey hint that shows while it is empty.</summary>
    public static Grid WithHint(TextBox tb, string hint)
    {
        var g = new Grid { Width = tb.Width, Margin = tb.Margin }; tb.Margin = new Thickness(0); tb.Width = double.NaN;
        var h = new TextBlock { Text = hint, Foreground = B("MutedBrush"), IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        void Upd() => h.Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed;
        tb.TextChanged += (_, _) => Upd(); Upd(); g.Children.Add(tb); g.Children.Add(h); return g;
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
