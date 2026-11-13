using AudioReactor.Core.Audio;
using AudioReactor.Core.Data;
using AudioReactor.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace AudioReactor.App.Services;

/// <summary>Queue, shuffle/repeat and now-playing state on top of <see cref="AudioEngine"/>. UI-thread owned.</summary>
public sealed partial class PlayerService : ObservableObject, IDisposable
{
    private readonly Database _db;
    private readonly AppSettings _settings;
    private readonly Random _rng = new();
    private readonly List<int> _shuffleOrder = new();

    public AudioEngine Engine { get; } = new();
    public List<Track> Queue { get; } = new();

    [ObservableProperty] private int _queueIndex = -1;
    [ObservableProperty] private Track? _currentTrack;
    [ObservableProperty] private Station? _currentStation;
    [ObservableProperty] private string _nowTitle = "Nothing playing";
    [ObservableProperty] private string _nowArtist = "";
    [ObservableProperty] private string _nowAlbum = "";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _shuffle;
    [ObservableProperty] private string _repeat = "Off";
    [ObservableProperty] private string _radioStatus = "";
    [ObservableProperty] private byte[]? _artwork;
    [ObservableProperty] private string _lastError = "";

    /// <summary>kind: track | radio | error</summary>
    public event Action<string, string>? Notify;
    public bool IsRadio => CurrentStation != null;

    public PlayerService(Database db, AppSettings settings)
    {
        _db = db; _settings = settings;
        Shuffle = settings.Shuffle; Repeat = settings.Repeat;
        Engine.Volume = (float)settings.Volume;
        Engine.LatencyMs = settings.LatencyMs;
        Engine.Effects = settings.Effects;
        Engine.EqEnabled = settings.EqEnabled;
        Engine.SetEq(settings.EqGains);
        Engine.SetDevice(settings.OutputDeviceId);
        Engine.TrackEnded += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnTrackEnded());
        Engine.StateChanged += s => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => IsPlaying = s == PlaybackState2.Playing);
        Engine.Error += m => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { LastError = m; Notify?.Invoke("error", m); });
    }

    public void PlayQueue(IEnumerable<Track> tracks, int startIndex = 0)
    {
        Queue.Clear(); Queue.AddRange(tracks);
        RebuildShuffle(startIndex);
        PlayAt(startIndex);
    }

    public void Enqueue(Track t) { Queue.Add(t); RebuildShuffle(QueueIndex); }

    private void RebuildShuffle(int current)
    {
        _shuffleOrder.Clear();
        _shuffleOrder.AddRange(Enumerable.Range(0, Queue.Count).Where(i => i != current).OrderBy(_ => _rng.Next()));
    }

    public void PlayAt(int index)
    {
        if (index < 0 || index >= Queue.Count) return;
        var t = Queue[index];
        try
        {
            Engine.OpenFile(t.Path);
            QueueIndex = index; CurrentTrack = t; CurrentStation = null; RadioStatus = "";
            NowTitle = t.Title; NowArtist = t.Artist; NowAlbum = t.Album;
            Artwork = LibraryScanner.ReadArtwork(t.Path);
            Engine.Play();
            _db.MarkPlayed(t.Id);
            _settings.LastTrackId = t.Id;
            Notify?.Invoke("track", t.Display);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Skipping unplayable track {Path}", t.Path);
            // Skip corrupted/unsupported files automatically, but avoid infinite loops.
            if (Queue.Count > 1 && index != Queue.Count - 1) PlayAt(index + 1);
        }
    }

    public async Task PlayStationAsync(Station s)
    {
        try
        {
            RadioStatus = "Connecting…";
            CurrentTrack = null; CurrentStation = s;
            NowTitle = s.Name; NowArtist = "Internet radio"; NowAlbum = s.Genre; Artwork = null;
            await Engine.OpenRadioAsync(s.Url, _settings.RadioBufferSeconds);
            if (Engine.Radio != null)
            {
                var r = Engine.Radio;
                r.MetadataChanged += t => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => NowArtist = t);
                r.StateChanged += st => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    RadioStatus = st.ToString();
                    if (st == RadioState.Reconnecting) Notify?.Invoke("radio", $"Connection lost to {s.Name}. Reconnecting…");
                    if (st == RadioState.Playing) Notify?.Invoke("radio", $"Connected: {s.Name}");
                });
                if (!string.IsNullOrEmpty(r.NowPlaying)) NowArtist = r.NowPlaying;
            }
            Engine.Play();
            RadioStatus = "Playing";
            _db.MarkStationPlayed(s.Id);
        }
        catch (Exception ex)
        {
            RadioStatus = "Failed";
            LastError = ex.Message;
            Notify?.Invoke("error", $"Could not play {s.Name}: {ex.Message}");
        }
    }

    /// <summary>Loads a queue and opens a track paused (no sound, no play count). Used to restore the previous session.</summary>
    public void PrepareQueue(IEnumerable<Track> tracks, int index, TimeSpan position)
    {
        Queue.Clear(); Queue.AddRange(tracks); RebuildShuffle(index);
        if (index < 0 || index >= Queue.Count) return;
        var t = Queue[index];
        try
        {
            Engine.OpenFile(t.Path); QueueIndex = index; CurrentTrack = t; CurrentStation = null;
            NowTitle = t.Title; NowArtist = t.Artist; NowAlbum = t.Album; Artwork = LibraryScanner.ReadArtwork(t.Path);
            if (position > TimeSpan.Zero && position < Engine.Duration) Engine.Position = position;
        }
        catch (Exception ex) { Log.Warning(ex, "Could not restore last track {Path}", t.Path); }
    }

    public void TogglePlay()
    {
        if (Engine.State == PlaybackState2.Playing) Engine.Pause();
        else if (Engine.State == PlaybackState2.Paused) Engine.Play();
        else if (Queue.Count > 0) PlayAt(Math.Max(0, QueueIndex));
    }

    public void Stop() { Engine.Stop(); }

    public void Next()
    {
        if (Queue.Count == 0 || IsRadio) return;
        int next;
        if (Shuffle)
        {
            if (_shuffleOrder.Count == 0) { if (Repeat == "Off") { Stop(); return; } RebuildShuffle(QueueIndex); }
            if (_shuffleOrder.Count == 0) { PlayAt(QueueIndex); return; }
            next = _shuffleOrder[0]; _shuffleOrder.RemoveAt(0);
        }
        else
        {
            next = QueueIndex + 1;
            if (next >= Queue.Count) { if (Repeat == "All") next = 0; else { Stop(); return; } }
        }
        PlayAt(next);
    }

    public void Previous()
    {
        if (Queue.Count == 0 || IsRadio) return;
        if (Engine.Position.TotalSeconds > 3) { Engine.Position = TimeSpan.Zero; return; }
        PlayAt(QueueIndex <= 0 ? (Repeat == "All" ? Queue.Count - 1 : 0) : QueueIndex - 1);
    }

    private void OnTrackEnded()
    {
        if (Repeat == "One") PlayAt(QueueIndex); else Next();
    }

    public void CycleRepeat() { Repeat = Repeat switch { "Off" => "All", "All" => "One", _ => "Off" }; _settings.Repeat = Repeat; }
    public void ToggleShuffle() { Shuffle = !Shuffle; _settings.Shuffle = Shuffle; }
    public void SetVolume(double v) { Engine.Volume = (float)v; _settings.Volume = v; }

    public void Dispose() => Engine.Dispose();
}
