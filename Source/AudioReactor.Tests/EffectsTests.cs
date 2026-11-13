using AudioReactor.Core.Audio;
using NAudio.Wave;
using Xunit;

namespace AudioReactor.Tests;

public class EffectsTests
{
    private sealed class ArraySource : ISampleProvider
    {
        private readonly float[] _d; private int _p;
        public ArraySource(float[] d, int sr = 44100) { _d = d; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sr, 2); }
        public WaveFormat WaveFormat { get; }
        public int Read(Span<float> b) { int n = Math.Min(b.Length, _d.Length - _p); _d.AsSpan(_p, n).CopyTo(b); _p += n; return n; }
    }

    private static float[] Sine(double f, double amp, int frames, int sr = 44100)
    {
        var a = new float[frames * 2]; for (int i = 0; i < frames; i++) { float v = (float)(amp * Math.Sin(2 * Math.PI * f * i / sr)); a[i * 2] = v; a[i * 2 + 1] = v; } return a;
    }

    private static float[] Run(float[] input, Action<EffectsSettings> cfg)
    {
        var s = new EffectsSettings { Limiter = false }; cfg(s);
        var fx = new EffectsChain(new ArraySource(input), s); var o = new float[input.Length]; int n = fx.Read(o); Assert.Equal(input.Length, n); return o;
    }
    private static float Peak(float[] a, int from = 0) { float p = 0; for (int i = from; i < a.Length; i++) p = Math.Max(p, Math.Abs(a[i])); return p; }

    [Fact]
    public void Disabled_IsBitExactPassThrough()
    {
        var input = Sine(440, 0.5, 4410); var o = Run(input, s => { s.Enabled = false; s.ReverbMix = 1; s.BassBoostDb = 12; });
        Assert.Equal(input, o);
    }

    [Fact]
    public void Limiter_PreventsClipping()
    {
        var o = Run(Sine(300, 3.0, 8820), s => s.Limiter = true);
        Assert.True(Peak(o) <= 1.0f); Assert.True(Peak(o) > 0.9f);
    }

    [Fact]
    public void BassBoost_RaisesLowTone_NotHighTone()
    {
        var low = Sine(60, 0.2, 44100); var high = Sine(5000, 0.2, 44100);
        float lowBoost = Peak(Run(low, s => s.BassBoostDb = 9), 22050) / Peak(low), highBoost = Peak(Run(high, s => s.BassBoostDb = 9), 22050) / Peak(high);
        Assert.True(lowBoost > 1.8f, $"low {lowBoost}"); Assert.InRange(highBoost, 0.9f, 1.1f);
    }

    [Fact]
    public void TrebleBoost_RaisesHighTone()
    {
        var high = Sine(10000, 0.2, 44100);
        Assert.True(Peak(Run(high, s => s.TrebleBoostDb = 9), 22050) / Peak(high) > 1.8f);
    }

    [Fact]
    public void Echo_ProducesDelayedCopy()
    {
        var input = new float[44100 * 2]; input[0] = 1f; input[1] = 1f;
        var o = Run(input, s => { s.EchoMix = 1f; s.EchoDelayMs = 100; s.EchoFeedback = 0f; });
        int d = 4410 * 2; Assert.True(Math.Abs(o[d]) > 0.9f, $"echo={o[d]}"); Assert.True(Math.Abs(o[d / 2]) < 0.01f);
    }

    [Fact]
    public void Reverb_AddsDecayingTail()
    {
        var input = new float[44100 * 2]; input[0] = 1f; input[1] = 1f;
        var dry = Run(input, s => { });
        var wet = Run(input, s => { s.ReverbMix = 0.8f; s.ReverbRoom = 0.9f; });
        double tailDry = 0, tailWet = 0; for (int i = 8000; i < 40000; i++) { tailDry += Math.Abs(dry[i]); tailWet += Math.Abs(wet[i]); }
        Assert.True(tailWet > 0.05 && tailDry < 1e-6, $"wet tail {tailWet}");
    }

    [Fact]
    public void StereoWidth_ZeroCollapsesToMono_TwoWidens()
    {
        var input = new float[2000]; for (int i = 0; i < 1000; i++) { input[i * 2] = 0.8f; input[i * 2 + 1] = 0.2f; }
        var mono = Run(input, s => s.StereoWidth = 0f); var wide = Run(input, s => s.StereoWidth = 2f);
        Assert.Equal(mono[10], mono[11], 4); Assert.True(Math.Abs(wide[10] - wide[11]) > Math.Abs(input[10] - input[11]) * 1.8f);
    }

    [Fact]
    public void Compressor_ReducesLevelDifferences()
    {
        var quiet = Sine(300, 0.05, 44100); var loud = Sine(300, 0.8, 44100);
        float inRatio = 0.8f / 0.05f;
        float q = Peak(Run(quiet, s => { s.Compressor = true; s.CompThresholdDb = -30; s.CompRatio = 4; }), 30000), l = Peak(Run(loud, s => { s.Compressor = true; s.CompThresholdDb = -30; s.CompRatio = 4; }), 30000);
        Assert.True(l / q < inRatio * 0.75f, $"out ratio {l / q} vs in {inRatio}");
    }
}

public class SpeedTests
{
    private sealed class Src : ISampleProvider
    {
        private readonly int _frames; private int _p;
        public Src(int frames) { _frames = frames; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2); }
        public WaveFormat WaveFormat { get; }
        public int Read(Span<float> b) { int n = Math.Min(b.Length / 2, _frames - _p); for (int i = 0; i < n; i++) { b[i * 2] = b[i * 2 + 1] = (float)(_p + i) / _frames; } _p += n; return n * 2; }
    }

    private static int Drain(SpeedProvider sp) { var buf = new float[4096]; int total = 0, n; while ((n = sp.Read(buf)) > 0) total += n; return total / 2; }

    [Theory]
    [InlineData(2.0f, 0.5)]
    [InlineData(0.5f, 2.0)]
    [InlineData(1.0f, 1.0)]
    public void OutputLengthScalesInverselyWithSpeed(float speed, double expectedFactor)
    {
        var sp = new SpeedProvider(new Src(44100)) { Speed = speed };
        int frames = Drain(sp);
        Assert.InRange(frames, (int)(44100 * expectedFactor * 0.98), (int)(44100 * expectedFactor * 1.02));
    }

    [Fact]
    public void RampStaysMonotonic()
    {
        var sp = new SpeedProvider(new Src(20000)) { Speed = 1.5f }; var buf = new float[2000]; float prev = -1;
        int n; while ((n = sp.Read(buf)) > 0) { for (int i = 0; i < n; i += 2) { Assert.True(buf[i] >= prev - 1e-6f); prev = buf[i]; } }
    }
}
