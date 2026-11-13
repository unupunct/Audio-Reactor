using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AudioReactor.Core.Data;
using AudioReactor.Core.Models;
using Microsoft.Win32;

namespace AudioReactor.App.Views;

public sealed class LibraryView : UserControl
{
    private readonly ListView _list = new();
    private readonly TextBox _search = new() { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox _sort = new() { Width = 130, Margin = new Thickness(0, 0, 12, 0) };
    private readonly ComboBox _view = new() { Width = 150, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBlock _count = Ui.Text("", 12, true);
    private List<Track> _tracks = new();

    public LibraryView()
    {
        _sort.ItemsSource = new[] { "artist", "album", "genre", "date", "title" }; _sort.SelectedIndex = 0;
        _view.ItemsSource = new[] { "All music", "Recently added", "Recently played", "Most played", "Favorites" }; _view.SelectedIndex = 0;

        var grid = new GridView();
        void Col(string h, string path, double w) => grid.Columns.Add(new GridViewColumn { Header = h, DisplayMemberBinding = new System.Windows.Data.Binding(path), Width = w });
        Col("Title", "Title", 280); Col("Artist", "Artist", 200); Col("Album", "Album", 200); Col("Genre", "Genre", 110); Col("Time", "DurationText", 70); Col("Plays", "PlayCount", 55);
        _list.View = grid; _list.Background = System.Windows.Media.Brushes.Transparent; _list.BorderThickness = new Thickness(0);
        _list.Foreground = Ui.B("TextBrush"); _list.SelectionMode = SelectionMode.Extended;
        _list.MouseDoubleClick += (_, _) => PlaySelected();

        var menu = new ContextMenu();
        void Item(string t, Action a) { var mi = new MenuItem { Header = t }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Item("Play", PlaySelected);
        Item("Add to queue", () => { foreach (Track t in _list.SelectedItems) App.Player.Enqueue(t); });
        Item("Toggle favorite", () => { foreach (Track t in _list.SelectedItems) App.Db.SetFavorite(t.Id, !t.Favorite); Reload(); });
        Item("Add to playlist…", AddToPlaylist);
        Item("Remove from library", () => { foreach (Track t in _list.SelectedItems.Cast<Track>().ToList()) App.Db.DeleteTrack(t.Id); Reload(); });
        _list.ContextMenu = menu;

        _search.TextChanged += (_, _) => Reload();
        _sort.SelectionChanged += (_, _) => Reload();
        _view.SelectionChanged += (_, _) => Reload();

        var bar = Ui.Row(Ui.Button("Add files", AddFiles, true), Ui.Button("Add folder", AddFolder), Ui.WithHint(_search, "Search title, artist, album…"), _sort, _view, _count);
        var root = new DockPanel { Margin = new Thickness(28, 22, 28, 12) };
        var head = Ui.Col(Ui.Title("Music Library"), bar); head.Margin = new Thickness(0, 0, 0, 12);
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        root.Children.Add(Ui.Card(_list, 16, new Thickness(6)));
        Content = root;
        Reload();
        Loaded += (_, _) => { if (_list.Items.Count == 0) _count.Text = "Library is empty — add files or a folder, or drag and drop music onto the window."; };
    }

    private void Reload()
    {
        var sort = _sort.SelectedItem as string ?? "artist";
        _tracks = _view.SelectedIndex switch
        {
            1 => App.Db.GetSmart("recent"), 2 => App.Db.GetSmart("played"), 3 => App.Db.GetSmart("most"), 4 => App.Db.GetSmart("favorites"),
            _ => App.Db.GetTracks(_search.Text, sort)
        };
        if (_view.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(_search.Text))
            _tracks = _tracks.Where(t => t.Display.Contains(_search.Text, StringComparison.OrdinalIgnoreCase) || t.Album.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.ItemsSource = _tracks;
        var total = TimeSpan.FromSeconds(_tracks.Sum(t => t.DurationSec));
        _count.Text = $"{_tracks.Count} tracks · {(int)total.TotalHours}h {total.Minutes}m";
    }

    private void PlaySelected()
    {
        if (_list.SelectedIndex < 0) return;
        App.Player.PlayQueue(_tracks, _list.SelectedIndex);
    }

    private async void AddFiles(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Audio files|*.mp3;*.wav;*.flac;*.ogg;*.aac;*.m4a;*.wma;*.opus|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        await ImportAsync(dlg.FileNames);
    }

    private async void AddFolder(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a music folder" };
        if (dlg.ShowDialog() != true) return;
        if (!App.Settings.LibraryFolders.Contains(dlg.FolderName)) App.Settings.LibraryFolders.Add(dlg.FolderName);
        await ImportAsync(new[] { dlg.FolderName });
    }

    private async Task ImportAsync(IEnumerable<string> paths)
    {
        var prog = new Progress<int>(n => _count.Text = $"Scanning… {n} tracks");
        var list = paths.ToList();
        int n = await Task.Run(() => LibraryScanner.Import(App.Db, list, prog));
        Reload();
        ((MainWindow)Application.Current.MainWindow).ShowToast($"Imported {n} track(s)");
    }

    private void AddToPlaylist()
    {
        var pls = App.Db.GetPlaylists();
        if (pls.Count == 0) { ((MainWindow)Application.Current.MainWindow).ShowToast("Create a playlist first (Playlists page)."); return; }
        var menu = new ContextMenu { PlacementTarget = _list, IsOpen = true };
        foreach (var p in pls)
        {
            var mi = new MenuItem { Header = p.Name }; var pid = p.Id;
            mi.Click += (_, _) => { foreach (Track t in _list.SelectedItems) App.Db.AddToPlaylist(pid, t.Id); };
            menu.Items.Add(mi);
        }
    }
}
