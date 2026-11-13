using System.Diagnostics;

namespace AudioReactor.Core.Analysis;

/// <summary>
/// Collects interleaved samples from the audio thread into a lock-free-ish ring buffer, and produces
/// <see cref="AudioFrame"/>s on demand (called from a UI/render timer, never from the audio thread).
/// </summary>
public sealed class AudioAnalyzer
{
    public const int FftSize = 2048;
    private readonly float[] _ringL = new float[FftSize * 2], _ringR = new float[FftSize * 2];
    private int _write;
    private readonly object _lock = new();
    private readonly Fft _fft = new(FftSize);
    private readonly float[] _re = new float[FftSize], _im = new float[FftSize], _win = new float[FftSize];
    private readonly float[] _mag = new float[FftSize / 2];
    private readonly float[] _smooth = new float[64];
    private readonly float[] _prevMag = new float[FftSize / 2];
    private readonly BeatDetector _beat = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private float _env, _bass, _lowMid, _mid, _highMid, _treble;

    public int SampleRate { get; set; } = 44100;
    public float Sensitivity { get; set; } = 1f;
    public float BassSensitivity { get; set; } = 1f;
    public float TrebleSensitivity { get; set; } = 1f;
    public float BeatSensitivity { get => _beat.Sensitivity; set => _beat.Sensitivity = value; }

    public AudioAnalyzer()
    {
        for (int i = 0; i < FftSize; i++) _win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
    }

    public void Write(float[] buffer, int offset, int count, int channels) => Write(buffer.AsSpan(offset, count), channels);

    /// <summary>Called from the audio thread. Interleaved samples.</summary>
    public void Write(ReadOnlySpan<float> buffer, int channels)
    {
        int count = buffer.Length;
        lock (_lock)
        {
            for (int i = 0; i + channels <= count; i += channels)
            {
                float l = buffer[i];
                float r = channels > 1 ? buffer[i + 1] : l;
                _ringL[_write] = l; _ringR[_write] = r;
                _write = (_write + 1) % _ringL.Length;
            }
        }
    }

    public void Clear()
    {
        lock (_lock) { Array.Clear(_ringL); Array.Clear(_ringR); }
    }

    /// <summary>Analyze the most recent samples. <paramref name="frame"/> is reused to avoid allocation.</summary>
    public void Analyze(AudioFrame frame, double? timeSeconds = null)
    {
        int start;
        lock (_lock)
        {
            start = (_write - FftSize + _ringL.Length) % _ringL.Length;
            for (int i = 0; i < FftSize; i++)
            {
                int idx = (start + i) % _ringL.Length;
                float m = (_ringL[idx] + _ringR[idx]) * 0.5f;
                _re[i] = m * _win[i];
                _im[i] = 0;
                if (i >= FftSize - frame.Waveform.Length)
                    frame.Waveform[i - (FftSize - frame.Waveform.Length)] = m;
                if (i >= FftSize - frame.WaveLeft.Length)
                {
                    frame.WaveLeft[i - (FftSize - frame.WaveLeft.Length)] = _ringL[idx];
                    frame.WaveRight[i - (FftSize - frame.WaveRight.Length)] = _ringR[idx];
                }
            }
        }

        _fft.Transform(_re, _im);
        float norm = 2f / FftSize;
        for (int i = 0; i < _mag.Length; i++) _mag[i] = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * norm;

        float hz = SampleRate / (float)FftSize;
        float bass = Band(20, 150, hz), low = Band(150, 400, hz), mid = Band(400, 2000, hz),
              high = Band(2000, 5000, hz), tre = Band(5000, 16000, hz);

        // Convert magnitude to a perceptual 0..1 (dB scale, -60..0 dB).
        float s = Sensitivity;
        frame.Bass = Level(bass * BassSensitivity * s); frame.LowMid = Level(low * s);
        frame.Mid = Level(mid * s); frame.HighMid = Level(high * s); frame.Treble = Level(tre * TrebleSensitivity * s);
        _bass = Smooth(_bass, frame.Bass); _lowMid = Smooth(_lowMid, frame.LowMid); _mid = Smooth(_mid, frame.Mid);
        _highMid = Smooth(_highMid, frame.HighMid); _treble = Smooth(_treble, frame.Treble);
        frame.Bass = _bass; frame.LowMid = _lowMid; frame.Mid = _mid; frame.HighMid = _highMid; frame.Treble = _treble;
        frame.Energy = Math.Clamp((_bass * 1.2f + _lowMid + _mid + _highMid * 0.8f + _treble * 0.6f) / 4.6f, 0, 1);

        // Volume envelope (RMS with fast attack, slow release).
        float rms = 0; int cnt = frame.Waveform.Length;
        for (int i = 0; i < cnt; i++) rms += frame.Waveform[i] * frame.Waveform[i];
        rms = MathF.Sqrt(rms / cnt);
        _env = rms > _env ? rms : _env * 0.92f + rms * 0.08f;
        frame.Volume = Math.Clamp(_env * 3f * s, 0, 1);

        // Log-spaced spectrum for display.
        int bands = frame.Spectrum.Length;
        float fmin = 30, fmax = Math.Min(18000, SampleRate / 2f - 1);
        for (int b = 0; b < bands; b++)
        {
            float f0 = fmin * MathF.Pow(fmax / fmin, b / (float)bands);
            float f1 = fmin * MathF.Pow(fmax / fmin, (b + 1) / (float)bands);
            float v = Level(Peak(f0, f1, hz) * s * (1f + 0.6f * b / bands));
            _smooth[b] = v > _smooth[b] ? v : _smooth[b] * 0.85f + v * 0.15f;
            frame.Spectrum[b] = _smooth[b];
        }

        // Onset signal: positive spectral flux in the low band (<= ~250 Hz), linear magnitudes.
        int top = Math.Min(_mag.Length - 1, (int)(250 / hz)); float flux = 0;
        for (int i = 1; i <= top; i++) { float d = _mag[i] - _prevMag[i]; if (d > 0) flux += d; }
        Array.Copy(_mag, _prevMag, _mag.Length);
        frame.Beat = _beat.Process(flux * 8f, timeSeconds ?? _clock.Elapsed.TotalSeconds);
        frame.BeatIntensity = _beat.Intensity;
        frame.Bpm = _beat.Bpm;
    }

    private static float Smooth(float prev, float cur) => cur > prev ? prev * 0.3f + cur * 0.7f : prev * 0.85f + cur * 0.15f;

    private static float Level(float mag)
    {
        if (mag < 1e-6f) return 0;
        float db = 20f * MathF.Log10(mag);
        return Math.Clamp((db + 60f) / 60f, 0f, 1f);
    }

    private float Band(float f0, float f1, float hz)
    {
        int a = Math.Max(1, (int)(f0 / hz)), b = Math.Min(_mag.Length - 1, Math.Max(a, (int)(f1 / hz)));
        float sum = 0, peak = 0;
        for (int i = a; i <= b; i++) { sum += _mag[i]; peak = Math.Max(peak, _mag[i]); }
        // Blend of mean and peak so narrow tones in wide bands (treble) still register.
        return 0.5f * (sum / (b - a + 1)) * 2.5f + 0.5f * peak * 1.2f;
    }

    private float Peak(float f0, float f1, float hz)
    {
        int a = Math.Max(1, (int)(f0 / hz)), b = Math.Min(_mag.Length - 1, Math.Max(a, (int)(f1 / hz)));
        float p = 0;
        for (int i = a; i <= b; i++) p = Math.Max(p, _mag[i]);
        return p;
    }
}
