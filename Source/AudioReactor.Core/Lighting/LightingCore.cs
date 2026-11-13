using AudioReactor.Core.Analysis;

namespace AudioReactor.Core.Lighting;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Black => new(0, 0, 0);
    public static Rgb FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return hex.Length == 6 ? new Rgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16)) : Black;
    }
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    public Rgb Scale(float f) { f = Math.Clamp(f, 0, 1); return new((byte)(R * f), (byte)(G * f), (byte)(B * f)); }
    public static Rgb Lerp(Rgb a, Rgb b, float t) { t = Math.Clamp(t, 0, 1); return new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t)); }
    public static Rgb FromHsv(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360 / 60f; int i = (int)h; float f = h - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (float r, float g, float b) = (i % 6) switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return new((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }
    /// <summary>Hue 0..360 of this colour.</summary>
    public float Hue()
    {
        float r = R / 255f, g = G / 255f, b = B / 255f, max = Math.Max(r, Math.Max(g, b)), d = max - Math.Min(r, Math.Min(g, b));
        if (d < 1e-5f) return 0;
        float h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return ((h * 60) + 360) % 360;
    }
}

public enum LightingMode { BassPulse, SpectrumSync, RainbowFlow, BeatFlash, ColorWave, AmbientGlow, MusicMood, Strobe, StaticColor, CustomReactive }

public sealed class LightingSettings
{
    public LightingMode Mode { get; set; } = LightingMode.SpectrumSync;
    public float Brightness { get; set; } = 0.8f;       // master 0..1
    public float Speed { get; set; } = 1f;              // animation speed multiplier
    public float Sensitivity { get; set; } = 1f;        // audio sensitivity
    public List<Rgb> Palette { get; set; } = new() { new Rgb(0, 229, 255), new Rgb(166, 77, 255), new Rgb(255, 43, 214) };
    public Rgb StaticColor { get; set; } = new Rgb(0, 229, 255);
    /// <summary>Strobe rate in Hz. Hard-capped at <see cref="MaxStrobeHz"/> (photosensitivity guideline: no more than 3 flashes per second).</summary>
    public float StrobeHz { get; set; } = 2f;
    public const float MaxStrobeHz = 3f;
    // Custom reactive: which audio feature drives each colour channel and with what gain.
    public string CustomR { get; set; } = "bass";
    public string CustomG { get; set; } = "mid";
    public string CustomB { get; set; } = "treble";
    public float CustomGain { get; set; } = 1f;
}

/// <summary>Turns analysis frames into a strip of virtual zones. Pure logic (no I/O), so it is fully unit-testable.</summary>
public sealed class LightingRenderer
{
    public const int Zones = 16;
    private float _bass, _flash, _calm = 0.5f, _hue;
    private double _lastFlash = -10, _strobeAcc;
    private bool _strobeOn;
    private Rgb _mood = new(0, 120, 255);

    private static float Feature(AudioFrame f, string name) => name switch
    {
        "bass" => f.Bass, "lowMid" => f.LowMid, "mid" => f.Mid, "highMid" => f.HighMid, "treble" => f.Treble,
        "energy" => f.Energy, "volume" => f.Volume, "beat" => f.BeatIntensity, _ => 0
    };

    private static Rgb Gradient(List<Rgb> p, float t)
    {
        if (p.Count == 0) return Rgb.Black;
        if (p.Count == 1) return p[0];
        t = Math.Clamp(t, 0, 0.9999f) * (p.Count - 1); int i = (int)t;
        return Rgb.Lerp(p[i], p[i + 1], t - i);
    }

    public Rgb[] Render(AudioFrame f, double time, float dt, LightingSettings s, Rgb[]? output = null)
    {
        var o = output is { Length: Zones } ? output : new Rgb[Zones];
        float sens = Math.Clamp(s.Sensitivity, 0.2f, 3f), speed = Math.Clamp(s.Speed, 0.1f, 5f), master = Math.Clamp(s.Brightness, 0, 1);
        void Fill(Func<int, Rgb> fn) { for (int i = 0; i < Zones; i++) o[i] = fn(i); }
        var pal = s.Palette.Count > 0 ? s.Palette : new List<Rgb> { s.StaticColor };

        switch (s.Mode)
        {
            case LightingMode.BassPulse:
                _bass = f.Bass * sens > _bass ? f.Bass * sens : _bass * MathF.Pow(0.02f, dt);
                { var c = pal[0].Scale(Math.Clamp(_bass, 0, 1)); Fill(_ => c); }
                break;
            case LightingMode.SpectrumSync:
                Fill(i =>
                {
                    int a = i * 4, b = a + 4; float v = 0; for (int k = a; k < b; k++) v = Math.Max(v, f.Spectrum[k]);
                    return Gradient(pal, i / (float)(Zones - 1)).Scale(Math.Clamp(v * sens, 0, 1));
                });
                break;
            case LightingMode.RainbowFlow:
                _hue = (_hue + dt * 60f * speed) % 360;
                { float v = 0.35f + 0.65f * Math.Clamp(f.Energy * sens * 1.5f, 0, 1); Fill(i => Rgb.FromHsv(_hue + i * 360f / Zones, 1, v)); }
                break;
            case LightingMode.BeatFlash:
                // Flash limited to ~4 per second to avoid flicker.
                if (f.Beat && time - _lastFlash > 0.25) { _flash = Math.Clamp(0.5f + f.BeatIntensity * sens, 0, 1); _lastFlash = time; }
                _flash *= MathF.Pow(0.003f, dt);
                { var c = Rgb.Lerp(pal[0], pal[^1], _flash).Scale(_flash); Fill(_ => c); }
                break;
            case LightingMode.ColorWave:
                _hue += dt * speed * (0.4f + f.Energy * sens);
                Fill(i => Gradient(pal, 0.5f + 0.5f * MathF.Sin(i / (float)Zones * MathF.PI * 2 + _hue * 2)).Scale(0.35f + 0.65f * Math.Clamp(f.Volume * sens + 0.2f, 0, 1)));
                break;
            case LightingMode.AmbientGlow:
                _hue += dt * 0.25f * speed;
                { float br = 0.3f + 0.25f * (0.5f + 0.5f * MathF.Sin(_hue * 1.3f)) + 0.35f * Math.Clamp(f.Volume * sens, 0, 1); Fill(i => Gradient(pal, 0.5f + 0.5f * MathF.Sin(_hue + i * 0.15f)).Scale(br)); }
                break;
            case LightingMode.MusicMood:
                {
                    // Calm/slow/quiet -> cool blues; mid -> purple; fast & loud -> warm reds/oranges. Smoothed so it never flickers.
                    float tempo = f.Bpm > 0 ? Math.Clamp((float)(f.Bpm - 70) / 90f, 0, 1) : 0.3f, target = Math.Clamp(tempo * 0.5f + f.Energy * sens * 0.7f, 0, 1);
                    _calm += (target - _calm) * Math.Min(1, dt * 0.8f);
                    float hue = 230 - _calm * 230; var c = Rgb.FromHsv(hue < 0 ? hue + 360 : hue, 0.9f, 0.45f + 0.55f * Math.Clamp(f.Volume * sens + 0.25f, 0, 1)); _mood = c; Fill(_ => _mood);
                }
                break;
            case LightingMode.Strobe:
                {
                    float hz = Math.Clamp(s.StrobeHz * speed, 0.2f, LightingSettings.MaxStrobeHz);
                    if (f.Energy * sens < 0.05f) { Fill(_ => Rgb.Black); break; }
                    _strobeAcc += dt * hz; if (_strobeAcc >= 1) _strobeAcc -= 1; _strobeOn = _strobeAcc < 0.35;
                    var c = _strobeOn ? pal[0] : Rgb.Black; Fill(_ => c);
                }
                break;
            case LightingMode.StaticColor:
                Fill(_ => s.StaticColor);
                break;
            case LightingMode.CustomReactive:
                {
                    float g = s.CustomGain * sens;
                    byte ch(string n) => (byte)(Math.Clamp(Feature(f, n) * g, 0, 1) * 255);
                    var c = new Rgb(ch(s.CustomR), ch(s.CustomG), ch(s.CustomB)); Fill(_ => c);
                }
                break;
        }
        if (s.Mode != LightingMode.StaticColor || master < 1) for (int i = 0; i < Zones; i++) o[i] = o[i].Scale(master);
        return o;
    }
}
