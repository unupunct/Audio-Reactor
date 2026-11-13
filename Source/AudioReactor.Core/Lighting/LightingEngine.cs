using AudioReactor.Core.Analysis;
using Serilog;

namespace AudioReactor.Core.Lighting;

/// <summary>Owns adapters + detected devices and pushes rendered frames to enabled devices, honouring each adapter's rate limit.</summary>
public sealed class LightingEngine : IDisposable
{
    private readonly LightingRenderer _renderer = new();
    private readonly Dictionary<string, DateTime> _lastSend = new();
    private readonly HashSet<string> _inFlight = new();
    private DateTime _last = DateTime.UtcNow;
    private readonly Rgb[] _frame = new Rgb[LightingRenderer.Zones];

    public List<ILightingAdapter> Adapters { get; } = new();
    public List<LightDevice> Devices { get; } = new();
    public LightingSettings Settings { get; set; } = new();
    public bool Active { get; set; }
    public Rgb[] CurrentFrame => _frame;

    public LightingEngine(bool includeDefaults = true)
    {
        if (includeDefaults) { Adapters.Add(new VirtualAdapter()); Adapters.Add(new OpenRgbAdapter()); Adapters.Add(new RazerChromaAdapter()); Adapters.Add(new HueAdapter()); }
    }

    public T? Get<T>() where T : class, ILightingAdapter => Adapters.OfType<T>().FirstOrDefault();

    public async Task DetectAllAsync(CancellationToken ct = default)
    {
        var previous = Devices.ToDictionary(d => d.Adapter + "|" + d.Id);
        var found = new List<LightDevice>();
        foreach (var a in Adapters)
        {
            try
            {
                foreach (var d in await a.DetectAsync(ct))
                {
                    if (previous.TryGetValue(d.Adapter + "|" + d.Id, out var old)) { d.Enabled = old.Enabled; d.ZoneStart = old.ZoneStart; d.ZoneEnd = old.ZoneEnd; }
                    else d.Enabled = a is VirtualAdapter; // real hardware is opt-in
                    found.Add(d);
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Lighting detection failed for {Adapter}", a.Name); }
        }
        Devices.Clear(); Devices.AddRange(found);
    }

    /// <summary>Call once per render tick with the latest analysis frame.</summary>
    public void Update(AudioFrame audio, double time)
    {
        var now = DateTime.UtcNow; float dt = (float)Math.Min(0.1, (now - _last).TotalSeconds); _last = now;
        _renderer.Render(audio, time, dt, Settings, _frame);
        if (!Active) return;
        foreach (var d in Devices.Where(d => d.Enabled).ToList())
        {
            var adapter = Adapters.FirstOrDefault(a => a.Name == d.Adapter); if (adapter == null) continue;
            string key = d.Adapter + "|" + d.Id;
            if (_lastSend.TryGetValue(key, out var t) && now - t < adapter.MinInterval) continue;
            lock (_inFlight) { if (!_inFlight.Add(key)) continue; }   // never queue frames: drop if the previous one is still in flight
            _lastSend[key] = now;
            int s = Math.Clamp(d.ZoneStart, 0, LightingRenderer.Zones - 1), e = Math.Clamp(d.ZoneEnd, s + 1, LightingRenderer.Zones);
            var slice = _frame[s..e];
            _ = Task.Run(async () =>
            {
                try { await adapter.SendAsync(d, slice); }
                catch (Exception ex) { Log.Debug(ex, "Lighting send failed {Key}", key); }
                finally { lock (_inFlight) _inFlight.Remove(key); }
            });
        }
    }

    public async Task TurnOffAsync()
    {
        foreach (var d in Devices.Where(d => d.Enabled))
        {
            var a = Adapters.FirstOrDefault(x => x.Name == d.Adapter); if (a == null) continue;
            try { await a.SendAsync(d, new Rgb[LightingRenderer.Zones]); await a.ReleaseAsync(d); } catch { }
        }
    }

    public void Dispose() { foreach (var a in Adapters) a.Dispose(); }
}
