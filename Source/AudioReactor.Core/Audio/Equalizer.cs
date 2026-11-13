using NAudio.Dsp;
using NAudio.Wave;

namespace AudioReactor.Core.Audio;

/// <summary>10-band peaking equalizer, bypassable.</summary>
public sealed class Equalizer : ISampleProvider
{
    public static readonly float[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly Dictionary<string, float[]> Presets = new()
    {
        ["Flat"] = new float[10],
        ["Rock"] = new float[] { 5, 4, 3, 1, -1, -1, 1, 3, 4, 5 },
        ["Pop"] = new float[] { -1, 2, 4, 5, 3, 0, -1, -1, 2, 3 },
        ["Classical"] = new float[] { 4, 3, 2, 1, -1, -1, 0, 2, 3, 4 },
        ["Jazz"] = new float[] { 3, 2, 1, 2, -2, -2, 0, 1, 3, 4 },
        ["Electronic"] = new float[] { 5, 4, 1, 0, -2, 2, 1, 2, 4, 5 },
        ["Bass Boost"] = new float[] { 7, 6, 5, 3, 1, 0, 0, 0, 0, 0 },
        ["Vocal"] = new float[] { -2, -3, -2, 1, 4, 4, 3, 1, 0, -1 },
    };

    private readonly ISampleProvider _src;
    private readonly BiQuadFilter[][] _filters; // [channel][band]
    private readonly float[] _gains = new float[10];
    private volatile bool _dirty = true;
    private bool _flat = true;
    public bool Enabled { get; set; } = true;
    public WaveFormat WaveFormat => _src.WaveFormat;

    public Equalizer(ISampleProvider src, float[]? gains = null)
    {
        _src = src;
        _filters = new BiQuadFilter[src.WaveFormat.Channels][];
        if (gains != null) Array.Copy(gains, _gains, 10);
    }

    public void SetGains(float[] gains) { lock (_gains) { Array.Copy(gains, _gains, 10); _dirty = true; } }

    private void Rebuild()
    {
        int sr = WaveFormat.SampleRate;
        for (int c = 0; c < _filters.Length; c++)
        {
            _filters[c] = new BiQuadFilter[10];
            for (int b = 0; b < 10; b++)
                _filters[c][b] = BiQuadFilter.PeakingEQ(sr, Math.Min(Frequencies[b], sr * 0.45f), 1.1f, _gains[b]);
        }
        _flat = _gains.All(g => Math.Abs(g) < 0.01f);
        _dirty = false;
    }

    public int Read(Span<float> buffer)
    {
        int n = _src.Read(buffer);
        if (!Enabled) return n;
        if (_dirty) lock (_gains) Rebuild();
        if (_flat) return n;
        int ch = WaveFormat.Channels;
        for (int i = 0; i < n; i++)
        {
            var f = _filters[i % ch];
            float s = buffer[i];
            for (int b = 0; b < 10; b++) s = f[b].Transform(s);
            buffer[i] = s;
        }
        return n;
    }
}
