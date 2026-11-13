using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace AudioReactor.Core.Lighting;

public sealed class LightDevice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Adapter { get; init; } = "";
    public int Leds { get; init; } = 1;
    public bool Enabled { get; set; } = true;
    /// <summary>Which of the 16 virtual zones drive this device (start inclusive, end exclusive). Lets users split zones between devices.</summary>
    public int ZoneStart { get; set; }
    public int ZoneEnd { get; set; } = LightingRenderer.Zones;
    public string Display => $"{Name}  ·  {Adapter}";
}

public interface ILightingAdapter : IDisposable
{
    string Name { get; }
    /// <summary>Human readable availability/status, e.g. "Not running" or "Connected".</summary>
    string Status { get; }
    /// <summary>True when this integration can work in this build (some need external SDKs).</summary>
    bool Supported { get; }
    Task<IReadOnlyList<LightDevice>> DetectAsync(CancellationToken ct = default);
    /// <summary>Send one frame for a device. <paramref name="zones"/> is the already-sliced zone range for the device.</summary>
    Task SendAsync(LightDevice device, Rgb[] zones, CancellationToken ct = default);
    Task ReleaseAsync(LightDevice device);
    /// <summary>Minimum interval between sends for this integration (protects hardware/bridge rate limits).</summary>
    TimeSpan MinInterval { get; }
}

/// <summary>On-screen virtual light bar. Needs no hardware, used for preview and as a reference implementation.</summary>
public sealed class VirtualAdapter : ILightingAdapter
{
    public string Name => "Virtual preview";
    public string Status => "Always available";
    public bool Supported => true;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(16);
    public Rgb[] Last { get; private set; } = new Rgb[LightingRenderer.Zones];
    public Task<IReadOnlyList<LightDevice>> DetectAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LightDevice>>(new[] { new LightDevice { Id = "virtual", Name = "Virtual light bar", Adapter = Name, Leds = LightingRenderer.Zones } });
    public Task SendAsync(LightDevice d, Rgb[] zones, CancellationToken ct = default) { Last = (Rgb[])zones.Clone(); return Task.CompletedTask; }
    public Task ReleaseAsync(LightDevice d) => Task.CompletedTask;
    public void Dispose() { }
}

/// <summary>
/// OpenRGB SDK server client (TCP, default 127.0.0.1:6742). OpenRGB itself supports many vendors (including Corsair, ASUS Aura, MSI, Gigabyte, etc.).
/// The user must run OpenRGB with the SDK Server started.
/// </summary>
public sealed class OpenRgbAdapter : ILightingAdapter
{
    private const uint ReqControllerCount = 0, ReqControllerData = 1, ReqProtocolVersion = 40, SetClientName = 50, UpdateLeds = 1050, SetCustomMode = 1100;
    private readonly string _host; private readonly int _port;
    private TcpClient? _tcp; private NetworkStream? _ns; private uint _proto;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public string Name => "OpenRGB";
    public string Status { get; private set; } = "Not detected";
    public bool Supported => true;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(25);
    public OpenRgbAdapter(string host = "127.0.0.1", int port = 6742) { _host = host; _port = port; }

    private static byte[] Header(uint dev, uint id, uint size)
    {
        var b = new byte[16]; "ORGB"u8.CopyTo(b); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), dev); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), id); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), size); return b;
    }

    private async Task WriteAsync(uint dev, uint id, byte[] data, CancellationToken ct)
    {
        await _ns!.WriteAsync(Header(dev, id, (uint)data.Length), ct); if (data.Length > 0) await _ns.WriteAsync(data, ct);
    }

    private async Task<byte[]> ReadAsync(CancellationToken ct)
    {
        var h = new byte[16]; await ReadFullyAsync(h, ct);
        if (h[0] != 'O' || h[1] != 'R' || h[2] != 'G' || h[3] != 'B') throw new InvalidDataException("Bad OpenRGB header");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12)); var d = new byte[size]; await ReadFullyAsync(d, ct); return d;
    }

    private async Task ReadFullyAsync(byte[] buf, CancellationToken ct) { int o = 0; while (o < buf.Length) { int r = await _ns!.ReadAsync(buf.AsMemory(o), ct); if (r <= 0) throw new EndOfStreamException(); o += r; } }

    public async Task<IReadOnlyList<LightDevice>> DetectAsync(CancellationToken ct = default)
    {
        var list = new List<LightDevice>();
        await _lock.WaitAsync(ct);
        try
        {
            Disconnect();
            _tcp = new TcpClient { NoDelay = true };
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) { cts.CancelAfter(1500); await _tcp.ConnectAsync(_host, _port, cts.Token); }
            _ns = _tcp.GetStream();
            await WriteAsync(0, SetClientName, Encoding.UTF8.GetBytes("Audio Reactor\0"), ct);
            var ver = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(ver, 3);
            await WriteAsync(0, ReqProtocolVersion, ver, ct);
            var vr = await ReadAsync(ct); _proto = Math.Min(3u, vr.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(vr) : 0u);
            await WriteAsync(0, ReqControllerCount, Array.Empty<byte>(), ct);
            var cr = await ReadAsync(ct); uint count = BinaryPrimitives.ReadUInt32LittleEndian(cr);
            for (uint i = 0; i < count; i++)
            {
                var pv = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(pv, _proto);
                await WriteAsync(i, ReqControllerData, pv, ct);
                var data = await ReadAsync(ct);
                var (name, leds) = ParseController(data);
                list.Add(new LightDevice { Id = i.ToString(), Name = name, Adapter = Name, Leds = Math.Max(1, leds) });
                await WriteAsync(i, SetCustomMode, Array.Empty<byte>(), ct); // direct control
            }
            Status = $"Connected ({list.Count} device{(list.Count == 1 ? "" : "s")})";
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException or InvalidDataException or EndOfStreamException)
        {
            Status = "OpenRGB server not running (start OpenRGB → SDK Server → Start Server)"; Disconnect();
        }
        finally { _lock.Release(); }
        return list;
    }

    /// <summary>Extracts device name and total LED count. The LED count is read from the trailing colour array (u16 count + 4 bytes per colour).</summary>
    public static (string name, int leds) ParseController(byte[] d)
    {
        string name = "OpenRGB device"; int leds = 0;
        try
        {
            int p = 8; ushort len = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(p)); p += 2;
            name = Encoding.UTF8.GetString(d, p, Math.Max(0, len - 1));
        }
        catch { }
        for (int n = Math.Min(4096, (d.Length - 2) / 4); n >= 1; n--)
        {
            int pos = d.Length - 2 - 4 * n;
            if (pos > 8 && BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos)) == n) { leds = n; break; }
        }
        return (name, leds);
    }

    public async Task SendAsync(LightDevice dev, Rgb[] zones, CancellationToken ct = default)
    {
        if (_ns == null) return;
        // Resample the zone slice across the device's LEDs.
        int n = dev.Leds; var data = new byte[4 + 2 + n * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)data.Length); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)n);
        for (int i = 0; i < n; i++) { var c = zones[Math.Min(zones.Length - 1, i * zones.Length / n)]; data[6 + i * 4] = c.R; data[7 + i * 4] = c.G; data[8 + i * 4] = c.B; }
        await _lock.WaitAsync(ct);
        try { await WriteAsync(uint.Parse(dev.Id), UpdateLeds, data, ct); }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { Status = "Connection lost"; Disconnect(); }
        finally { _lock.Release(); }
    }

    public Task ReleaseAsync(LightDevice device) => Task.CompletedTask;
    private void Disconnect() { try { _ns?.Dispose(); _tcp?.Dispose(); } catch { } _ns = null; _tcp = null; }
    public void Dispose() => Disconnect();
}

/// <summary>Razer Chroma via the local Chroma REST API (127.0.0.1:54235). Requires Razer Synapse 3 with Chroma Connect/SDK enabled.</summary>
public sealed class RazerChromaAdapter : ILightingAdapter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly string _baseUrl; private string? _session; private DateTime _lastBeat = DateTime.MinValue;
    public string Name => "Razer Chroma";
    public string Status { get; private set; } = "Not detected";
    public bool Supported => true;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(50); // Chroma REST allows ~20 req/s
    public static readonly string[] DeviceTypes = { "keyboard", "mouse", "mousepad", "headset", "keypad", "chromalink" };
    public RazerChromaAdapter(string baseUrl = "http://localhost:54235/razer/chromasdk") => _baseUrl = baseUrl;

    public async Task<IReadOnlyList<LightDevice>> DetectAsync(CancellationToken ct = default)
    {
        try
        {
            var body = new { title = "Audio Reactor", description = "Music reactive lighting", author = new { name = "Audio Reactor", contact = "https://github.com/unupunct/Audio-Reactor" },
                device_supported = new[] { "keyboard", "mouse", "headset", "mousepad", "keypad", "chromalink" }, category = "application" };
            var resp = await Http.PostAsJsonAsync(_baseUrl, body, ct); resp.EnsureSuccessStatusCode();
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!; _session = json["uri"]?.GetValue<string>();
            if (_session == null) { Status = "Unexpected reply from Chroma SDK"; return Array.Empty<LightDevice>(); }
            Status = "Connected";
            return DeviceTypes.Select(t => new LightDevice { Id = t, Name = "Razer " + t, Adapter = Name, Leds = t == "keyboard" ? 22 : 1 }).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { Status = "Chroma SDK not reachable (install Razer Synapse 3 and enable Chroma Connect)"; return Array.Empty<LightDevice>(); }
    }

    private static int Bgr(Rgb c) => c.R | (c.G << 8) | (c.B << 16);

    public async Task SendAsync(LightDevice dev, Rgb[] zones, CancellationToken ct = default)
    {
        if (_session == null) return;
        try
        {
            if ((DateTime.UtcNow - _lastBeat).TotalSeconds > 10) { _lastBeat = DateTime.UtcNow; await Http.PutAsync(_session + "/heartbeat", null, ct); } // keep the session alive (<15 s)
            object payload;
            if (dev.Id == "keyboard")
            {
                // 6 rows x 22 columns; columns follow the zone spectrum.
                var grid = new int[6][]; for (int r = 0; r < 6; r++) { grid[r] = new int[22]; for (int c = 0; c < 22; c++) grid[r][c] = Bgr(zones[Math.Min(zones.Length - 1, c * zones.Length / 22)]); }
                payload = new { effect = "CHROMA_CUSTOM", param = grid };
            }
            else
            {
                var avg = new Rgb((byte)zones.Average(z => z.R), (byte)zones.Average(z => z.G), (byte)zones.Average(z => z.B));
                payload = new { effect = "CHROMA_STATIC", param = new { color = Bgr(avg) } };
            }
            await Http.PutAsJsonAsync($"{_session}/{dev.Id}", payload, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Status = "Connection lost"; _session = null; }
    }

    public async Task ReleaseAsync(LightDevice d) { }
    public void Dispose()
    {
        if (_session != null) { try { Http.DeleteAsync(_session).Wait(1000); } catch { } _session = null; }
    }
}

/// <summary>
/// Philips Hue (local bridge, v1 REST API). The bridge must be paired once by pressing its link button.
/// The bridge accepts roughly 10 light commands per second in total, so frames are rate limited and lights are updated round-robin.
/// </summary>
public sealed class HueAdapter : ILightingAdapter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private int _rr;
    public string? BridgeIp { get; set; }
    public string? Username { get; set; }
    public string Name => "Philips Hue";
    public string Status { get; private set; } = "Not configured (enter the bridge IP and pair)";
    public bool Supported => true;
    public TimeSpan MinInterval => TimeSpan.FromMilliseconds(120);

    /// <summary>Pair with the bridge. The user must press the physical link button first.</summary>
    public async Task<bool> PairAsync(string ip, CancellationToken ct = default)
    {
        try
        {
            var resp = await Http.PostAsJsonAsync($"http://{ip}/api", new { devicetype = "audioreactor#pc" }, ct);
            var arr = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!.AsArray();
            var user = arr[0]?["success"]?["username"]?.GetValue<string>();
            if (user == null) { Status = "Press the bridge link button, then try again"; return false; }
            BridgeIp = ip; Username = user; Status = "Paired"; return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { Status = "Bridge not reachable"; return false; }
    }

    public async Task<IReadOnlyList<LightDevice>> DetectAsync(CancellationToken ct = default)
    {
        if (BridgeIp == null || Username == null) return Array.Empty<LightDevice>();
        try
        {
            var json = JsonNode.Parse(await Http.GetStringAsync($"http://{BridgeIp}/api/{Username}/lights", ct))!.AsObject();
            var list = json.Where(kv => kv.Value?["state"]?["reachable"]?.GetValue<bool>() != false)
                .Select(kv => new LightDevice { Id = kv.Key, Name = kv.Value?["name"]?.GetValue<string>() ?? "Hue light " + kv.Key, Adapter = Name }).ToList();
            Status = $"Connected ({list.Count} lights)"; return list;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { Status = "Bridge not reachable"; return Array.Empty<LightDevice>(); }
    }

    public async Task SendAsync(LightDevice dev, Rgb[] zones, CancellationToken ct = default)
    {
        if (BridgeIp == null || Username == null) return;
        var avg = new Rgb((byte)zones.Average(z => z.R), (byte)zones.Average(z => z.G), (byte)zones.Average(z => z.B));
        float v = Math.Max(avg.R, Math.Max(avg.G, avg.B)) / 255f;
        var body = v < 0.02f ? (object)new { on = false, transitiontime = 0 }
            : new { on = true, bri = Math.Clamp((int)(v * 254), 1, 254), hue = (int)(avg.Hue() / 360f * 65535), sat = 254, transitiontime = 0 };
        try { await Http.PutAsJsonAsync($"http://{BridgeIp}/api/{Username}/lights/{dev.Id}/state", body, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Status = "Connection lost"; }
    }

    public Task ReleaseAsync(LightDevice d) => Task.CompletedTask;
    public void Dispose() { }
}
