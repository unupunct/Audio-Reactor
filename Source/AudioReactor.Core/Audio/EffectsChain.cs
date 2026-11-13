using NAudio.Dsp;
using NAudio.Wave;

namespace AudioReactor.Core.Audio;

public sealed class EffectsSettings
{
    public bool Enabled { get; set; } = true;
    public float BassBoostDb { get; set; }          // 0..12 (low shelf @ 120 Hz)
    public float TrebleBoostDb { get; set; }        // 0..12 (high shelf @ 6 kHz)
    public float ReverbMix { get; set; }            // 0..1
    public float ReverbRoom { get; set; } = 0.6f;   // 0..1
    public float EchoMix { get; set; }              // 0..1
    public float EchoDelayMs { get; set; } = 350;   // 50..1000
    public float EchoFeedback { get; set; } = 0.35f;// 0..0.8
    public float StereoWidth { get; set; } = 1f;    // 0 (mono) .. 2 (extra wide), 1 = unchanged
    public bool Compressor { get; set; }
    public float CompThresholdDb { get; set; } = -18; public float CompRatio { get; set; } = 3;
    public bool Limiter { get; set; } = true;       // safety ceiling at -0.3 dBFS
    public bool Normalize { get; set; }             // slow automatic gain towards a target loudness
}

/// <summary>
/// Optional audio processing chain applied before the equalizer. When <see cref="EffectsSettings.Enabled"/> is false the
/// samples pass through untouched (zero processing cost). Settings can be changed live from the UI thread.
/// </summary>
public sealed class EffectsChain : ISampleProvider
{
    private readonly ISampleProvider _src;
    private readonly int _sr, _ch;
    public EffectsSettings S { get; set; } = new();

    // shelving filters per channel
    private readonly BiQuadFilter[] _bass, _treble; private float _lastBass = -1, _lastTreble = -1;
    // freeverb-ish reverb: 4 combs + 2 allpass per channel
    private readonly Comb[][] _combs; private readonly AllPass[][] _aps;
    // echo
    private readonly float[][] _echo; private int _echoPos;
    // dynamics
    private float _env, _gain = 1f, _normGain = 1f, _normLevel;

    public WaveFormat WaveFormat => _src.WaveFormat;
    /// <summary>Current gain reduction applied by compressor/limiter, in dB (0 = none). Useful for a UI meter.</summary>
    public float GainReductionDb { get; private set; }

    public EffectsChain(ISampleProvider src, EffectsSettings? s = null)
    {
        _src = src; _sr = src.WaveFormat.SampleRate; _ch = src.WaveFormat.Channels; if (s != null) S = s;
        _bass = new BiQuadFilter[_ch]; _treble = new BiQuadFilter[_ch];
        int[] cl = { 1116, 1188, 1277, 1356 }, al = { 556, 441 }; double k = _sr / 44100.0;
        _combs = new Comb[_ch][]; _aps = new AllPass[_ch][];
        for (int c = 0; c < _ch; c++)
        {
            _combs[c] = cl.Select(l => new Comb((int)((l + c * 23) * k))).ToArray();
            _aps[c] = al.Select(l => new AllPass((int)((l + c * 23) * k))).ToArray();
        }
        _echo = Enumerable.Range(0, _ch).Select(_ => new float[_sr * 2]).ToArray();
    }

    private sealed class Comb
    {
        private readonly float[] _b; private int _i; private float _store;
        public Comb(int n) { _b = new float[Math.Max(1, n)]; }
        public float Process(float x, float feedback, float damp) { float o = _b[_i]; _store = o * (1 - damp) + _store * damp; _b[_i] = x + _store * feedback; if (++_i >= _b.Length) _i = 0; return o; }
    }

    private sealed class AllPass
    {
        private readonly float[] _b; private int _i;
        public AllPass(int n) { _b = new float[Math.Max(1, n)]; }
        public float Process(float x) { float bo = _b[_i]; float o = -x + bo; _b[_i] = x + bo * 0.5f; if (++_i >= _b.Length) _i = 0; return o; }
    }

    private void UpdateShelves()
    {
        if (Math.Abs(S.BassBoostDb - _lastBass) > 0.01f) { for (int c = 0; c < _ch; c++) _bass[c] = BiQuadFilter.LowShelf(_sr, 120, 0.8f, S.BassBoostDb); _lastBass = S.BassBoostDb; }
        if (Math.Abs(S.TrebleBoostDb - _lastTreble) > 0.01f) { for (int c = 0; c < _ch; c++) _treble[c] = BiQuadFilter.HighShelf(_sr, 6000, 0.8f, S.TrebleBoostDb); _lastTreble = S.TrebleBoostDb; }
    }

    public int Read(Span<float> buffer)
    {
        int n = _src.Read(buffer);
        var s = S;
        if (!s.Enabled || n == 0) { GainReductionDb = 0; return n; }
        UpdateShelves();
        bool shelves = s.BassBoostDb > 0.01f || s.TrebleBoostDb > 0.01f;
        float feedback = 0.7f + 0.28f * Math.Clamp(s.ReverbRoom, 0, 1), damp = 0.25f, mix = Math.Clamp(s.ReverbMix, 0, 1);
        int delay = Math.Clamp((int)(s.EchoDelayMs / 1000f * _sr), 1, _echo[0].Length - 1); float emix = Math.Clamp(s.EchoMix, 0, 1), efb = Math.Clamp(s.EchoFeedback, 0, 0.8f);
        float thr = MathF.Pow(10, s.CompThresholdDb / 20f), ratio = Math.Max(1, s.CompRatio);
        float maxRed = 0;

        for (int i = 0; i + _ch <= n; i += _ch)
        {
            for (int c = 0; c < _ch; c++)
            {
                float x = buffer[i + c];
                if (shelves) { if (s.BassBoostDb > 0.01f) x = _bass[c].Transform(x); if (s.TrebleBoostDb > 0.01f) x = _treble[c].Transform(x); }
                if (mix > 0.001f)
                {
                    float inp = x * 0.03f, wet = 0; foreach (var cb in _combs[c]) wet += cb.Process(inp, feedback, damp);
                    foreach (var ap in _aps[c]) wet = ap.Process(wet);
                    x = x * (1 - mix * 0.5f) + wet * mix * 3f;
                }
                if (emix > 0.001f)
                {
                    int rp = (_echoPos - delay + _echo[c].Length) % _echo[c].Length; float d = _echo[c][rp];
                    _echo[c][_echoPos] = x + d * efb; x += d * emix;
                }
                buffer[i + c] = x;
            }
            if (emix > 0.001f) _echoPos = (_echoPos + 1) % _echo[0].Length;

            if (_ch >= 2 && Math.Abs(s.StereoWidth - 1f) > 0.01f)
            {
                float l = buffer[i], r = buffer[i + 1], mid = (l + r) * 0.5f, side = (l - r) * 0.5f * Math.Clamp(s.StereoWidth, 0, 2.5f);
                buffer[i] = mid + side; buffer[i + 1] = mid - side;
            }

            float peak = 0; for (int c = 0; c < _ch; c++) peak = Math.Max(peak, Math.Abs(buffer[i + c]));
            float g = 1f;
            if (s.Compressor)
            {
                _env = peak > _env ? _env + (peak - _env) * 0.02f : _env + (peak - _env) * 0.0005f;
                if (_env > thr) { float over = _env / thr; g = MathF.Pow(over, 1f / ratio - 1f); }
                // make-up gain so the compressor doesn't just make everything quieter
                g *= MathF.Pow(10, (-s.CompThresholdDb * (1 - 1 / ratio)) / 40f);
            }
            if (s.Normalize)
            {
                _normLevel += (peak - _normLevel) * (peak > _normLevel ? 0.0005f : 0.00005f);
                float want = _normLevel > 0.001f ? Math.Clamp(0.5f / _normLevel, 0.4f, 3f) : 1f; _normGain += (want - _normGain) * 0.00005f; g *= _normGain;
            }
            float outPeak = peak * g;
            if (s.Limiter && outPeak > 0.966f) g *= 0.966f / outPeak;   // brick-wall ceiling at about -0.3 dBFS
            if (g != 1f) for (int c = 0; c < _ch; c++) buffer[i + c] *= g;
            if (g < 1f) maxRed = Math.Max(maxRed, -20f * MathF.Log10(Math.Max(g, 1e-4f)));
            if (s.Limiter) for (int c = 0; c < _ch; c++) buffer[i + c] = Math.Clamp(buffer[i + c], -1f, 1f);
        }
        GainReductionDb = maxRed;
        return n;
    }
}
