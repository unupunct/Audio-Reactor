using AudioReactor.Core.Recording;
using NAudio.Wave;
using Xunit;

namespace AudioReactor.Tests;

public class RecordingTests
{
    private static string? FindFfmpeg()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { var p = Path.Combine(d.FullName, "Source", "AudioReactor.App", "ThirdParty", "ffmpeg", "ffmpeg.exe"); if (File.Exists(p)) return p; d = d.Parent; }
        return Ffmpeg.Find();
    }

    [Fact]
    public void AudioRecorder_WritesValidWav_AndPauseSkipsAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), "ar_rec_" + Guid.NewGuid().ToString("N") + ".wav");
        var rec = new AudioRecorder(); rec.Start(path, 44100, 2);
        var block = new float[4410 * 2]; for (int i = 0; i < block.Length; i++) block[i] = (float)Math.Sin(i * 0.05) * 0.5f;
        for (int i = 0; i < 10; i++) rec.Write(block);            // 1 s
        rec.Paused = true; for (int i = 0; i < 10; i++) rec.Write(block); // ignored
        rec.Paused = false; for (int i = 0; i < 5; i++) rec.Write(block);  // 0.5 s
        rec.Stop();
        using (var r = new WaveFileReader(path)) { Assert.Equal(44100, r.WaveFormat.SampleRate); Assert.Equal(2, r.WaveFormat.Channels); Assert.InRange(r.TotalTime.TotalSeconds, 1.45, 1.55); }
        File.Delete(path);
    }

    [Fact]
    public void EstimateSize_ScalesWithQualityAndDuration()
    {
        long a = RecordProfiles.EstimateBytes(RecordQuality.P720, TimeSpan.FromMinutes(1)), b = RecordProfiles.EstimateBytes(RecordQuality.P2160, TimeSpan.FromMinutes(1));
        Assert.True(b > a * 5); Assert.InRange(a, 20_000_000, 35_000_000);
    }

    [Fact]
    public async Task Ffmpeg_MuxesWebmAndWavIntoMp4_WithVideoAndAudioStreams()
    {
        var ff = FindFfmpeg(); if (ff == null) return;
        var dir = Path.Combine(Path.GetTempPath(), "ar_mux_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var webm = Path.Combine(dir, "v.webm"); var wav = Path.Combine(dir, "a.wav"); var mp4 = Path.Combine(dir, "o.mp4");
            var (c1, e1) = await Ffmpeg.RunAsync(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-t", "2", "-c:v", "libvpx", "-b:v", "1M", webm });
            Assert.True(c1 == 0, e1);
            var sine = new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 440 };
            WaveFileWriter.CreateWaveFile16(wav, sine.Take(TimeSpan.FromSeconds(2)));
            var (c2, e2) = await Ffmpeg.RunAsync(ff, Ffmpeg.MuxMp4Args(webm, wav, mp4, 1280, 720, 30, 4000, 0.05));
            Assert.True(c2 == 0, e2); Assert.True(new FileInfo(mp4).Length > 10_000);
            var (_, info) = await Ffmpeg.RunAsync(ff, new[] { "-hide_banner", "-i", mp4 }); // exits 1 (no output) but prints stream info
            Assert.Contains("Video: h264", info); Assert.Contains("Audio: aac", info); Assert.Contains("1280x720", info);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Ffmpeg_EncodesWavToMp3()
    {
        var ff = FindFfmpeg(); if (ff == null) return;
        var wav = Path.Combine(Path.GetTempPath(), "ar_m3_" + Guid.NewGuid().ToString("N") + ".wav"); var mp3 = Path.ChangeExtension(wav, ".mp3");
        try
        {
            WaveFileWriter.CreateWaveFile16(wav, new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 300 }.Take(TimeSpan.FromSeconds(1)));
            var (c, e) = await Ffmpeg.RunAsync(ff, Ffmpeg.AudioToMp3Args(wav, mp3)); Assert.True(c == 0, e); Assert.True(new FileInfo(mp3).Length > 1000);
        }
        finally { File.Delete(wav); File.Delete(mp3); }
    }
    [Fact]
    public async Task Ffmpeg_YouTubeSettings_ProduceH264_1080p_WithKeyframesAndAac48k()
    {
        var ff = FindFfmpeg(); if (ff == null) return;
        var dir = Path.Combine(Path.GetTempPath(), "ar_yt_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var webm = Path.Combine(dir, "v.webm"); var wav = Path.Combine(dir, "a.wav"); var mp4 = Path.Combine(dir, "o.mp4");
            var (c1, e1) = await Ffmpeg.RunAsync(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30", "-t", "3", "-c:v", "libvpx", "-b:v", "2M", webm }); Assert.True(c1 == 0, e1);
            WaveFileWriter.CreateWaveFile16(wav, new NAudio.Wave.SampleProviders.SignalGenerator(44100, 2) { Frequency = 440 }.Take(TimeSpan.FromSeconds(3)));
            var yt = AudioReactor.Core.Video.YouTubeProfiles.All[0];
            // h264_mf is the encoder every Windows machine has; GPU encoders are tried first by the app when present.
            var (c2, e2) = await Ffmpeg.RunAsync(ff, Ffmpeg.MuxMp4Args(webm, wav, mp4, yt.Width, yt.Height, yt.Fps, yt.VideoKbps, 0, "h264_mf", yt.AudioKbps, true));
            Assert.True(c2 == 0, e2);
            var (_, info) = await Ffmpeg.RunAsync(ff, new[] { "-hide_banner", "-i", mp4 });
            Assert.Contains("Video: h264", info); Assert.Contains("1920x1080", info); Assert.Contains("yuv420p", info); Assert.Contains("30 fps", info);
            Assert.Contains("Audio: aac", info); Assert.Contains("48000 Hz", info);
        }
        finally { Directory.Delete(dir, true); }
    }
}
