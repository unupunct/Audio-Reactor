using System.Net;
using System.Text;
using AudioReactor.Core.Audio;
using AudioReactor.Core.Recording;
using NAudio.Vorbis;
using NAudio.Wave;
using Xunit;
using Xunit.Abstractions;

namespace AudioReactor.Tests;

internal static class Tools
{
    public static string? Ffmpeg()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { var p = Path.Combine(d.FullName, "Source", "AudioReactor.App", "ThirdParty", "ffmpeg", "ffmpeg.exe"); if (File.Exists(p)) return p; d = d.Parent; }
        return AudioReactor.Core.Recording.Ffmpeg.Find();
    }

    public static string? Demo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { var p = Path.Combine(d.FullName, "Demo", "Neon Pulse (demo).wav"); if (File.Exists(p)) return p; d = d.Parent; }
        return null;
    }
}

public class FormatTests
{
    private readonly ITestOutputHelper _o; public FormatTests(ITestOutputHelper o) => _o = o;

    // codec args per container; every file is produced from the demo track by FFmpeg and must decode with the same readers the player uses.
    [Theory]
    [InlineData("mp3", "-c:a libmp3lame -b:a 192k")]
    [InlineData("flac", "-c:a flac")]
    [InlineData("ogg", "-c:a libvorbis -q:a 5")]
    [InlineData("m4a", "-c:a aac -b:a 192k")]
    [InlineData("aac", "-c:a aac -b:a 192k")]
    [InlineData("wma", "-c:a wmav2 -b:a 192k")]
    [InlineData("wav", "-c:a pcm_s16le")]
    public async Task PlayerReaders_DecodeFormat(string ext, string codec)
    {
        var ff = Tools.Ffmpeg(); var demo = Tools.Demo(); if (ff == null || demo == null) return;
        var path = Path.Combine(Path.GetTempPath(), "ar_fmt_" + Guid.NewGuid().ToString("N") + "." + ext);
        try
        {
            var args = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-t", "6", "-i", demo }; args.AddRange(codec.Split(' ')); args.Add(path);
            var (c, err) = await Ffmpeg2.Run(ff, args);
            if (c != 0) { _o.WriteLine($"[{ext}] encoder not available in this FFmpeg build: {err.Trim()} — skipped"); return; }
            // Same selection as AudioEngine.OpenFile: Vorbis reader for .ogg, Media Foundation for everything else.
            WaveStream r = ext == "ogg" ? new VorbisWaveReader(path) : new MediaFoundationReader(path);
            using (r)
            {
                var sp = r.ToSampleProvider(); var buf = new float[48000]; int total = 0, n; float peak = 0;
                while ((n = sp.Read(buf)) > 0 && total < 44100 * 3 * sp.WaveFormat.Channels) { total += n; for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Abs(buf[i])); }
                _o.WriteLine($"[{ext}] {sp.WaveFormat.SampleRate} Hz x{sp.WaveFormat.Channels}, {r.TotalTime.TotalSeconds:0.0}s, decoded {total} samples, peak {peak:0.00}");
                Assert.True(total > 44100 * 2, $"{ext}: too few samples ({total})"); Assert.True(peak > 0.1f, $"{ext}: silent output");
                // Raw ADTS (.aac) has no duration header, so Media Foundation only estimates it: a known limitation, documented.
                if (ext != "aac") Assert.InRange(r.TotalTime.TotalSeconds, 4.5, 7.5);
            }
            var t = AudioReactor.Core.Data.LibraryScanner.ReadTrack(path); Assert.NotNull(t); if (ext != "aac") Assert.InRange(t!.DurationSec, 4.5, 7.5);
        }
        finally { File.Delete(path); }
    }
}

internal static class Ffmpeg2
{
    public static async Task<(int, string)> Run(string exe, IEnumerable<string> a) => await AudioReactor.Core.Recording.Ffmpeg.RunAsync(exe, a);
}

/// <summary>Runs the radio client against a local SHOUTcast/Icecast-style server (ICY metadata, forced disconnect). Not a real internet station.</summary>
public class RadioTests
{
    private readonly ITestOutputHelper _o; public RadioTests(ITestOutputHelper o) => _o = o;

    private static byte[] WithIcy(byte[] mp3, int metaInt, string title)
    {
        var ms = new MemoryStream(); var meta = Encoding.UTF8.GetBytes($"StreamTitle='{title}';"); int blocks = (meta.Length + 15) / 16;
        for (int i = 0; i < mp3.Length; i += metaInt)
        {
            int n = Math.Min(metaInt, mp3.Length - i); ms.Write(mp3, i, n); if (n < metaInt) break;
            ms.WriteByte(i == 0 ? (byte)blocks : (byte)0); if (i == 0) { var blk = new byte[blocks * 16]; meta.CopyTo(blk, 0); ms.Write(blk); }
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Radio_PlaysStream_ReadsMetadata_AndReconnectsAfterDisconnect()
    {
        var ff = Tools.Ffmpeg(); var demo = Tools.Demo(); if (ff == null || demo == null) return;
        var mp3 = Path.Combine(Path.GetTempPath(), "ar_radio_" + Guid.NewGuid().ToString("N") + ".mp3");
        var (c, e) = await Ffmpeg2.Run(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-t", "12", "-i", demo, "-c:a", "libmp3lame", "-b:a", "128k", "-ar", "44100", mp3 }); Assert.True(c == 0, e);
        var bytes = File.ReadAllBytes(mp3); File.Delete(mp3);
        const int metaInt = 8192; var stream = WithIcy(bytes, metaInt, "Test Artist - Test Song");

        int port; using (var t = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { t.Start(); port = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop(); }
        var http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{port}/"); http.Start();
        int connections = 0;
        var server = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx; try { ctx = await http.GetContextAsync(); } catch { return; }
                int n = Interlocked.Increment(ref connections); bool wantsMeta = ctx.Request.Headers["Icy-MetaData"] == "1";
                ctx.Response.StatusCode = 200; ctx.Response.ContentType = "audio/mpeg"; ctx.Response.SendChunked = true;
                ctx.Response.Headers["icy-name"] = "Local Test Radio"; ctx.Response.Headers["icy-genre"] = "Test"; ctx.Response.Headers["icy-br"] = "128"; ctx.Response.Headers["icy-metaint"] = metaInt.ToString();
                try
                {
                    // First connection: send ~40% then drop abruptly (simulated network failure). Second: send everything.
                    int limit = n == 1 ? stream.Length * 2 / 5 : stream.Length; int sent = 0;
                    while (sent < limit) { int chunk = Math.Min(4096, limit - sent); await ctx.Response.OutputStream.WriteAsync(stream.AsMemory(sent, chunk)); sent += chunk; await Task.Delay(8); }
                    if (n == 1) ctx.Response.Abort(); else ctx.Response.Close();
                }
                catch { }
            }
        });

        using var radio = new RadioSource($"http://127.0.0.1:{port}/", 4);
        var states = new List<RadioState>(); radio.StateChanged += s => { lock (states) states.Add(s); };
        string? title = null; radio.MetadataChanged += t => title = t; var errors = new List<string>(); radio.Error += m => errors.Add(m);
        var provider = await radio.StartAsync();
        var buf = new float[4800]; long nonSilent = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(14))
        {
            int got = provider.Read(buf); for (int i = 0; i < got; i++) if (Math.Abs(buf[i]) > 0.01f) nonSilent++;
            await Task.Delay(40);
            lock (states) if (states.Contains(RadioState.Reconnecting) && connections >= 2 && radio.State == RadioState.Playing && sw.Elapsed > TimeSpan.FromSeconds(6)) break;
        }
        _o.WriteLine("errors: " + string.Join(" | ", errors.Distinct()));
        _o.WriteLine($"states: {string.Join(",", states.Take(12))}; connections={connections}; title={title}; name={radio.StationName}; nonSilentSamples={nonSilent}");
        http.Stop();
        Assert.Equal("Local Test Radio", radio.StationName); Assert.Equal("Test Artist - Test Song", title);
        Assert.True(nonSilent > 10000, "no audible audio decoded from the stream");
        Assert.Contains(RadioState.Reconnecting, states); Assert.True(connections >= 2, "client did not reconnect after the drop");
    }
}

/// <summary>Regression: NAudio 3's MP3 frame reader needs full reads; ICY boundaries and slow networks must not truncate the stream.</summary>
public class IcyPartialReadTests
{
    private readonly ITestOutputHelper _o; public IcyPartialReadTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public async Task FrameCount_IsIdentical_WithIcyBlocks_AndTrickleReads()
    {
        var ff = Tools.Ffmpeg(); var demo = Tools.Demo(); if (ff == null || demo == null) return;
        var f = Path.Combine(Path.GetTempPath(), "ar_dbg_" + Guid.NewGuid().ToString("N") + ".mp3");
        await Ffmpeg2.Run(ff, new[] { "-y", "-hide_banner", "-loglevel", "error", "-t", "6", "-i", demo, "-c:a", "libmp3lame", "-b:a", "128k", "-ar", "44100", f });
        var bytes = File.ReadAllBytes(f); File.Delete(f);
        int Count(Stream s) { int n = 0; try { while (NAudio.Wave.Mp3Frame.LoadFromStream(s) != null) n++; } catch (Exception ex) { _o.WriteLine("  ex: " + ex.GetType().Name + ": " + ex.Message); } return n; }
        _o.WriteLine($"mp3 bytes={bytes.Length}; header={BitConverter.ToString(bytes.Take(10).ToArray())}");
        int plain = Count(new MemoryStream(bytes)); Assert.True(plain > 100); _o.WriteLine("plain MemoryStream frames: " + plain);
        // metaInt=0 (no metadata) through IcyStream
        Assert.Equal(plain, Count(new IcyStream(new MemoryStream(bytes), 0)));
        // With real ICY blocks
        var ms = new MemoryStream(); for (int i = 0; i < bytes.Length; i += 8192) { int n = Math.Min(8192, bytes.Length - i); ms.Write(bytes, i, n); if (n < 8192) break; ms.WriteByte(0); }
        Assert.Equal(plain, Count(new IcyStream(new MemoryStream(ms.ToArray()), 8192)));
        // One byte at a time reads, like a slow network
        Assert.Equal(plain, Count(new IcyStream(new TrickleStream(new MemoryStream(bytes)), 0)));
    }

    private sealed class TrickleStream : Stream
    {
        private readonly Stream _s; public TrickleStream(Stream s) => _s = s;
        public override int Read(byte[] b, int o, int c) => _s.Read(b, o, Math.Min(c, 7));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long a, SeekOrigin b) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] a, int b, int c) => throw new NotSupportedException();
    }
}
