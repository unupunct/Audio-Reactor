using AudioReactor.Core.Analysis;
using NAudio.CoreAudioApi;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Serilog;

namespace AudioReactor.Core.Audio;

public enum PlaybackState2 { Stopped, Playing, Paused }

/// <summary>
/// Owns the output device and the sample chain: source -> EQ -> tap/volume -> WASAPI.
/// The analyzer is fed from the audio thread; analysis itself runs on the caller's (render) thread.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private WasapiOut? _out;
    private WaveStream? _reader;            // only used for the Media Foundation radio fallback
    private TrackMixer? _mixer;             // gapless / crossfade playback of local files
    private bool _live;                     // true while playing radio
    private float _crossfade;
    private RadioSource? _radio;
    private Equalizer? _eq;
    private EffectsChain? _fx;
    private SpeedProvider? _speed;
    private DeviceWatcher? _watcher;
    private float _playbackSpeed = 1f;
    private TapProvider? _tap;
    private ISampleProvider? _lastProvider;
    private string? _deviceId;
    private int _latencyMs = 100;
    private float _volume = 0.8f;
    private bool _eqEnabled = true;
    private float[] _gains = new float[10];
    private bool _stopping;

    public AudioAnalyzer Analyzer { get; } = new();
    /// <summary>Live effects settings; edits apply immediately to the playing stream.</summary>
    public EffectsSettings Effects { get; set; } = new();
    private Recording.AudioRecorder? _recorder;
    /// <summary>Attach/detach an audio recorder; takes effect immediately and survives track changes.</summary>
    public Recording.AudioRecorder? Recorder { get => _recorder; set { _recorder = value; if (_tap != null) _tap.Recorder = value; } }
    public const int InternalRate = 48000;
    public float GainReductionDb => _fx?.GainReductionDb ?? 0;
    public PlaybackState2 State { get; private set; } = PlaybackState2.Stopped;
    /// <summary>Loudness leveling / noise cleaning options. Always applied to radio; to local files only when <see cref="SoundCleanSettings.ApplyToFiles"/> is on.</summary>
    public SoundCleanSettings SoundClean { get; set; } = new();
    public bool IsRadio => _radio != null || _live;
    public RadioSource? Radio => _radio;
    public TimeSpan Position { get => _mixer != null ? _mixer.Position : (_reader?.CurrentTime ?? TimeSpan.Zero); set { if (_mixer != null) _mixer.Seek(value); else if (_reader?.CanSeek == true) _reader.CurrentTime = value; } }
    public TimeSpan Duration => _mixer != null ? _mixer.Duration : (_reader?.TotalTime ?? TimeSpan.Zero);
    /// <summary>Crossfade between tracks in seconds (0 = off). Only used when a next track has been queued with <see cref="PrepareNext"/>.</summary>
    public float CrossfadeSeconds { get => _crossfade; set { _crossfade = Math.Clamp(value, 0, 12); if (_mixer != null) _mixer.CrossfadeSeconds = _crossfade; } }
    /// <summary>Raised (audio thread) when playback moved on to the queued next track without a gap. Argument: its file path.</summary>
    public event Action<string?>? NextTrackStarted;
    public float Volume { get => _volume; set { _volume = Math.Clamp(value, 0, 1); if (_tap != null) _tap.Volume = _volume; } }
    public bool EqEnabled { get => _eqEnabled; set { _eqEnabled = value; if (_eq != null) _eq.Enabled = value; } }
    public int LatencyMs { get => _latencyMs; set => _latencyMs = Math.Clamp(value, 20, 1000); }

    /// <summary>0.5x..2x. Changes pitch along with speed. Applies to files only.</summary>
    public float PlaybackSpeed { get => _playbackSpeed; set { _playbackSpeed = Math.Clamp(value, 0.5f, 2f); if (_speed != null) _speed.Speed = IsRadio ? 1f : _playbackSpeed; } }
    public event Action<string, string>? DeviceAdded;
    public event Action<string>? DeviceRemoved;
    /// <summary>Raised after the engine automatically moved playback to the default device because the selected one disappeared.</summary>
    public event Action? DeviceRecovered;
    public event Action? TrackEnded;
    public event Action<string>? Error;
    public event Action<PlaybackState2>? StateChanged;

    public static IReadOnlyList<(string Id, string Name)> GetOutputDevices()
    {
        using var en = new MMDeviceEnumerator();
        return en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => (d.ID, d.FriendlyName)).ToList();
    }

    public static IReadOnlyList<(string Id, string Name)> GetCaptureDevices()
    {
        using var en = new MMDeviceEnumerator();
        return en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => (d.ID, d.FriendlyName)).ToList();
    }

    /// <summary>Switch output device without losing the current source/position.</summary>
    public void SetDevice(string? id)
    {
        _deviceId = string.IsNullOrEmpty(id) ? null : id;
        if (_out != null && _lastProvider != null)
        {
            var was = State;
            BuildOutput(_lastProvider);
            if (was == PlaybackState2.Playing) Play();
        }
    }

    /// <summary>Start listening for device changes (hot-plug) and recover automatically if the selected output disappears.</summary>
    public void StartDeviceWatching()
    {
        if (_watcher != null) return;
        _watcher = new DeviceWatcher();
        _watcher.DeviceAdded += (id, name) => DeviceAdded?.Invoke(id, name);
        _watcher.DeviceRemoved += id =>
        {
            DeviceRemoved?.Invoke(id);
            if (_deviceId != null && id == _deviceId) Recover();
        };
        _watcher.DefaultOutputChanged += () => { if (_deviceId == null && _out != null && State == PlaybackState2.Playing) Recover(); };
    }

    private void Recover()
    {
        try
        {
            var was = State; var pos = _reader?.CurrentTime; var src = _lastProvider;
            _deviceId = null;
            if (src == null) return;
            BuildOutput(src);
            if (was == PlaybackState2.Playing) Play();
            DeviceRecovered?.Invoke();
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Device recovery failed"); Error?.Invoke("Audio device lost: " + ex.Message); }
    }

    public void SetEq(float[] gains) { _gains = (float[])gains.Clone(); _eq?.SetGains(_gains); }

    public void OpenFile(string path)
    {
        Stop();
        try
        {
            var src = TrackSource.FromFile(path);
            _mixer = new TrackMixer { CrossfadeSeconds = _crossfade };
            _mixer.AdvancedToNext += s => NextTrackStarted?.Invoke(s.Path);
            _mixer.SetCurrent(src);
            BuildOutput(_mixer);
        }
        catch (Exception ex)
        {
            _mixer?.Dispose(); _mixer = null;
            Log.Error(ex, "Cannot open {Path}", path);
            Error?.Invoke($"Cannot play {Path.GetFileName(path)}: {ex.Message}");
            throw;
        }
    }

    public enum PrepareResult { Ok, Busy, Failed, NoCurrentTrack }

    /// <summary>Opens the next file in advance so playback can continue into it with no gap (or crossfade). Replaces any earlier choice.</summary>
    public PrepareResult PrepareNext(string path)
    {
        var m = _mixer; if (m == null) return PrepareResult.NoCurrentTrack;
        TrackSource src;
        try { src = TrackSource.FromFile(path); }
        catch (Exception ex) { Log.Warning(ex, "Cannot pre-open next track {Path}", path); return PrepareResult.Failed; }
        if (m.SetNext(src) == TrackMixer.NextResult.Busy) { src.Dispose(); return PrepareResult.Busy; }
        return PrepareResult.Ok;
    }

    /// <summary>Forget the queued next track (used when gapless is off or the queue ends).</summary>
    public void ClearNext() => _mixer?.SetNext(null);

    public async Task OpenRadioAsync(string url, int bufferSeconds)
    {
        Stop(); _live = true;
        var radio = new RadioSource(url, bufferSeconds);
        radio.Error += m => Error?.Invoke(m);
        try
        {
            ISampleProvider sp;
            try { sp = await radio.StartAsync(); _radio = radio; }
            catch (Exception)
            {
                // Fallback for AAC/HLS/Ogg etc. via Media Foundation (Windows decodes the URL directly).
                radio.Dispose();
                var mf = new MediaFoundationReader(url);
                _reader = mf; sp = mf.ToSampleProvider();
            }
            BuildOutput(sp);
        }
        catch (Exception ex) { radio.Dispose(); Error?.Invoke($"Cannot connect: {ex.Message}"); throw; }
    }

    private void BuildOutput(ISampleProvider src)
    {
        _lastProvider = src;
        _out?.Dispose();
        ISampleProvider chain = src;
        if (chain.WaveFormat.Channels == 1) chain = new MonoToStereoSampleProvider(chain);
        // One internal rate (48 kHz) keeps effects, analysis and recordings consistent across tracks with different sample rates.
        if (chain.WaveFormat.SampleRate != InternalRate) chain = new WdlResamplingSampleProvider(chain, InternalRate);
        chain = new SoundCleaner(chain, SoundClean) { IsLive = IsRadio };
        _speed = new SpeedProvider(chain) { Speed = IsRadio ? 1f : _playbackSpeed };
        _fx = new EffectsChain(_speed, Effects);
        _eq = new Equalizer(_fx, _gains) { Enabled = _eqEnabled };
        _tap = new TapProvider(_eq, Analyzer) { Volume = _volume, Recorder = _recorder };
        MMDevice? dev = null;
        if (_deviceId != null)
        {
            try { using var en = new MMDeviceEnumerator(); dev = en.GetDevice(_deviceId); } catch { dev = null; }
        }
        _out = dev != null
            ? new WasapiOut(dev, AudioClientShareMode.Shared, true, _latencyMs)
            : new WasapiOut(AudioClientShareMode.Shared, _latencyMs);
        _out.Init(_tap);
        _out.PlaybackStopped += (_, e) =>
        {
            if (_stopping) return;
            if (e.Exception != null) { Log.Error(e.Exception, "Playback stopped"); Error?.Invoke(e.Exception.Message); SetState(PlaybackState2.Stopped); }
            else if (_mixer != null ? _mixer.Ended : (_reader != null && _reader.CurrentTime >= _reader.TotalTime - TimeSpan.FromMilliseconds(500)))
            { SetState(PlaybackState2.Stopped); TrackEnded?.Invoke(); }
        };
    }

    public void Play() { if (_out == null) return; _out.Play(); SetState(PlaybackState2.Playing); }
    public void Pause() { if (_out == null) return; _out.Pause(); SetState(PlaybackState2.Paused); }

    public void Stop()
    {
        _stopping = true;
        try
        {
            _out?.Stop(); _out?.Dispose(); _out = null;
            _mixer?.Dispose(); _mixer = null; _live = false;
            _reader?.Dispose(); _reader = null;
            _radio?.Dispose(); _radio = null;
            _lastProvider = null;
            Analyzer.Clear();
            SetState(PlaybackState2.Stopped);
        }
        finally { _stopping = false; }
    }

    private void SetState(PlaybackState2 s) { if (State != s) { State = s; StateChanged?.Invoke(s); } }
    public void Dispose() { _watcher?.Dispose(); Stop(); }
}
