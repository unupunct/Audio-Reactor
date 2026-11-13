using AudioReactor.Core.Models;
using Microsoft.Data.Sqlite;

namespace AudioReactor.Core.Data;

/// <summary>SQLite persistence for the library, playlists and radio stations. Thread-safe via a single lock.</summary>
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _c;
    private readonly object _lock = new();

    public Database(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _c = new SqliteConnection($"Data Source={path}");
        _c.Open();
        Exec("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS tracks(id INTEGER PRIMARY KEY, path TEXT UNIQUE NOT NULL, title TEXT, artist TEXT, album TEXT, genre TEXT,
              year INTEGER, duration REAL, added TEXT, last_played TEXT, play_count INTEGER DEFAULT 0, favorite INTEGER DEFAULT 0);
            CREATE TABLE IF NOT EXISTS playlists(id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS playlist_tracks(playlist_id INTEGER, track_id INTEGER, pos INTEGER);
            CREATE TABLE IF NOT EXISTS stations(id INTEGER PRIMARY KEY, name TEXT, url TEXT UNIQUE NOT NULL, description TEXT, genre TEXT,
              country TEXT, logo TEXT, website TEXT, favorite INTEGER DEFAULT 0, last_played TEXT);
            CREATE TABLE IF NOT EXISTS history(id INTEGER PRIMARY KEY, kind TEXT, ref TEXT, at TEXT);
            """);
    }

    private void Exec(string sql, params (string, object?)[] p)
    {
        lock (_lock)
        {
            using var cmd = _c.CreateCommand(); cmd.CommandText = sql;
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] p)
    {
        lock (_lock)
        {
            using var cmd = _c.CreateCommand(); cmd.CommandText = sql;
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            var l = new List<T>();
            while (r.Read()) l.Add(map(r));
            return l;
        }
    }

    private long Scalar(string sql, params (string, object?)[] p)
    {
        lock (_lock)
        {
            using var cmd = _c.CreateCommand(); cmd.CommandText = sql;
            foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
            return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
        }
    }

    // ---------- Tracks ----------
    private const string TrackCols = "id,path,title,artist,album,genre,year,duration,added,last_played,play_count,favorite";
    private static Track MapTrack(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), Path = r.GetString(1), Title = r.IsDBNull(2) ? "" : r.GetString(2), Artist = r.IsDBNull(3) ? "" : r.GetString(3),
        Album = r.IsDBNull(4) ? "" : r.GetString(4), Genre = r.IsDBNull(5) ? "" : r.GetString(5), Year = r.IsDBNull(6) ? 0 : r.GetInt32(6),
        DurationSec = r.IsDBNull(7) ? 0 : r.GetDouble(7), DateAdded = DateTime.TryParse(r.IsDBNull(8) ? null : r.GetString(8), out var d) ? d : DateTime.UtcNow,
        LastPlayed = r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9)), PlayCount = r.GetInt32(10), Favorite = r.GetInt32(11) != 0
    };

    /// <summary>Inserts or updates by path; returns track id.</summary>
    public long UpsertTrack(Track t)
    {
        Exec("""
            INSERT INTO tracks(path,title,artist,album,genre,year,duration,added) VALUES($p,$t,$a,$al,$g,$y,$d,$add)
            ON CONFLICT(path) DO UPDATE SET title=$t,artist=$a,album=$al,genre=$g,year=$y,duration=$d
            """, ("$p", t.Path), ("$t", t.Title), ("$a", t.Artist), ("$al", t.Album), ("$g", t.Genre), ("$y", t.Year), ("$d", t.DurationSec),
            ("$add", t.DateAdded.ToString("o")));
        t.Id = Scalar("SELECT id FROM tracks WHERE path=$p", ("$p", t.Path));
        return t.Id;
    }

    public List<Track> GetTracks(string? search = null, string orderBy = "artist")
    {
        string order = orderBy switch { "album" => "album,artist,title", "genre" => "genre,artist,title", "date" => "added DESC", "title" => "title", _ => "artist,album,title" };
        if (string.IsNullOrWhiteSpace(search)) return Query($"SELECT {TrackCols} FROM tracks ORDER BY {order} COLLATE NOCASE", MapTrack);
        return Query($"SELECT {TrackCols} FROM tracks WHERE title LIKE $s OR artist LIKE $s OR album LIKE $s OR genre LIKE $s ORDER BY {order} COLLATE NOCASE",
            MapTrack, ("$s", $"%{search}%"));
    }

    public List<Track> GetSmart(string kind) => kind switch
    {
        "recent" => Query($"SELECT {TrackCols} FROM tracks ORDER BY added DESC LIMIT 100", MapTrack),
        "played" => Query($"SELECT {TrackCols} FROM tracks WHERE last_played IS NOT NULL ORDER BY last_played DESC LIMIT 100", MapTrack),
        "most" => Query($"SELECT {TrackCols} FROM tracks WHERE play_count>0 ORDER BY play_count DESC LIMIT 100", MapTrack),
        "favorites" => Query($"SELECT {TrackCols} FROM tracks WHERE favorite=1 ORDER BY artist,title", MapTrack),
        _ => new()
    };

    public List<string> GetGenres() => Query("SELECT DISTINCT genre FROM tracks WHERE genre<>'' ORDER BY genre", r => r.GetString(0));
    public List<Track> GetByGenre(string g) => Query($"SELECT {TrackCols} FROM tracks WHERE genre=$g ORDER BY artist,title", MapTrack, ("$g", g));
    public List<string> GetArtists() => Query("SELECT DISTINCT artist FROM tracks WHERE artist<>'' ORDER BY artist COLLATE NOCASE", r => r.GetString(0));
    public List<Track> GetByArtist(string a) => Query($"SELECT {TrackCols} FROM tracks WHERE artist=$a ORDER BY album,title", MapTrack, ("$a", a));
    public int TrackCount() => (int)Scalar("SELECT COUNT(*) FROM tracks");
    public bool HasTrack(string path) => Scalar("SELECT COUNT(*) FROM tracks WHERE path=$p", ("$p", path)) > 0;
    public void DeleteTrack(long id) { Exec("DELETE FROM tracks WHERE id=$i", ("$i", id)); Exec("DELETE FROM playlist_tracks WHERE track_id=$i", ("$i", id)); }
    public void SetFavorite(long id, bool fav) => Exec("UPDATE tracks SET favorite=$f WHERE id=$i", ("$f", fav ? 1 : 0), ("$i", id));
    public void MarkPlayed(long id)
    {
        Exec("UPDATE tracks SET play_count=play_count+1,last_played=$n WHERE id=$i", ("$n", DateTime.UtcNow.ToString("o")), ("$i", id));
        Exec("INSERT INTO history(kind,ref,at) VALUES('track',$r,$n)", ("$r", id.ToString()), ("$n", DateTime.UtcNow.ToString("o")));
    }

    // ---------- Playlists ----------
    public long CreatePlaylist(string name) { Exec("INSERT INTO playlists(name) VALUES($n)", ("$n", name)); return Scalar("SELECT last_insert_rowid()"); }
    public void RenamePlaylist(long id, string name) => Exec("UPDATE playlists SET name=$n WHERE id=$i", ("$n", name), ("$i", id));
    public void DeletePlaylist(long id) { Exec("DELETE FROM playlists WHERE id=$i", ("$i", id)); Exec("DELETE FROM playlist_tracks WHERE playlist_id=$i", ("$i", id)); }

    public List<Playlist> GetPlaylists() => Query("""
        SELECT p.id,p.name,COUNT(t.id),COALESCE(SUM(t.duration),0) FROM playlists p
        LEFT JOIN playlist_tracks pt ON pt.playlist_id=p.id LEFT JOIN tracks t ON t.id=pt.track_id
        GROUP BY p.id ORDER BY p.name COLLATE NOCASE
        """, r => new Playlist { Id = r.GetInt64(0), Name = r.GetString(1), TrackCount = r.GetInt32(2), DurationSec = r.GetDouble(3) });

    public void AddToPlaylist(long playlistId, long trackId)
    {
        long pos = Scalar("SELECT COALESCE(MAX(pos),-1)+1 FROM playlist_tracks WHERE playlist_id=$p", ("$p", playlistId));
        Exec("INSERT INTO playlist_tracks(playlist_id,track_id,pos) VALUES($p,$t,$o)", ("$p", playlistId), ("$t", trackId), ("$o", pos));
    }

    public void RemoveFromPlaylist(long playlistId, int position)
    {
        Exec("DELETE FROM playlist_tracks WHERE playlist_id=$p AND pos=$o", ("$p", playlistId), ("$o", position));
        Exec("UPDATE playlist_tracks SET pos=pos-1 WHERE playlist_id=$p AND pos>$o", ("$p", playlistId), ("$o", position));
    }

    public List<Track> GetPlaylistTracks(long playlistId) => Query($"""
        SELECT {string.Join(',', TrackCols.Split(',').Select(c => "t." + c))} FROM playlist_tracks pt JOIN tracks t ON t.id=pt.track_id
        WHERE pt.playlist_id=$p ORDER BY pt.pos
        """, MapTrack, ("$p", playlistId));

    /// <summary>Replaces the order of a playlist with the given track ids.</summary>
    public void SetPlaylistOrder(long playlistId, IList<long> trackIds)
    {
        Exec("DELETE FROM playlist_tracks WHERE playlist_id=$p", ("$p", playlistId));
        for (int i = 0; i < trackIds.Count; i++)
            Exec("INSERT INTO playlist_tracks(playlist_id,track_id,pos) VALUES($p,$t,$o)", ("$p", playlistId), ("$t", trackIds[i]), ("$o", i));
    }

    // ---------- Stations ----------
    private const string StCols = "id,name,url,description,genre,country,logo,website,favorite,last_played";
    private static Station MapStation(SqliteDataReader r)
    {
        string S(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
        return new Station { Id = r.GetInt64(0), Name = S(1), Url = S(2), Description = S(3), Genre = S(4), Country = S(5), Logo = S(6), Website = S(7),
            Favorite = r.GetInt32(8) != 0, LastPlayed = r.IsDBNull(9) ? null : DateTime.Parse(r.GetString(9)) };
    }

    public long SaveStation(Station s)
    {
        if (s.Id == 0)
        {
            Exec("INSERT OR IGNORE INTO stations(name,url,description,genre,country,logo,website,favorite) VALUES($n,$u,$d,$g,$c,$l,$w,$f)",
                ("$n", s.Name), ("$u", s.Url), ("$d", s.Description), ("$g", s.Genre), ("$c", s.Country), ("$l", s.Logo), ("$w", s.Website), ("$f", s.Favorite ? 1 : 0));
            s.Id = Scalar("SELECT id FROM stations WHERE url=$u", ("$u", s.Url));
        }
        else Exec("UPDATE stations SET name=$n,url=$u,description=$d,genre=$g,country=$c,logo=$l,website=$w,favorite=$f WHERE id=$i",
            ("$n", s.Name), ("$u", s.Url), ("$d", s.Description), ("$g", s.Genre), ("$c", s.Country), ("$l", s.Logo), ("$w", s.Website), ("$f", s.Favorite ? 1 : 0), ("$i", s.Id));
        return s.Id;
    }

    public List<Station> GetStations(string? search = null)
    {
        if (string.IsNullOrWhiteSpace(search)) return Query($"SELECT {StCols} FROM stations ORDER BY favorite DESC,name COLLATE NOCASE", MapStation);
        return Query($"SELECT {StCols} FROM stations WHERE name LIKE $s OR genre LIKE $s OR country LIKE $s OR description LIKE $s ORDER BY name COLLATE NOCASE",
            MapStation, ("$s", $"%{search}%"));
    }

    public List<Station> GetRecentStations() => Query($"SELECT {StCols} FROM stations WHERE last_played IS NOT NULL ORDER BY last_played DESC LIMIT 20", MapStation);
    public void DeleteStation(long id) => Exec("DELETE FROM stations WHERE id=$i", ("$i", id));
    public void MarkStationPlayed(long id)
    {
        Exec("UPDATE stations SET last_played=$n WHERE id=$i", ("$n", DateTime.UtcNow.ToString("o")), ("$i", id));
        Exec("INSERT INTO history(kind,ref,at) VALUES('station',$r,$n)", ("$r", id.ToString()), ("$n", DateTime.UtcNow.ToString("o")));
    }

    public void Dispose() { lock (_lock) _c.Dispose(); }
}
