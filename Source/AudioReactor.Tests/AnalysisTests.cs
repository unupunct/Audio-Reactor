using AudioReactor.Core.Analysis;
using AudioReactor.Core.Playlists;
using Xunit;

namespace AudioReactor.Tests;

public class AnalysisTests
{
    private static void Feed(AudioAnalyzer a, double freq, int sr, int samples, float amp = 0.5f, int start = 0)
    {
        var buf = new float[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            float v = amp * (float)Math.Sin(2 * Math.PI * freq * (start + i) / sr);
            buf[i * 2] = v; buf[i * 2 + 1] = v;
        }
        a.Write(buf, 0, buf.Length, 2);
    }

    [Fact]
    public void Fft_FindsSinePeakBin()
    {
        var fft = new Fft(1024);
        var re = new float[1024]; var im = new float[1024];
        for (int i = 0; i < 1024; i++) re[i] = (float)Math.Sin(2 * Math.PI * 64 * i / 1024);
        fft.Transform(re, im);
        int best = 0; float bv = 0;
        for (int i = 1; i < 512; i++) { float m = re[i] * re[i] + im[i] * im[i]; if (m > bv) { bv = m; best = i; } }
        Assert.Equal(64, best);
    }

    [Fact]
    public void Analyzer_BassToneActivatesBassNotTreble()
    {
        var a = new AudioAnalyzer { SampleRate = 44100 };
        var f = new AudioFrame();
        for (int k = 0; k < 10; k++) { Feed(a, 80, 44100, 1024, 0.6f, k * 1024); a.Analyze(f); }
        Assert.True(f.Bass > 0.5f, $"bass={f.Bass}");
        Assert.True(f.Treble < 0.1f, $"treble={f.Treble}");
    }

    [Fact]
    public void Analyzer_TrebleToneActivatesTreble()
    {
        var a = new AudioAnalyzer { SampleRate = 44100 };
        var f = new AudioFrame();
        for (int k = 0; k < 10; k++) { Feed(a, 9000, 44100, 1024, 0.6f, k * 1024); a.Analyze(f); }
        Assert.True(f.Treble > 0.5f, $"treble={f.Treble}");
        Assert.True(f.Bass < 0.1f, $"bass={f.Bass}");
    }

    [Fact]
    public void BeatDetector_DetectsPulsesAndBpm()
    {
        var d = new BeatDetector();
        int beats = 0; double t = 0;
        // 120 BPM kick: pulse every 0.5s, analysis at ~43 Hz
        for (int i = 0; i < 43 * 20; i++, t += 1.0 / 43)
        {
            float e = (i % 21 == 0) ? 0.9f : 0.15f;
            if (d.Process(e, t)) beats++;
        }
        Assert.InRange(beats, 30, 45);
        Assert.InRange(d.Bpm, 110, 130);
    }

    [Fact]
    public void BeatDetector_SilenceNoBeats()
    {
        var d = new BeatDetector(); int beats = 0;
        for (int i = 0; i < 500; i++) if (d.Process(0.01f, i / 43.0)) beats++;
        Assert.Equal(0, beats);
    }
}

public class PlaylistTests
{
    [Fact]
    public void ParsesM3uWithExtinf()
    {
        var l = PlaylistParser.Parse("#EXTM3U\n#EXTINF:123,Artist - Song\nhttp://a.b/stream\nC:\\x.mp3\n");
        Assert.Equal(2, l.Count);
        Assert.Equal("Artist - Song", l[0].Title);
        Assert.Equal("http://a.b/stream", l[0].Url);
        Assert.Null(l[1].Title);
    }

    [Fact]
    public void ParsesPls()
    {
        var l = PlaylistParser.Parse("[playlist]\nNumberOfEntries=2\nFile1=http://x/1\nTitle1=One\nFile2=http://x/2\nTitle2=Two\nVersion=2");
        Assert.Equal(2, l.Count);
        Assert.Equal("Two", l[1].Title);
        Assert.Equal("http://x/1", l[0].Url);
    }
}
