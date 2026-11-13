using AudioReactor.Core.Data;
using AudioReactor.Core.Models;
using NAudio.Wave;
using Xunit;

namespace AudioReactor.Tests;

public class DatabaseTests
{
    [Fact]
    public void TracksPlaylistsAndSmartLists()
    {
        using var db = new Database(":memory:");
        var a = new Track { Path = "a.mp3", Title = "A", Artist = "X", Genre = "Rock", DurationSec = 60 };
        var b = new Track { Path = "b.mp3", Title = "B", Artist = "Y", Genre = "Jazz", DurationSec = 120 };
        db.UpsertTrack(a); db.UpsertTrack(b);
        db.UpsertTrack(new Track { Path = "a.mp3", Title = "A2", Artist = "X", Genre = "Rock", DurationSec = 60 }); // update, not duplicate
        Assert.Equal(2, db.TrackCount());
        Assert.Equal("A2", db.GetTracks(search: "A2")[0].Title);
        Assert.Equal(new[] { "Jazz", "Rock" }, db.GetGenres());

        db.MarkPlayed(a.Id); db.MarkPlayed(a.Id); db.SetFavorite(b.Id, true);
        Assert.Equal(a.Id, db.GetSmart("most")[0].Id);
        Assert.Equal(b.Id, db.GetSmart("favorites")[0].Id);

        long pl = db.CreatePlaylist("Mix");
        db.AddToPlaylist(pl, a.Id); db.AddToPlaylist(pl, b.Id);
        var info = db.GetPlaylists().Single();
        Assert.Equal(2, info.TrackCount); Assert.Equal(180, info.DurationSec);
        db.SetPlaylistOrder(pl, new[] { b.Id, a.Id });
        Assert.Equal(b.Id, db.GetPlaylistTracks(pl)[0].Id);
        db.RemoveFromPlaylist(pl, 0);
        Assert.Single(db.GetPlaylistTracks(pl));
        db.RenamePlaylist(pl, "Renamed"); Assert.Equal("Renamed", db.GetPlaylists()[0].Name);
        db.DeletePlaylist(pl); Assert.Empty(db.GetPlaylists());
    }

    [Fact]
    public void Stations_CrudAndSearch()
    {
        using var db = new Database(":memory:");
        var s = new Station { Name = "Jazz FM", Url = "http://x/stream", Genre = "Jazz", Country = "UK" };
        db.SaveStation(s);
        Assert.Single(db.GetStations("jazz"));
        s.Favorite = true; s.Name = "Jazz FM 2"; db.SaveStation(s);
        Assert.Equal("Jazz FM 2", db.GetStations()[0].Name);
        db.MarkStationPlayed(s.Id);
        Assert.Single(db.GetRecentStations());
        db.DeleteStation(s.Id); Assert.Empty(db.GetStations());
    }

    [Fact]
    public void Scanner_ReadsWavAndSkipsCorruptFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ar_scan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var gen = new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 440 };
            WaveFileWriter.CreateWaveFile16(Path.Combine(dir, "tone.wav"), gen.Take(TimeSpan.FromSeconds(2)));
            File.WriteAllText(Path.Combine(dir, "broken.mp3"), "not audio at all");
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "ignored");
            using var db = new Database(":memory:");
            int n = LibraryScanner.Import(db, new[] { dir });
            Assert.Equal(1, n);
            var t = db.GetTracks().Single();
            Assert.Equal("tone", t.Title);
            Assert.InRange(t.DurationSec, 1.9, 2.1);
        }
        finally { Directory.Delete(dir, true); }
    }
}
