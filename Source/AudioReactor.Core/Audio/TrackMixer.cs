using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AudioReactor.Core.Audio;

/// <summary>One decoded track, normalised to 48 kHz stereo float so any two tracks can be joined or mixed sample-exactly.</summary>
public sealed class TrackSource : IDisposable
{
    private readonly WaveStream? _reader;
    internal long SamplesRead;                 // interleaved samples delivered so far (used when there is no reader)
    public ISampleProvider Provider { get; }
    public TimeSpan Duration { get; }
    public string? Path { get; }

    public TrackSource(ISampleProvider provider, TimeSpan duration, string? path = null, WaveStream? reader = null)
    { Provider = provider; Duration = duration; Path = path; _reader = reader; }

    public TimeSpan Position => _reader != null ? _reader.CurrentTime : TimeSpan.FromSeconds(SamplesRead / (double)(AudioEngine.InternalRate * 2));
    public bool CanSeek => _reader?.CanSeek ?? false;
    internal void Seek(TimeSpan t) { if (_reader?.CanSeek == true) _reader.CurrentTime = t < TimeSpan.Zero ? TimeSpan.Zero : t; }

    /// <summary>Mono → stereo and any sample rate → 48 kHz.</summary>
    public static ISampleProvider Normalize(ISampleProvider sp)
    {
        if (sp.WaveFormat.Channels == 1) sp = new MonoToStereoSampleProvider(sp);
        if (sp.WaveFormat.SampleRate != AudioEngine.InternalRate) sp = new WdlResamplingSampleProvider(sp, AudioEngine.InternalRate);
        return sp;
    }

    public static TrackSource FromFile(string path)
    {
        WaveStream r = System.IO.Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase) ? new VorbisWaveReader(path) : new MediaFoundationReader(path);
        try { return new TrackSource(Normalize(r.ToSampleProvider()), r.TotalTime, path, r); } catch { r.Dispose(); throw; }
    }

    public void Dispose() => _reader?.Dispose();
}

/// <summary>
/// Plays the current track and, when a next track has been queued, either continues into it with no gap (gapless) or overlaps the end of
/// the current track with the start of the next using an equal-power crossfade. Runs on the audio thread; all state changes are locked.
/// </summary>
public sealed class TrackMixer : ISampleProvider, IDisposable
{
    private readonly object _lock = new();
    private TrackSource? _current, _next;
    private bool _fading; private long _fadeTotal, _fadeDone;     // in frames (stereo sample pairs)
    private float[] _a = new float[8192], _b = new float[8192];
    private volatile bool _ended;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(AudioEngine.InternalRate, 2);
    /// <summary>Crossfade length in seconds; 0 = pure gapless join. Applied to the next fade that starts.</summary>
    public float CrossfadeSeconds { get; set; }
    /// <summary>Raised on the audio thread right after the next track has become the current one.</summary>
    public event Action<TrackSource>? AdvancedToNext;
    /// <summary>True once the current track ended and there was nothing queued.</summary>
    public bool Ended => _ended;
    public bool HasNext { get { lock (_lock) return _next != null; } }
    public bool IsFading { get { lock (_lock) return _fading; } }
    public string? CurrentPath { get { lock (_lock) return _current?.Path; } }
    public TimeSpan Position { get { lock (_lock) return _current?.Position ?? TimeSpan.Zero; } }
    public TimeSpan Duration { get { lock (_lock) return _current?.Duration ?? TimeSpan.Zero; } }

    public void SetCurrent(TrackSource s) { lock (_lock) { _current = s; _ended = false; _fading = false; } }

    public enum NextResult { Ok, Busy }

    /// <summary>Queue the next track (replaces any previous one). Refused while a crossfade is already running.</summary>
    public NextResult SetNext(TrackSource? s)
    {
        TrackSource? old;
        lock (_lock) { if (_fading) { return NextResult.Busy; } old = _next; _next = s; }
        old?.Dispose(); return NextResult.Ok;
    }

    public void Seek(TimeSpan t) { lock (_lock) _current?.Seek(t); }

    public int Read(Span<float> buffer)
    {
        buffer = buffer.Slice(0, buffer.Length & ~1);                 // always whole stereo frames
        TrackSource? finished = null, started = null; int written = 0;
        lock (_lock)
        {
            while (written < buffer.Length && _current != null)
            {
                if (!_fading && _next != null && CrossfadeSeconds > 0.05f && _current.Duration > TimeSpan.Zero)
                {
                    double remaining = (_current.Duration - _current.Position).TotalSeconds;
                    double len = Math.Min(CrossfadeSeconds, _current.Duration.TotalSeconds / 2);
                    if (_next.Duration > TimeSpan.Zero) len = Math.Min(len, _next.Duration.TotalSeconds / 2);
                    if (remaining <= len && len > 0.05) { _fading = true; _fadeTotal = (long)(len * AudioEngine.InternalRate); _fadeDone = 0; }
                }

                if (_fading)
                {
                    int frames = (int)Math.Min((buffer.Length - written) / 2, _fadeTotal - _fadeDone);
                    int n = frames * 2; if (_a.Length < n) { _a = new float[n]; _b = new float[n]; }
                    int ra = ReadFull(_current, _a.AsSpan(0, n)), rb = ReadFull(_next!, _b.AsSpan(0, n));
                    for (int f = 0; f < frames; f++)
                    {
                        double t = (_fadeDone + f) / (double)_fadeTotal;                       // 0..1
                        float gOut = (float)Math.Cos(t * Math.PI / 2), gIn = (float)Math.Sin(t * Math.PI / 2);
                        for (int c = 0; c < 2; c++)
                        {
                            int i = f * 2 + c; float va = i < ra ? _a[i] : 0f, vb = i < rb ? _b[i] : 0f;
                            buffer[written + i] = va * gOut + vb * gIn;
                        }
                    }
                    written += n; _fadeDone += frames;
                    if (_fadeDone >= _fadeTotal || (ra == 0 && rb == 0)) { Retire(ref finished); finished = _current; _current = _next; _next = null; _fading = false; started = _current; }
                    continue;
                }

                var dst = buffer.Slice(written);
                if (_next != null && CrossfadeSeconds > 0.05f && _current.Duration > TimeSpan.Zero)
                {
                    // Stop reading exactly where the crossfade must begin, so the fade covers the true end of this track (not "a buffer late").
                    double len = Math.Min(CrossfadeSeconds, _current.Duration.TotalSeconds / 2);
                    if (_next.Duration > TimeSpan.Zero) len = Math.Min(len, _next.Duration.TotalSeconds / 2);
                    long untilFade = (long)(((_current.Duration - _current.Position).TotalSeconds - len) * AudioEngine.InternalRate);
                    if (untilFade > 0 && untilFade * 2 < dst.Length) dst = dst.Slice(0, (int)Math.Max(2, untilFade * 2));
                }
                int r = _current.Provider.Read(dst);
                if (r > 0) { _current.SamplesRead += r; written += r; continue; }
                // current track is exhausted
                if (_next != null) { Retire(ref finished); finished = _current; _current = _next; _next = null; started = _current; continue; }   // gapless join: continue filling this same buffer
                _ended = true; break;
            }
        }
        if (finished != null) { var f = finished; Task.Run(() => f.Dispose()); }      // never dispose decoders on the audio thread
        if (started != null) AdvancedToNext?.Invoke(started);
        return written;
    }

    private static void Retire(ref TrackSource? old) { if (old != null) { var o = old; Task.Run(() => o.Dispose()); old = null; } }

    private static int ReadFull(TrackSource s, Span<float> dst)
    {
        int got = 0; while (got < dst.Length) { int r = s.Provider.Read(dst.Slice(got)); if (r <= 0) break; got += r; }
        s.SamplesRead += got; return got;
    }

    public void Dispose() { lock (_lock) { _current?.Dispose(); _next?.Dispose(); _current = null; _next = null; } }
}
