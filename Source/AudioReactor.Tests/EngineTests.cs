using AudioReactor.Core.Analysis;
using AudioReactor.Core.Audio;
using NAudio.Wave;
using Xunit;

namespace AudioReactor.Tests;

public class EngineTests
{
    [Fact]
    public void IcyTitleParsing()
    {
        Assert.Equal("Artist - Song", IcyStream.ParseTitle("StreamTitle='Artist - Song';StreamUrl='';"));
        Assert.Null(IcyStream.ParseTitle("nothing"));
    }

    [Fact]
    public void IcyStream_StripsMetadataAndReportsTitle()
    {
        const int metaInt = 16;
        var ms = new MemoryStream();
        var meta = System.Text.Encoding.UTF8.GetBytes("StreamTitle='Hi';");
        var block = new byte[32]; Array.Copy(meta, block, meta.Length);
        ms.Write(Enumerable.Repeat((byte)1, metaInt).ToArray());
        ms.WriteByte(2); ms.Write(block);
        ms.Write(Enumerable.Repeat((byte)2, metaInt).ToArray());
        ms.WriteByte(0);
        ms.Position = 0;
        string? title = null;
        using var icy = new IcyStream(ms, metaInt);
        icy.MetadataChanged += t => title = t;
        var all = new List<byte>(); var buf = new byte[64]; int n;
        while ((n = icy.Read(buf, 0, buf.Length)) > 0) all.AddRange(buf.Take(n));
        Assert.Equal(2 * metaInt, all.Count);
        Assert.All(all.Take(metaInt), b => Assert.Equal(1, b));
        Assert.Equal("Hi", title);
    }

    [Fact]
    public void Equalizer_BassBoostRaisesLowToneLevel()
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        var gen = new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 62, Gain = 0.2, Type = NAudio.Wave.SampleProviders.SignalGeneratorType.Sin };
        var eq = new Equalizer(gen, Equalizer.Presets["Bass Boost"]);
        var buf = new float[44100];
        eq.Read(buf);
        float peak = buf.Skip(20000).Max(Math.Abs);
        Assert.True(peak > 0.3f, $"peak={peak}");
    }

    [Fact]
    public void Engine_PlaysWavFileAndAnalyzerSeesAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), "ar_test_tone.wav");
        var gen = new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 80, Gain = 0.7 };
        WaveFileWriter.CreateWaveFile16(path, gen.Take(TimeSpan.FromSeconds(3)));
        using var engine = new AudioEngine { Volume = 0.0f };
        try { engine.OpenFile(path); engine.Play(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex.Message.Contains("device", StringComparison.OrdinalIgnoreCase))
        { return; } // no audio endpoint on this machine
        Thread.Sleep(1200);
        var f = new AudioFrame();
        engine.Analyzer.Analyze(f);
        Assert.Equal(PlaybackState2.Playing, engine.State);
        Assert.True(engine.Position > TimeSpan.FromMilliseconds(500));
        Assert.True(f.Bass > 0.4f, $"bass={f.Bass}");
        engine.Stop();
        File.Delete(path);
    }
}
