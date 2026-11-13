using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioReactor.Core.Models;
using AudioReactor.Core.Playlists;
using Microsoft.Win32;

namespace AudioReactor.App.Views;

/// <summary>Radio station manager. Stations come only from the user (no hardcoded listings): add by URL or import M3U/PLS.</summary>
public sealed class RadioView : UserControl
{
    private readonly WrapPanel _cards = new();
    private readonly TextBox _search = new() { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox _filter = new() { Width = 160, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _status = Ui.Text("", 12, true);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public RadioView()
    {
        _filter.ItemsSource = new[] { "All stations", "Favorites", "Recently played" }; _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => Reload();
        _filter.SelectionChanged += (_, _) => Reload();
        var bar = Ui.Row(Ui.Button("Add station", (_, _) => Edit(new Station()), true), Ui.Button("Import M3U / PLS", (_, _) => ImportFile()),
            Ui.Button("Import from URL", (_, _) => ImportUrl()), Ui.WithHint(_search, "Search stations…"), _filter, _status);
        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 12) };
        var head = Ui.Col(Ui.Title("Internet Radio"), bar); head.Margin = new Thickness(0, 0, 0, 14);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _cards });
        Content = root;
        Reload();
        App.Player.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Services.PlayerService.RadioStatus)) Dispatcher.BeginInvoke(() => _status.Text = App.Player.IsRadio ? $"{App.Player.CurrentStation?.Name}: {App.Player.RadioStatus}" : ""); };
    }

    private void Reload()
    {
        var list = _filter.SelectedIndex == 2 ? App.Db.GetRecentStations() : App.Db.GetStations(_search.Text);
        if (_filter.SelectedIndex == 1) list = list.Where(s => s.Favorite).ToList();
        if (_filter.SelectedIndex == 2 && !string.IsNullOrWhiteSpace(_search.Text)) list = list.Where(s => s.Name.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        _cards.Children.Clear();
        if (list.Count == 0)
        {
            _cards.Children.Add(Ui.Text("No stations yet. Use “Add station” to paste a stream URL (Icecast, SHOUTcast, direct MP3/AAC/OGG), or import an .m3u / .pls file.", 14, true));
            ((TextBlock)_cards.Children[0]).MaxWidth = 560;
            return;
        }
        foreach (var s in list) _cards.Children.Add(MakeCard(s));
    }

    private UIElement MakeCard(Station s)
    {
        var logo = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 12, 0) };
        logo.Background = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0x00, 0xE5, 0xFF), System.Windows.Media.Color.FromRgb(0xA6, 0x4D, 0xFF), 45);
        var img = TryLogo(s.Logo);
        logo.Child = img != null ? img : new TextBlock { Text = s.Name.Length > 0 ? s.Name[..1].ToUpper() : "?", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var fav = new Button { Content = s.Favorite ? "" : "", Style = (Style)FindResource("IconButton"), Width = 30, Height = 30, ToolTip = "Favorite", Foreground = s.Favorite ? Ui.B("AccentBrush") : Ui.B("MutedBrush") };
        fav.Click += (_, _) => { s.Favorite = !s.Favorite; App.Db.SaveStation(s); Reload(); };
        var edit = new Button { Content = "", Style = (Style)FindResource("IconButton"), Width = 30, Height = 30, ToolTip = "Edit" }; edit.Click += (_, _) => Edit(s);
        var del = new Button { Content = "", Style = (Style)FindResource("IconButton"), Width = 30, Height = 30, ToolTip = "Delete" };
        del.Click += (_, _) => { if (MessageBox.Show($"Delete station “{s.Name}”?", "Delete", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { App.Db.DeleteStation(s.Id); Reload(); } };

        var info = Ui.Col(Ui.Text(s.Name, 15, false, true), Ui.Text(string.Join(" · ", new[] { s.Genre, s.Country }.Where(x => x != "")), 12, true));
        ((TextBlock)info.Children[0]).TextTrimming = TextTrimming.CharacterEllipsis; ((TextBlock)info.Children[0]).MaxWidth = 170;
        var top = Ui.Row(logo, info);
        var play = Ui.Button("▶  Play", async (_, _) => await App.Player.PlayStationAsync(s), true);
        play.Margin = new Thickness(0, 10, 8, 0);
        var actions = Ui.Row(play, fav, edit, del);
        var card = Ui.Card(Ui.Col(top, actions), 18); card.Width = 290; card.Margin = new Thickness(0, 0, 16, 16);
        card.ToolTip = string.IsNullOrWhiteSpace(s.Description) ? s.Url : s.Description + "\n" + s.Url;
        card.MouseLeftButtonDown += async (_, e) => { if (e.ClickCount == 2) await App.Player.PlayStationAsync(s); };
        return card;
    }

    private static UIElement? TryLogo(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try { return new System.Windows.Controls.Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(url)) { }, Stretch = System.Windows.Media.Stretch.UniformToFill }; } catch { return null; }
    }

    private void Edit(Station s)
    {
        var w = new Window { Title = s.Id == 0 ? "Add station" : "Edit station", Width = 460, SizeToContent = SizeToContent.Height, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.B("PanelSolid"), ResizeMode = ResizeMode.NoResize };
        TextBox F(string v) => new() { Text = v, Margin = new Thickness(0, 2, 0, 10) };
        var name = F(s.Name); var url = F(s.Url); var desc = F(s.Description); var genre = F(s.Genre); var country = F(s.Country); var logo = F(s.Logo); var site = F(s.Website);
        var stack = new StackPanel { Margin = new Thickness(20) };
        void Add(string label, TextBox t) { stack.Children.Add(Ui.Text(label, 12, true)); stack.Children.Add(t); }
        Add("Station name", name); Add("Stream URL (http/https; .m3u/.pls are resolved)", url); Add("Description", desc); Add("Genre", genre); Add("Country", country); Add("Logo URL", logo); Add("Website", site);
        var err = Ui.Text("", 12); err.Foreground = System.Windows.Media.Brushes.OrangeRed;
        var ok = Ui.Button("Save", async (_, _) =>
        {
            var u = url.Text.Trim();
            if (string.IsNullOrWhiteSpace(name.Text) || !Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) { err.Text = "Enter a name and a valid http(s) stream URL."; return; }
            if (u.EndsWith(".m3u", true, null) || u.EndsWith(".pls", true, null) || u.EndsWith(".m3u8", true, null) && false)
            {
                try { var txt = await Http.GetStringAsync(u); var first = PlaylistParser.Parse(txt, u).FirstOrDefault(); if (first != null) u = first.Url; } catch (Exception ex) { err.Text = "Could not read playlist: " + ex.Message; return; }
            }
            s.Name = name.Text.Trim(); s.Url = u; s.Description = desc.Text; s.Genre = genre.Text; s.Country = country.Text; s.Logo = logo.Text; s.Website = site.Text;
            App.Db.SaveStation(s); w.Close(); Reload();
        }, true);
        stack.Children.Add(err); stack.Children.Add(ok); w.Content = stack; w.ShowDialog();
    }

    private void ImportFile()
    {
        var dlg = new OpenFileDialog { Filter = "Radio playlists|*.m3u;*.m3u8;*.pls" }; if (dlg.ShowDialog() != true) return;
        AddEntries(PlaylistParser.Parse(File.ReadAllText(dlg.FileName), dlg.FileName), Path.GetFileNameWithoutExtension(dlg.FileName));
    }

    private async void ImportUrl()
    {
        var w = new Window { Title = "Import playlist from URL", Width = 460, Height = 160, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.B("PanelSolid"), ResizeMode = ResizeMode.NoResize };
        var tb = new TextBox { Margin = new Thickness(0, 0, 0, 10) }; string? res = null;
        w.Content = new StackPanel { Margin = new Thickness(20), Children = { Ui.Text("URL of an .m3u or .pls playlist", 12, true), tb, Ui.Button("Import", (_, _) => { res = tb.Text; w.Close(); }, true) } };
        w.ShowDialog(); if (string.IsNullOrWhiteSpace(res)) return;
        try { var txt = await Http.GetStringAsync(res.Trim()); AddEntries(PlaylistParser.Parse(txt, res), "Imported"); }
        catch (Exception ex) { MessageBox.Show("Could not download playlist: " + ex.Message); }
    }

    private void AddEntries(List<PlaylistEntry> entries, string fallbackName)
    {
        int n = 0;
        foreach (var e in entries.Where(e => e.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)))
        { App.Db.SaveStation(new Station { Name = e.Title ?? (entries.Count == 1 ? fallbackName : $"{fallbackName} {n + 1}"), Url = e.Url }); n++; }
        Reload(); ((MainWindow)Application.Current.MainWindow).ShowToast($"Imported {n} station(s)");
    }
}
