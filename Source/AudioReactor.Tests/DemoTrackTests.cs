using AudioReactor.Core.Analysis;
using NAudio.Wave;
using Xunit;
using Xunit.Abstractions;

namespace AudioReactor.Tests;

public class DemoTrackTests
{
    private readonly ITestOutputHelper _out;
    public DemoTrackTests(ITestOutputHelper o) => _out = o;

    private static string? FindDemo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { var p = Path.Combine(d.FullName, "Demo", "Neon Pulse (demo).wav"); if (File.Exists(p)) return p; d = d.Parent; }
        return null;
    }

    [Theory]
    [InlineData(60.0)]   // render timer
    [InlineData(30.0)]
    public void RealisticKickTrack_BeatsAndBpmDetected(double analysisFps)
    {
        var path = FindDemo();
        if (path == null) return;
        using var r = new AudioFileReader(path);
        var an = new AudioAnalyzer { SampleRate = r.WaveFormat.SampleRate };
        var frame = new AudioFrame();
        int hop = (int)(r.WaveFormat.SampleRate / analysisFps) * r.WaveFormat.Channels;
        var buf = new float[hop]; double t = 0; int beats = 0; double lastBpm = 0; var beatTimes = new List<double>();
        while (((ISampleProvider)r).Read(buf.AsSpan(0, hop)) == hop)
        {
            an.Write(buf, 0, hop, r.WaveFormat.Channels);
            t += 1.0 / analysisFps;
            an.Analyze(frame, t);
            if (frame.Beat) { beats++; beatTimes.Add(t); }
            if (frame.Bpm > 0) lastBpm = frame.Bpm;
        }
        _out.WriteLine($"fps={analysisFps} beats={beats} bpm={lastBpm:0.0} first={string.Join(",", beatTimes.Take(8).Select(x => x.ToString("0.00")))}");
        // 32 s track, kicks from bar 1 (t=2s) at 2 per second => ~60 kicks.
        Assert.InRange(beats, 35, 75);
        Assert.InRange(lastBpm, 110, 130);
    }
}
