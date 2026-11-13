using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AudioReactor.Core.Analysis;
using AudioReactor.Core.Lighting;
using Xunit;

namespace AudioReactor.Tests;

public class LightingRendererTests
{
    private static AudioFrame Frame(float bass = 0, float mid = 0, float treble = 0, float energy = 0, float volume = 0, bool beat = false, double bpm = 0, float spec = 0)
    {
        var f = new AudioFrame { Bass = bass, Mid = mid, Treble = treble, Energy = energy, Volume = volume, Beat = beat, BeatIntensity = beat ? 1 : 0, Bpm = bpm };
        Array.Fill(f.Spectrum, spec); return f;
    }
    private static int Lit(Rgb[] z) => z.Count(c => c.R + c.G + c.B > 30);

    [Fact]
    public void AllTenModesProduceValidOutput()
    {
        foreach (LightingMode m in Enum.GetValues<LightingMode>())
        {
            var r = new LightingRenderer(); var s = new LightingSettings { Mode = m };
            Rgb[] o = null!;
            for (int i = 0; i < 120; i++) o = r.Render(Frame(0.8f, 0.5f, 0.4f, 0.7f, 0.6f, i % 20 == 0, 120, 0.6f), i / 60.0, 1 / 60f, s);
            Assert.Equal(LightingRenderer.Zones, o.Length);
            Assert.True(Lit(o) > 0 || m == LightingMode.Strobe || m == LightingMode.BeatFlash, $"{m} produced no light");
        }
    }

    [Fact]
    public void StaticColor_ScalesWithBrightness()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.StaticColor, StaticColor = new Rgb(200, 100, 50), Brightness = 0.5f };
        var o = r.Render(Frame(), 0, 0.016f, s);
        Assert.All(o, c => Assert.Equal(new Rgb(100, 50, 25), c));
    }

    [Fact]
    public void BassPulse_FollowsBassAndDecays()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.BassPulse, Brightness = 1 };
        var loud = r.Render(Frame(bass: 1), 0, 0.016f, s)[0]; int loudSum = loud.R + loud.G + loud.B;
        Rgb[] o = null!; for (int i = 0; i < 60; i++) o = r.Render(Frame(bass: 0), i / 60.0, 1 / 60f, s);
        Assert.True(loudSum > 300); Assert.True(o[0].R + o[0].G + o[0].B < 20);
    }

    [Fact]
    public void SpectrumSync_OnlyLightsActiveBands()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.SpectrumSync, Brightness = 1 };
        var f = Frame(); for (int i = 0; i < 16; i++) f.Spectrum[i] = 1f; // first 4 zones only
        var o = r.Render(f, 0, 0.016f, s);
        Assert.Equal(4, Lit(o)); Assert.True(o[0].R + o[0].G + o[0].B > 100); Assert.Equal(Rgb.Black, o[10]);
    }

    [Fact]
    public void Strobe_NeverExceedsSafeRate()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.Strobe, StrobeHz = 50, Speed = 5, Brightness = 1 };
        int rises = 0; bool prev = false; double secs = 10;
        for (int i = 0; i < secs * 60; i++) { var o = r.Render(Frame(energy: 1), i / 60.0, 1 / 60f, s); bool on = Lit(o) > 0; if (on && !prev) rises++; prev = on; }
        Assert.True(rises <= secs * LightingSettings.MaxStrobeHz + 2, $"strobe rises={rises}");
        Assert.True(rises > 0);
    }

    [Fact]
    public void BeatFlash_LimitedFlashRate()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.BeatFlash, Brightness = 1 };
        int flashes = 0; bool prevBright = false;
        for (int i = 0; i < 600; i++) { var o = r.Render(Frame(beat: i % 3 == 0), i / 60.0, 1 / 60f, s); bool b = o[0].R + o[0].G + o[0].B > 400; if (b && !prevBright) flashes++; prevBright = b; } // beats at 20 Hz
        Assert.InRange(flashes, 1, 45); // max ~4/s over 10 s
    }

    [Fact]
    public void Rainbow_ZonesDiffer_And_Animates()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.RainbowFlow };
        var a = r.Render(Frame(energy: 0.5f), 0, 0.016f, s).ToArray(); Rgb[] b = null!; for (int i = 0; i < 30; i++) b = r.Render(Frame(energy: 0.5f), i / 60.0, 1 / 60f, s);
        Assert.NotEqual(a[0], a[8]); Assert.NotEqual(a[0], b[0]);
    }

    [Fact]
    public void CustomReactive_MapsFeaturesToChannels()
    {
        var r = new LightingRenderer(); var s = new LightingSettings { Mode = LightingMode.CustomReactive, CustomR = "treble", CustomG = "bass", CustomB = "mid", Brightness = 1 };
        var o = r.Render(Frame(bass: 1, mid: 0, treble: 0.5f), 0, 0.016f, s)[0];
        Assert.Equal(255, o.G); Assert.Equal(0, o.B); Assert.InRange(o.R, 120, 135);
    }

    [Fact]
    public void MusicMood_CalmIsBlue_EnergeticIsWarm()
    {
        var s = new LightingSettings { Mode = LightingMode.MusicMood, Brightness = 1 };
        var calm = new LightingRenderer(); Rgb[] c = null!; for (int i = 0; i < 600; i++) c = calm.Render(Frame(energy: 0.05f, volume: 0.1f, bpm: 70), i / 60.0, 1 / 60f, s);
        var hot = new LightingRenderer(); Rgb[] h = null!; for (int i = 0; i < 600; i++) h = hot.Render(Frame(energy: 1f, volume: 0.9f, bpm: 170), i / 60.0, 1 / 60f, s);
        Assert.True(c[0].B > c[0].R, $"calm {c[0]}"); Assert.True(h[0].R > h[0].B, $"hot {h[0]}");
    }
}

public class LightingAdapterTests
{
    private static async Task ReadN(NetworkStream ns, byte[] b) { int o = 0; while (o < b.Length) { int r = await ns.ReadAsync(b.AsMemory(o)); if (r <= 0) throw new EndOfStreamException(); o += r; } }

    private static byte[] Pkt(uint dev, uint id, byte[] data)
    {
        var b = new byte[16 + data.Length]; "ORGB"u8.CopyTo(b); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), dev); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), id); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)data.Length); data.CopyTo(b.AsSpan(16)); return b;
    }

    /// <summary>Minimal in-process OpenRGB-style server used to exercise our client's framing. This is NOT a real OpenRGB server.</summary>
    [Fact]
    public async Task OpenRgb_DetectsDeviceAndSendsLedColours()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new TaskCompletionSource<byte[]>();
        var server = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync(); var ns = c.GetStream();
            while (true)
            {
                var h = new byte[16]; try { await ReadN(ns, h); } catch { return; }
                uint dev = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)), id = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8)), size = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12));
                var d = new byte[size]; await ReadN(ns, d);
                if (id == 40) await ns.WriteAsync(Pkt(0, 40, BitConverter.GetBytes(3u)));
                else if (id == 0) await ns.WriteAsync(Pkt(0, 0, BitConverter.GetBytes(1u)));
                else if (id == 1)
                {
                    var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write(0u); w.Write(5u); var nm = Encoding.UTF8.GetBytes("Test Strip\0"); w.Write((ushort)nm.Length); w.Write(nm);
                    w.Write(new byte[37]); // stand-in for modes/zones/leds (variable in the real protocol)
                    w.Write((ushort)5); for (int i = 0; i < 5; i++) w.Write(0u);
                    var data = ms.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)data.Length); await ns.WriteAsync(Pkt(dev, 1, data));
                }
                else if (id == 1050) { received.TrySetResult(d); return; }
            }
        });

        using var a = new OpenRgbAdapter("127.0.0.1", port);
        var devs = await a.DetectAsync();
        Assert.Single(devs); Assert.Equal("Test Strip", devs[0].Name); Assert.Equal(5, devs[0].Leds); Assert.StartsWith("Connected", a.Status);
        var zones = Enumerable.Range(0, 16).Select(i => new Rgb((byte)(i * 10), 100, 200)).ToArray();
        await a.SendAsync(devs[0], zones);
        var pkt = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(pkt.AsSpan(4)));
        Assert.Equal(0, pkt[6]); Assert.Equal(100, pkt[7]); Assert.Equal(200, pkt[8]);
        listener.Stop(); await Task.WhenAny(server, Task.Delay(1000));
    }

    [Fact]
    public async Task OpenRgb_ReportsHelpfulStatusWhenServerMissing()
    {
        using var a = new OpenRgbAdapter("127.0.0.1", 1); // nothing listens on port 1
        Assert.Empty(await a.DetectAsync()); Assert.Contains("OpenRGB", a.Status);
    }

    [Fact]
    public async Task Chroma_SessionAndKeyboardEffect_AgainstMockHttp()
    {
        int port; using (var tmp = new TcpListener(IPAddress.Loopback, 0)) { tmp.Start(); port = ((IPEndPoint)tmp.LocalEndpoint).Port; tmp.Stop(); }
        var http = new HttpListener(); http.Prefixes.Add($"http://127.0.0.1:{port}/"); http.Start();
        var seen = new List<string>(); string? kbBody = null;
        var loop = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx; try { ctx = await http.GetContextAsync(); } catch { return; }
                var body = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync(); seen.Add(ctx.Request.HttpMethod + " " + ctx.Request.Url!.AbsolutePath);
                string reply = ctx.Request.HttpMethod == "POST" ? $"{{\"sessionid\":1,\"uri\":\"http://127.0.0.1:{port}/sid=1\"}}" : "{\"result\":0}";
                if (ctx.Request.Url!.AbsolutePath.EndsWith("/keyboard")) kbBody = body;
                var bytes = Encoding.UTF8.GetBytes(reply); ctx.Response.ContentType = "application/json"; await ctx.Response.OutputStream.WriteAsync(bytes); ctx.Response.Close();
            }
        });
        using var a = new RazerChromaAdapter($"http://127.0.0.1:{port}/razer/chromasdk");
        var devs = await a.DetectAsync(); Assert.Contains(devs, d => d.Id == "keyboard"); Assert.Equal("Connected", a.Status);
        await a.SendAsync(devs.First(d => d.Id == "keyboard"), Enumerable.Repeat(new Rgb(255, 0, 0), 16).ToArray());
        Assert.Contains("PUT /sid=1/keyboard", seen); Assert.Contains("CHROMA_CUSTOM", kbBody);
        Assert.Contains("255", kbBody); // red = 0x0000FF in BGR
        http.Stop();
    }

    [Fact]
    public async Task Engine_VirtualDevice_ReceivesFramesAndHonoursEnableFlag()
    {
        using var e = new LightingEngine(includeDefaults: false); var v = new VirtualAdapter(); e.Adapters.Add(v);
        await e.DetectAllAsync(); Assert.Single(e.Devices);
        e.Settings = new LightingSettings { Mode = LightingMode.StaticColor, StaticColor = new Rgb(10, 20, 30), Brightness = 1 }; e.Active = true;
        e.Update(new AudioFrame(), 0); await Task.Delay(100);
        Assert.Equal(new Rgb(10, 20, 30), v.Last[0]);
        e.Devices[0].Enabled = false; e.Settings.StaticColor = new Rgb(200, 0, 0); await Task.Delay(30); e.Update(new AudioFrame(), 1); await Task.Delay(100);
        Assert.Equal(new Rgb(10, 20, 30), v.Last[0]); // disabled device not updated
    }
}
