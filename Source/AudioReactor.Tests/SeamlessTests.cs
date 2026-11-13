using AudioReactor.Core.Audio;
using NAudio.Wave;
using Xunit;
using Xunit.Abstractions;

namespace AudioReactor.Tests;

internal sealed class ConstSource : ISampleProvider
{
    private readonly float _v; private long _left;
    public ConstSource(float v, double seconds) { _v = v; _left = (long)(seconds * 48000) * 2; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); }
    public WaveFormat WaveFormat { get; }
    public int Read(Span<float> b) { int n = (int)Math.Min(b.Length, _left); b.Slice(0, n).Fill(_v); _left -= n; return n; }
}

public class TrackMixerTests
{
    private static TrackSource Src(float v, double sec) => new(new ConstSource(v, sec), TimeSpan.FromSeconds(sec));

    private static float[] Drain(TrackMixer m, int chunk)
    {
        var all = new List<float>(); var buf = new float[chunk]; int n;
        while ((n = m.Read(buf)) > 0) all.AddRange(buf.Take(n));
        return all.ToArray();
    }

    [Theory]
    [InlineData(1000)]   // chunk sizes that do not line up with the track boundary
    [InlineData(333)]
    [InlineData(4096)]
    public void Gapless_JoinsWithoutAnyGapOrOverlap(int chunk)
    {
        var m = new TrackMixer(); int changes = 0; m.AdvancedToNext += _ => changes++;
        m.SetCurrent(Src(0.3f, 1.0)); m.SetNext(Src(0.6f, 1.0));
        var o = Drain(m, chunk);
        Assert.Equal(48000 * 2 * 2, o.Length);                              // exactly the sum of both tracks
        Assert.Equal(48000 * 2, o.Count(x => x == 0.3f)); Assert.Equal(48000 * 2, o.Count(x => x == 0.6f));
        Assert.DoesNotContain(o, x => x == 0f);                              // never a silent sample
        int boundary = Array.IndexOf(o, 0.6f); Assert.Equal(48000 * 2, boundary); Assert.True(o.Skip(boundary).All(x => x == 0.6f));
        Assert.Equal(1, changes); Assert.True(m.Ended);
    }

    [Fact]
    public void Crossfade_OverlapsAndStaysSmoothAndBounded()
    {
        var m = new TrackMixer { CrossfadeSeconds = 0.5f }; int changes = 0; m.AdvancedToNext += _ => changes++;
        m.SetCurrent(Src(0.4f, 2.0)); m.SetNext(Src(0.8f, 2.0));
        var o = Drain(m, 777);
        Assert.InRange(o.Length, (int)((4.0 - 0.5) * 48000) * 2 - 8, (int)((4.0 - 0.5) * 48000) * 2 + 8);          // overlap shortens the total by the fade length
        Assert.Equal(0.4f, o[0], 4); Assert.Equal(0.8f, o[^1], 4);
        float maxStep = 0, max = 0; for (int i = 2; i < o.Length; i++) { maxStep = Math.Max(maxStep, Math.Abs(o[i] - o[i - 2])); max = Math.Max(max, o[i]); }
        Assert.True(maxStep < 0.01f, $"click: step {maxStep}"); Assert.True(max <= 0.8944f + 1e-3f, $"overshoot {max}");   // equal-power peak = sqrt(0.4²+0.8²)
        // loudness stays steady through the fade: no hole in the middle
        int mid = o.Length / 2; Assert.True(o.Skip(mid - 4000).Take(8000).All(x => x > 0.35f));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Crossfade_IsClampedForShortTracks()
    {
        var m = new TrackMixer { CrossfadeSeconds = 5f }; m.SetCurrent(Src(0.4f, 0.4)); m.SetNext(Src(0.8f, 0.4));
        var o = Drain(m, 500);
        Assert.InRange(o.Length / 2, (int)(0.6 * 48000) - 5, (int)(0.6 * 48000) + 5);   // fade = half of the shortest track (0.2 s)
    }

    [Fact]
    public void NoNextTrack_EndsAndRaisesNoAdvance()
    {
        var m = new TrackMixer { CrossfadeSeconds = 2f }; int changes = 0; m.AdvancedToNext += _ => changes++;
        m.SetCurrent(Src(0.5f, 1.0)); var o = Drain(m, 1024);
        Assert.Equal(48000 * 2, o.Length); Assert.True(m.Ended); Assert.Equal(0, changes); Assert.All(o, x => Assert.Equal(0.5f, x));
    }

    [Fact]
    public void NextQueuedLate_AndReplaced_UsesTheLatestChoice()
    {
        var m = new TrackMixer(); m.SetCurrent(Src(0.3f, 1.0)); var buf = new float[24000]; m.Read(buf);   // 0.25 s in
        m.SetNext(Src(0.9f, 0.5)); Assert.Equal(TrackMixer.NextResult.Ok, m.SetNext(Src(0.6f, 0.5)));      // replaced before it started
        var rest = Drain(m, 1000); Assert.Contains(0.6f, rest); Assert.DoesNotContain(0.9f, rest);
    }

    [Fact]
    public void SetNext_IsRefusedWhileFading()
    {
        var m = new TrackMixer { CrossfadeSeconds = 1f }; m.SetCurrent(Src(0.4f, 2.0)); m.SetNext(Src(0.8f, 2.0));
        var buf = new float[2000]; for (int i = 0; i < 52; i++) m.Read(buf);                                 // ~1.08 s: inside the final 1 s fade
        Assert.True(m.IsFading); Assert.Equal(TrackMixer.NextResult.Busy, m.SetNext(Src(0.1f, 1)));
    }

    [Fact]
    public void Position_FollowsTheCurrentTrackAfterTheJoin()
    {
        var m = new TrackMixer(); m.SetCurrent(Src(0.3f, 1.0)); m.SetNext(Src(0.6f, 3.0));
        var buf = new float[48000 * 2 + 9600]; m.Read(buf);                      // 1.0 s + 0.1 s into track two
        Assert.InRange(m.Position.TotalSeconds, 0.09, 0.11); Assert.InRange(m.Duration.TotalSeconds, 2.99, 3.01);
    }
}

public class SoundCleanerTests
{
    private readonly ITestOutputHelper _o; public SoundCleanerTests(ITestOutputHelper o) => _o = o;

    private sealed class Gen : ISampleProvider
    {
        private readonly Func<long, double> _f; private long _i; private readonly long _n;
        public Gen(Func<long, double> f, double sec) { _f = f; _n = (long)(sec * 48000); WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2); }
        public WaveFormat WaveFormat { get; }
        public int Read(Span<float> b) { int frames = (int)Math.Min(b.Length / 2, _n - _i); for (int k = 0; k < frames; k++) { float v = (float)_f(_i++); b[k * 2] = v; b[k * 2 + 1] = v; } return frames * 2; }
    }

    private static float[] Run(ISampleProvider p) { var l = new List<float>(); var b = new float[9600]; int n; while ((n = p.Read(b)) > 0) l.AddRange(b.Take(n)); return l.ToArray(); }
    private static double Db(float[] a, double fromSec, double toSec) { int s = (int)(fromSec * 96000), e = Math.Min(a.Length, (int)(toSec * 96000)); double sum = 0; for (int i = s; i < e; i++) sum += (double)a[i] * a[i]; return 10 * Math.Log10(Math.Max(1e-12, sum / (e - s))); }
    private static double Sine(long i, double hz, double amp) => amp * Math.Sin(2 * Math.PI * hz * i / 48000);

    [Fact]
    public void Leveling_MakesLoudAndQuietPassagesTheSameVolume()
    {
        // 10 s loud (0.5), 10 s quiet (0.05, 20 dB lower), 10 s loud again
        var src = new Gen(i => Sine(i, 1000, i < 480000 ? 0.5 : i < 960000 ? 0.05 : 0.5) * 1.0, 30);
        var raw = Run(new Gen(i => Sine(i, 1000, i < 480000 ? 0.5 : i < 960000 ? 0.05 : 0.5), 30));
        var o = Run(new SoundCleaner(src, new SoundCleanSettings { Leveling = true, Clean = false }) { IsLive = true });
        double rawGap = Db(raw, 8, 10) - Db(raw, 18, 20), gap = Db(o, 8, 10) - Db(o, 18, 20), gap2 = Db(o, 28, 30) - Db(o, 18, 20);
        _o.WriteLine($"raw loud-quiet gap {rawGap:0.0} dB -> leveled {gap:0.0} dB; after returning loud {gap2:0.0} dB");
        Assert.True(rawGap > 19); Assert.True(Math.Abs(gap) < 3.0, $"gap {gap}"); Assert.True(Math.Abs(gap2) < 3.0, $"gap2 {gap2}");
    }

    [Fact]
    public void Leveling_NeverBoostsSilenceOrNoiseFloor()
    {
        var rnd = new Random(1); var src = new Gen(i => (rnd.NextDouble() * 2 - 1) * 0.0004, 6);            // about −75 dBFS hiss
        var o = Run(new SoundCleaner(src, new SoundCleanSettings { Leveling = true, Clean = false }) { IsLive = true });
        Assert.True(Db(o, 2, 6) < -62, $"noise floor raised to {Db(o, 2, 6):0.0} dB");
    }

    [Fact]
    public void Clean_RemovesRumble_KeepsMusicBand()
    {
        var off = new SoundCleanSettings { Leveling = false, Clean = true };
        double Gain(double hz) { var o = Run(new SoundCleaner(new Gen(i => Sine(i, hz, 0.2), 3), off) { IsLive = true }); var r = Run(new Gen(i => Sine(i, hz, 0.2), 3)); return Db(o, 1, 3) - Db(r, 1, 3); }
        double g20 = Gain(20), g1k = Gain(1000), g8k = Gain(8000); _o.WriteLine($"20 Hz {g20:0.0} dB, 1 kHz {g1k:0.0} dB, 8 kHz {g8k:0.0} dB");
        Assert.True(g20 < -10); Assert.InRange(g1k, -1, 1); Assert.InRange(g8k, -1, 1);
    }

    [Fact]
    public void Clean_QuietsHissBetweenSounds_ButNotMusic()
    {
        var rnd = new Random(2);
        double Hiss(double amp, bool clean) { var o = Run(new SoundCleaner(new Gen(i => (rnd.NextDouble() * 2 - 1) * amp, 4), new SoundCleanSettings { Leveling = false, Clean = clean }) { IsLive = true }); return Db(o, 2, 4); }
        double quietOff = Hiss(0.0015, false), quietOn = Hiss(0.0015, true), loudOff = Hiss(0.2, false), loudOn = Hiss(0.2, true);   // ≈ −57 dBFS hiss vs. music-level noise
        _o.WriteLine($"hiss: {quietOff:0.0} -> {quietOn:0.0} dB; loud: {loudOff:0.0} -> {loudOn:0.0} dB");
        Assert.True(quietOff - quietOn > 5, "hiss not reduced"); Assert.True(Math.Abs(loudOff - loudOn) < 1.0, "music-level signal was altered");
    }

    [Fact]
    public void Limiter_PreventsClippingEvenWhenLevelingBoosts()
    {
        var src = new Gen(i => Sine(i, 200, i < 96000 ? 0.05 : 0.95), 6);       // quiet (gets boosted), then a sudden loud burst
        var o = Run(new SoundCleaner(src, new SoundCleanSettings { Leveling = true, Clean = true, TargetLufs = -12 }) { IsLive = true });
        Assert.True(o.Max(Math.Abs) <= 0.999f, $"peak {o.Max(Math.Abs)}");
    }

    [Fact]
    public void FilesAreUntouched_UnlessEnabled()
    {
        var a = Run(new Gen(i => Sine(i, 440, 0.3), 2)); var o = Run(new SoundCleaner(new Gen(i => Sine(i, 440, 0.3), 2), new SoundCleanSettings()) { IsLive = false });
        Assert.Equal(a, o);
    }
}

/// <summary>Real playback on the default output device (volume 0). Skipped silently on machines without an audio endpoint.</summary>
public class SeamlessEngineTests
{
    private readonly ITestOutputHelper _o; public SeamlessEngineTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public async Task Engine_PlaysNextTrackWithoutStopping_ThenEnds()
    {
        string a = Path.Combine(Path.GetTempPath(), "ar_sa_" + Guid.NewGuid().ToString("N") + ".wav"), b = Path.Combine(Path.GetTempPath(), "ar_sb_" + Guid.NewGuid().ToString("N") + ".wav");
        WaveFileWriter.CreateWaveFile16(a, new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 300, Gain = 0.3 }.Take(TimeSpan.FromSeconds(2.5)));
        WaveFileWriter.CreateWaveFile16(b, new NAudio.Wave.SampleProviders.SignalGenerator(48000, 2) { Frequency = 500, Gain = 0.3 }.Take(TimeSpan.FromSeconds(2.5)));   // different sample rate on purpose
        using var eng = new AudioEngine { Volume = 0 };
        try
        {
            string? advanced = null; var t0 = DateTime.UtcNow; double advancedAt = -1, endedAt = -1; int stoppedBeforeAdvance = 0;
            eng.NextTrackStarted += p => { advanced = p; advancedAt = (DateTime.UtcNow - t0).TotalSeconds; };
            eng.TrackEnded += () => endedAt = (DateTime.UtcNow - t0).TotalSeconds;
            eng.StateChanged += s => { if (s == PlaybackState2.Stopped && advanced == null) stoppedBeforeAdvance++; };
            try { eng.OpenFile(a); eng.CrossfadeSeconds = 0.5f; Assert.Equal(AudioEngine.PrepareResult.Ok, eng.PrepareNext(b)); eng.Play(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex.Message.Contains("device", StringComparison.OrdinalIgnoreCase)) { return; }
            t0 = DateTime.UtcNow;
            for (int i = 0; i < 160 && endedAt < 0; i++) await Task.Delay(50);
            _o.WriteLine($"advanced at {advancedAt:0.00}s to {Path.GetFileName(advanced)}; ended at {endedAt:0.00}s; stops before advance: {stoppedBeforeAdvance}");
            Assert.Equal(b, advanced); Assert.InRange(advancedAt, 1.7, 2.6); Assert.Equal(0, stoppedBeforeAdvance);
            Assert.InRange(endedAt, 4.0, 5.6);                                                      // 2.5 + 2.5 − 0.5 crossfade ≈ 4.5 s
        }
        finally { eng.Stop(); File.Delete(a); File.Delete(b); }
    }
}
