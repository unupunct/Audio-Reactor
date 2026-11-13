using System.IO;
using System.Windows;
using System.Windows.Controls;
using AudioReactor.Core.Data;
using AudioReactor.Core.Models;
using AudioReactor.Core.Playlists;
using Microsoft.Win32;

namespace AudioReactor.App.Views;

public sealed class PlaylistsView : UserControl
{
    private readonly ListBox _lists = new() { MinWidth = 240 };
    private readonly ListBox _tracks = new();
    private readonly TextBlock _info = Ui.Text("", 12, true);
    private List<Playlist> _pls = new();
    private List<Track> _cur = new();
    private enum Smart { Genre, Artist }

    public PlaylistsView()
    {
        _lists.DisplayMemberPath = "Name";
        _lists.SelectionChanged += (_, _) => LoadTracks();
        _tracks.MouseDoubleClick += (_, _) => { if (_tracks.SelectedIndex >= 0) App.Player.PlayQueue(_cur, _tracks.SelectedIndex); };

        var buttons = Ui.Row(Ui.Button("New", (_, _) => Create(), true), Ui.Button("Rename", (_, _) => Rename()), Ui.Button("Delete", (_, _) => Delete()),
            Ui.Button("Import…", (_, _) => Import()), Ui.Button("Export…", (_, _) => Export()), Ui.Button("Save queue", (_, _) => SaveQueue()));
        var trackBtns = Ui.Row(Ui.Button("Play", (_, _) => { if (_cur.Count > 0) App.Player.PlayQueue(_cur, Math.Max(0, _tracks.SelectedIndex)); }, true),
            Ui.Button("Move up", (_, _) => Move(-1)), Ui.Button("Move down", (_, _) => Move(1)), Ui.Button("Remove", (_, _) => RemoveSel()));

        var smart = new ComboBox { Width = 200, Margin = new Thickness(0, 0, 8, 0) };
        smart.ItemsSource = new[] { "Smart: by genre…", "Smart: by artist…", "Recently added", "Frequently played", "Favorites" }; smart.SelectedIndex = 0;
        smart.SelectionChanged += (_, _) => { SmartPick(smart.SelectedIndex); smart.SelectedIndex = 0; };

        var left = Ui.Col(Ui.Text("Your playlists", 13, true), _lists); left.Margin = new Thickness(0, 0, 18, 0);
        var right = new DockPanel();
        var rh = Ui.Col(_info, trackBtns); rh.Margin = new Thickness(0, 0, 0, 10); DockPanel.SetDock(rh, Dock.Top); right.Children.Add(rh);
        right.Children.Add(_tracks);

        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var lc = Ui.Card(new DockPanel { Children = { _lists } }, 16, new Thickness(8)); body.Children.Add(lc);
        var rc = Ui.Card(right); Grid.SetColumn(rc, 1); rc.Margin = new Thickness(16, 0, 0, 0); body.Children.Add(rc);

        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 12) };
        var head = Ui.Col(Ui.Title("Playlists"), Ui.Row(buttons, smart)); head.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head); root.Children.Add(body);
        Content = root;
        Reload();
    }

    private static string? Ask(string title, string initial = "")
    {
        var w = new Window { Title = title, Width = 380, Height = 150, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, Background = Ui.B("PanelSolid"), ResizeMode = ResizeMode.NoResize };
        var tb = new TextBox { Text = initial, Margin = new Thickness(0, 0, 0, 12) }; string? res = null;
        var ok = Ui.Button("OK", (_, _) => { res = tb.Text; w.Close(); }, true);
        w.Content = new StackPanel { Margin = new Thickness(18), Children = { tb, ok } };
        tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        w.ShowDialog(); return string.IsNullOrWhiteSpace(res) ? null : res.Trim();
    }

    private void Reload(long? select = null)
    {
        _pls = App.Db.GetPlaylists();
        _lists.ItemsSource = null; _lists.ItemsSource = _pls;
        if (select != null) _lists.SelectedItem = _pls.FirstOrDefault(p => p.Id == select);
        else if (_pls.Count > 0 && _lists.SelectedIndex < 0) _lists.SelectedIndex = 0;
    }

    private Playlist? Sel => _lists.SelectedItem as Playlist;

    private void LoadTracks()
    {
        if (Sel == null) { _cur = new(); _tracks.ItemsSource = null; _info.Text = "Create a playlist to get started."; return; }
        _cur = App.Db.GetPlaylistTracks(Sel.Id);
        _tracks.ItemsSource = _cur.Select((t, i) => $"{i + 1}.  {t.Display}   [{t.DurationText}]").ToList();
        var d = TimeSpan.FromSeconds(_cur.Sum(t => t.DurationSec));
        _info.Text = $"{Sel.Name} · {_cur.Count} tracks · {(int)d.TotalHours}h {d.Minutes}m {d.Seconds}s";
    }

    private void Create() { var n = Ask("New playlist"); if (n != null) Reload(App.Db.CreatePlaylist(n)); }
    private void Rename() { if (Sel == null) return; var n = Ask("Rename playlist", Sel.Name); if (n != null) { var id = Sel.Id; App.Db.RenamePlaylist(id, n); Reload(id); } }
    private void Delete()
    {
        if (Sel == null) return;
        if (MessageBox.Show($"Delete playlist “{Sel.Name}”? Your music files are not affected.", "Delete playlist", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
        { App.Db.DeletePlaylist(Sel.Id); _lists.SelectedIndex = -1; Reload(); LoadTracks(); }
    }

    private void Move(int d)
    {
        if (Sel == null || _tracks.SelectedIndex < 0) return;
        int i = _tracks.SelectedIndex, j = i + d; if (j < 0 || j >= _cur.Count) return;
        (_cur[i], _cur[j]) = (_cur[j], _cur[i]);
        App.Db.SetPlaylistOrder(Sel.Id, _cur.Select(t => t.Id).ToList()); LoadTracks(); _tracks.SelectedIndex = j;
    }

    private void RemoveSel() { if (Sel == null || _tracks.SelectedIndex < 0) return; var id = Sel.Id; App.Db.RemoveFromPlaylist(id, _tracks.SelectedIndex); Reload(id); LoadTracks(); }

    private void SaveQueue()
    {
        if (App.Player.Queue.Count == 0) { ((MainWindow)Application.Current.MainWindow).ShowToast("The queue is empty."); return; }
        var n = Ask("Save queue as playlist", "Queue " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")); if (n == null) return;
        var id = App.Db.CreatePlaylist(n); foreach (var t in App.Player.Queue) App.Db.AddToPlaylist(id, t.Id); Reload(id);
    }

    private void SmartPick(int idx)
    {
        List<Track>? tracks = null; string name = "";
        switch (idx)
        {
            case 1: { var g = App.Db.GetGenres(); var pick = PickFrom("Genre", g); if (pick != null) { tracks = App.Db.GetByGenre(pick); name = "Genre: " + pick; } break; }
            case 2: { var a = App.Db.GetArtists(); var pick = PickFrom("Artist", a); if (pick != null) { tracks = App.Db.GetByArtist(pick); name = "Artist: " + pick; } break; }
            case 3: tracks = App.Db.GetSmart("recent"); name = "Recently added"; break;
            case 4: tracks = App.Db.GetSmart("most"); name = "Frequently played"; break;
            case 5: tracks = App.Db.GetSmart("favorites"); name = "Favorites"; break;
        }
        if (tracks == null || tracks.Count == 0) { if (idx > 0) ((MainWindow)Application.Current.MainWindow).ShowToast("No matching tracks yet."); return; }
        var id = App.Db.CreatePlaylist(name); foreach (var t in tracks) App.Db.AddToPlaylist(id, t.Id); Reload(id);
    }

    private static string? PickFrom(string title, List<string> items)
    {
        if (items.Count == 0) return null;
        var w = new Window { Title = title, Width = 340, Height = 420, Owner = Application.Current.MainWindow, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Ui.B("PanelSolid") };
        var lb = new ListBox { ItemsSource = items }; string? res = null;
        lb.MouseDoubleClick += (_, _) => { res = lb.SelectedItem as string; w.Close(); };
        w.Content = lb; w.ShowDialog(); return res;
    }

    private void Import()
    {
        var dlg = new OpenFileDialog { Filter = "Playlists|*.m3u;*.m3u8;*.pls" }; if (dlg.ShowDialog() != true) return;
        var entries = PlaylistParser.Parse(File.ReadAllText(dlg.FileName), dlg.FileName);
        var baseDir = Path.GetDirectoryName(dlg.FileName)!; var id = App.Db.CreatePlaylist(Path.GetFileNameWithoutExtension(dlg.FileName)); int added = 0;
        foreach (var en in entries)
        {
            var path = Path.IsPathRooted(en.Url) ? en.Url : Path.GetFullPath(Path.Combine(baseDir, en.Url));
            if (!File.Exists(path)) continue;
            if (!App.Db.HasTrack(path)) { var t = LibraryScanner.ReadTrack(path); if (t == null) continue; App.Db.UpsertTrack(t); }
            var tr = App.Db.GetTracks().First(x => x.Path == path); App.Db.AddToPlaylist(id, tr.Id); added++;
        }
        Reload(id); ((MainWindow)Application.Current.MainWindow).ShowToast($"Imported {added} of {entries.Count} entries");
    }

    private void Export()
    {
        if (Sel == null) return;
        var dlg = new SaveFileDialog { Filter = "M3U playlist|*.m3u8", FileName = Sel.Name + ".m3u8" }; if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, PlaylistParser.ToM3u(_cur.Select(t => (t.Path, t.Display, (int)t.DurationSec))), new System.Text.UTF8Encoding(true));
    }
}
