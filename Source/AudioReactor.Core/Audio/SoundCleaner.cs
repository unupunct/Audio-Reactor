using NAudio.Dsp;
using NAudio.Wave;

namespace AudioReactor.Core.Audio;

public sealed class SoundCleanSettings
{
    /// <summary>Even volume: automatic loudness leveling so every song/station plays at about the same loudness.</summary>
    public bool Leveling { get; set; } = true;
    /// <summary>Clean sound: removes rumble/DC, quiets background noise and hiss in quiet passages, and prevents clipping.</summary>
    public bool Clean { get; set; } = true;
    /// <summary>Target loudness in LUFS (about −16 is typical streaming loudness; −20 is quieter, −12 louder).</summary>
    public double TargetLufs { get; set; } = -16;
    /// <summary>Also process local files (radio is always processed when the options above are on).</summary>
    public bool ApplyToFiles { get; set; }
}

/// <summary>
/// Loudness leveler + cleaner for radio (and optionally files): 40 Hz high-pass, ITU-style K-weighted short-term loudness measurement with a gate
/// (silence and noise are never boosted), smooth gain riding (fast down, slow up, ±12 dB), a downward expander for noise between sounds,
/// and a fast peak limiter. All of it is bypassed per option, and changes apply live.
/// </summary>
public sealed class SoundCleaner : ISampleProvider
{
    private readonly ISampleProvider _src; private readonly int _ch, _sr;
    public SoundCleanSettings S { get; set; } = new();
    /// <summary>True for radio: always processed. Files are processed only when <see cref="SoundCleanSettings.ApplyToFiles"/> is on.</summary>
    public bool IsLive { get; set; }
    private readonly BiQuadFilter[] _hp, _kShelf, _kHp;
    private double _meanSq;                 // 400 ms exponential mean of K-weighted power
    private double _gainDb, _lim = 1, _expGainDb, _envFast;
    private readonly double _msAlpha, _fastAlpha, _limRelease;
    private int _blockCounter; private double _desiredDb;
    private const double GateLufs = -50, MaxBoostDb = 12, MaxCutDb = 12, ExpThresholdDb = -52, ExpRatio = 2, ExpMaxDb = 12, Ceiling = 0.97;

    public WaveFormat WaveFormat => _src.WaveFormat;
    /// <summary>Current leveling gain in dB (for UI meters/tests).</summary>
    public double GainDb => _gainDb;
    /// <summary>Last measured momentary loudness (LUFS-like), or −∞ before any audio.</summary>
    public double MeasuredLufs { get; private set; } = double.NegativeInfinity;

    public SoundCleaner(ISampleProvider src, SoundCleanSettings? s = null)
    {
        _src = src; _ch = src.WaveFormat.Channels; _sr = src.WaveFormat.SampleRate; if (s != null) S = s;
        _hp = new BiQuadFilter[_ch]; _kShelf = new BiQuadFilter[_ch]; _kHp = new BiQuadFilter[_ch];
        for (int c = 0; c < _ch; c++)
        {
            _hp[c] = BiQuadFilter.HighPassFilter(_sr, 40, 0.707f);
            _kShelf[c] = BiQuadFilter.HighShelf(_sr, 1681, 0.707f, 4f);   // K-weighting stage 1 (head-related boost)
            _kHp[c] = BiQuadFilter.HighPassFilter(_sr, 38, 0.5f);          // K-weighting stage 2 (RLB)
        }
        _msAlpha = 1 - Math.Exp(-1.0 / (0.4 * _sr));          // 400 ms momentary window
        _fastAlpha = 1 - Math.Exp(-1.0 / (0.02 * _sr));       // 20 ms envelope for the expander
        _limRelease = 1 - Math.Exp(-1.0 / (0.12 * _sr));
    }

    public int Read(Span<float> buffer)
    {
        int n = _src.Read(buffer); var s = S;
        if (!IsLive && !s.ApplyToFiles) return n;
        if (n == 0 || (!s.Leveling && !s.Clean && Math.Abs(_gainDb) < 0.01 && _lim >= 0.999 && _expGainDb > -0.01)) return n;
        double target = Math.Clamp(s.TargetLufs, -30, -8);
        double attack = 1 - Math.Exp(-64.0 / (0.08 * _sr)), release = 1 - Math.Exp(-64.0 / (2.0 * _sr)), expAtk = 1 - Math.Exp(-64.0 / (0.005 * _sr)), expRel = 1 - Math.Exp(-64.0 / (0.15 * _sr));
        int frames = n / _ch;
        for (int f = 0; f < frames; f++)
        {
            double power = 0, peakIn = 0;
            for (int c = 0; c < _ch; c++)
            {
                float x = buffer[f * _ch + c];
                if (s.Clean) x = _hp[c].Transform(x);
                buffer[f * _ch + c] = x;
                float k = _kHp[c].Transform(_kShelf[c].Transform(x)); power += (double)k * k; peakIn = Math.Max(peakIn, Math.Abs(x));
            }
            _meanSq += (power - _meanSq) * _msAlpha; _envFast += (peakIn - _envFast) * _fastAlpha;

            if (++_blockCounter >= 64)
            {
                _blockCounter = 0;
                double lufs = _meanSq > 1e-12 ? -0.691 + 10 * Math.Log10(_meanSq) : double.NegativeInfinity; MeasuredLufs = lufs;
                if (s.Leveling) { if (lufs > GateLufs) _desiredDb = Math.Clamp(target - lufs, -MaxCutDb, MaxBoostDb); /* else: hold the previous gain, never lift silence/noise */ }
                else _desiredDb = 0;
                _gainDb += (_desiredDb - _gainDb) * (_desiredDb < _gainDb ? attack : release);
                // Expander: below the threshold every dB of level lost removes another (ratio-1) dB, up to ExpMaxDb, to hide hiss and noise.
                double expDesired = 0;
                if (s.Clean) { double envDb = _envFast > 1e-9 ? 20 * Math.Log10(_envFast) : -120; if (envDb < ExpThresholdDb) expDesired = -Math.Min(ExpMaxDb, (ExpThresholdDb - envDb) * (ExpRatio - 1)); }
                _expGainDb += (expDesired - _expGainDb) * (expDesired < _expGainDb ? expAtk : expRel);
            }
            double gain = Math.Pow(10, (_gainDb + _expGainDb) / 20);
            double peak = 0;
            for (int c = 0; c < _ch; c++) { double y = buffer[f * _ch + c] * gain; buffer[f * _ch + c] = (float)y; peak = Math.Max(peak, Math.Abs(y)); }

            // Peak limiter: instant attack, slower release, plus a hard clamp as the final safety.
            if (s.Clean || s.Leveling)
            {
                double need = peak > Ceiling ? Ceiling / peak : 1; if (need < _lim) _lim = need; else _lim += (1 - _lim) * _limRelease;
                if (_lim < 0.9999) for (int c = 0; c < _ch; c++) buffer[f * _ch + c] = (float)Math.Clamp(buffer[f * _ch + c] * _lim, -0.999, 0.999);
            }
        }
        return n;
    }
}
