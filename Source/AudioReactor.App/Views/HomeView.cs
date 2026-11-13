using System.Windows;
using System.Windows.Controls;

namespace AudioReactor.App.Views;

public sealed class HomeView : UserControl
{
    public HomeView(MainWindow main)
    {
        var hero = new StackPanel { Margin = new Thickness(8) };
        hero.Children.Add(new TextBlock { Text = "Hear it. See it. Feel it.", FontSize = 40, FontWeight = FontWeights.Bold, Foreground = Ui.B("AccentBrush") });
        hero.Children.Add(Ui.Text("Local music, internet radio and real-time audio-reactive visuals in one place.", 16, true));
        hero.Children.Add(Ui.Row(Ui.Button("Open visualizer", (_, _) => main.Navigate("Visualizer Studio"), true), Ui.Button("Browse library", (_, _) => main.Navigate("Music Library")),
            Ui.Button("Listen to radio", (_, _) => main.Navigate("Internet Radio"))));
        ((StackPanel)hero.Children[2]).Margin = new Thickness(0, 18, 0, 0);

        UIElement Stat(string label, string value) => Ui.Card(Ui.Col(Ui.Text(value, 30, false, true), Ui.Text(label, 12, true)), 16);
        var stats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 26, 0, 0) };
        var pls = App.Db.GetPlaylists().Count;
        foreach (var (l, v) in new[] { ("tracks in library", App.Db.TrackCount().ToString()), ("playlists", pls.ToString()), ("radio stations", App.Db.GetStations().Count.ToString()) })
        { var c = Stat(l, v); ((FrameworkElement)c).Margin = new Thickness(0, 0, 14, 0); stats.Children.Add(c); }

        var recent = App.Db.GetSmart("played").Take(8).ToList();
        var recentList = new ListBox { ItemsSource = recent.Select(t => $"{t.Display}   [{t.DurationText}]").ToList(), MaxHeight = 280 };
        recentList.MouseDoubleClick += (_, _) => { if (recentList.SelectedIndex >= 0) App.Player.PlayQueue(recent, recentList.SelectedIndex); };
        var rc = Ui.Col(Ui.Text("Recently played", 16, false, true), recent.Count == 0 ? Ui.Text("Nothing played yet — add some music from the library.", 13, true) : recentList);
        var rcard = Ui.Card(rc); rcard.Margin = new Thickness(0, 14, 0, 0);

        var root = new StackPanel { Margin = new Thickness(36, 30, 36, 20) };
        root.Children.Add(hero); root.Children.Add(stats); root.Children.Add(rcard);
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
