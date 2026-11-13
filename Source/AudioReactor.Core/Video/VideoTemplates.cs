using AudioReactor.Core.Analysis;
using NAudio.Wave;

namespace AudioReactor.Core.Video;

public enum TextLayout { LowerThird, Center, TopLeft, BottomCenter }

public sealed record VideoPalette(string Primary, string Secondary, string Background, string Accent, string Particle, string Glow);

/// <summary>A ready-made look for a video: visualizer preset + palette + text layout, plus the "mood" it suits (used by auto-match).</summary>
public sealed record VideoTemplate(
    string Name, string Description, string Preset, VideoPalette Palette, TextLayout Layout, double TextScale,
    double TargetBpm, double TargetEnergy, double TargetBassShare)
{
    public override string ToString() => Name;
}

public static class VideoTemplates
{
    private static VideoPalette P(string a, string b, string bg, string c, string d, string e) => new(a, b, bg, c, d, e);

    public static readonly IReadOnlyList<VideoTemplate> All = new List<VideoTemplate>
    {
        new("Neon Spectrum", "Classic glowing spectrum bars with a lower-third title. A safe all-rounder.", "Spectrum Bars", P("#00e5ff", "#a64dff", "#05060a", "#ff2bd6", "#ffffff", "#00e5ff"), TextLayout.LowerThird, 1.0, 120, 0.55, 0.50),
        new("Pulse Circle", "A circular spectrum that breathes with the bass, title in the centre.", "Circular Spectrum", P("#2e8bff", "#00e5ff", "#030814", "#ffffff", "#bfe0ff", "#2e8bff"), TextLayout.Center, 1.1, 110, 0.50, 0.55),
        new("Cyberpunk Night", "Neon skyline where every tower is a frequency band. Great for electronic and hip-hop.", "Cyberpunk City", P("#00e5ff", "#ff2bd6", "#07060d", "#fcee09", "#ffffff", "#00e5ff"), TextLayout.TopLeft, 1.0, 128, 0.60, 0.65),
        new("Synthwave Drive", "Retro sun and neon grid landscape for synth, retro and chill-wave tracks.", "Neon Grid Landscape", P("#ff3cac", "#784ba0", "#0b0221", "#2b86c5", "#ffd6f0", "#ff3cac"), TextLayout.BottomCenter, 1.0, 105, 0.45, 0.50),
        new("Particle Burst", "Every beat detonates a burst of particles. Made for fast, energetic music.", "Exploding Particles", P("#ff2bd6", "#ff6a00", "#0d0310", "#00f0ff", "#ffd0f5", "#ff2bd6"), TextLayout.LowerThird, 1.0, 150, 0.80, 0.55),
        new("Fire Beat", "Radial waveform in hot colours for rock, metal and high-energy bass.", "Radial Waveform", P("#ff7a00", "#ff1f00", "#0c0402", "#ffe066", "#ffd199", "#ff5a00"), TextLayout.Center, 1.1, 140, 0.75, 0.70),
        new("Digital Rain", "Falling glyph columns that listen to the spectrum. Techno and glitchy sounds.", "Digital Rain", P("#00ff41", "#008f11", "#020a03", "#b6ffb0", "#c8ffc8", "#00ff41"), TextLayout.BottomCenter, 0.9, 135, 0.60, 0.45),
        new("Galaxy Spin", "A spiral galaxy of stars that spins up with the music.", "Digital Galaxy", P("#b06cff", "#4b1fa8", "#07040f", "#ff7be5", "#e8d8ff", "#9b5cff"), TextLayout.LowerThird, 1.0, 100, 0.50, 0.50),
        new("Deep Space", "Slow parallax stars and planets for ambient and cinematic tracks.", "Space Environment", P("#b06cff", "#4b1fa8", "#07040f", "#ff7be5", "#e8d8ff", "#9b5cff"), TextLayout.Center, 1.0, 80, 0.30, 0.55),
        new("Aurora Calm", "Northern-lights curtains for calm, atmospheric and lo-fi music.", "Aurora", P("#3dffa0", "#7a5cff", "#030a10", "#ff6ad5", "#d9fff0", "#3dffa0"), TextLayout.Center, 1.0, 75, 0.25, 0.40),
        new("Ocean Chill", "Light rays, bubbles and swaying kelp for relaxed and acoustic tracks.", "Underwater World", P("#00c6ff", "#0052d4", "#020b14", "#7fffd4", "#d0f4ff", "#00c6ff"), TextLayout.BottomCenter, 1.0, 90, 0.30, 0.35),
        new("Minimal Waveform", "A clean waveform on black. Best for voice, piano, acoustic and podcasts.", "Waveform", P("#ffffff", "#8a8a8a", "#050505", "#d0d0d0", "#ffffff", "#ffffff"), TextLayout.LowerThird, 1.0, 85, 0.20, 0.30),
    };

    public static VideoTemplate ByName(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];
}

/// <summary>Summary of a piece of audio, used to pick a matching template.</summary>
public sealed record AudioProfile(double Bpm, double Energy, double BassShare, double Brightness, double Seconds)
{
    public string Describe() => $"{(Bpm > 0 ? $"{Bpm:0} BPM" : "tempo unclear")}, {(Energy > 0.62 ? "high" : Energy > 0.38 ? "medium" : "low")} energy, {(BassShare > 0.6 ? "bass-heavy" : BassShare < 0.4 ? "bright" : "balanced")}";
}

public static class AudioProfiler
{
    /// <summary>Analyses up to <paramref name="maxSeconds"/> of an audio file (faster than real time) with the same analyzer used for visuals.</summary>
    public static AudioProfile Analyze(string path, double maxSeconds = 45, double skipSeconds = 5, CancellationToken ct = default)
    {
        using var reader = new AudioFileReader(path);
        var an = new AudioAnalyzer { SampleRate = reader.WaveFormat.SampleRate };
        var frame = new AudioFrame(); int ch = reader.WaveFormat.Channels; double fps = 50; int hop = (int)(reader.WaveFormat.SampleRate / fps) * ch;
        var buf = new float[hop]; var src = (ISampleProvider)reader;
        double total = reader.TotalTime.TotalSeconds;
        double start = total > skipSeconds + 20 ? skipSeconds : 0;          // skip a quiet intro on normal-length tracks
        reader.CurrentTime = TimeSpan.FromSeconds(start);
        double t = 0, energy = 0, bass = 0, treble = 0; int n = 0; double lastBpm = 0; var bpms = new List<double>();
        while (t < maxSeconds && src.Read(buf.AsSpan(0, hop)) == hop)
        {
            ct.ThrowIfCancellationRequested();
            an.Write(buf, 0, hop, ch); t += 1.0 / fps; an.Analyze(frame, t);
            if (t > 2) { energy += frame.Energy; bass += frame.Bass; treble += frame.Treble; n++; if (frame.Bpm > 0) { lastBpm = frame.Bpm; bpms.Add(frame.Bpm); } }
        }
        if (n == 0) return new AudioProfile(0, 0, 0.5, 0.5, t);
        double bpm = bpms.Count > 0 ? bpms.OrderBy(x => x).ElementAt(bpms.Count / 2) : 0;
        double b = bass / n, tr = treble / n;
        return new AudioProfile(bpm, energy / n, b / Math.Max(0.001, b + tr), tr, t);
    }
}

public static class TemplateMatcher
{
    /// <summary>Picks the template whose mood is closest to the audio (tempo, loudness/energy and bass-vs-treble balance).</summary>
    public static (VideoTemplate Template, double Score) Suggest(AudioProfile p, IEnumerable<VideoTemplate>? templates = null)
    {
        var list = (templates ?? VideoTemplates.All).ToList();
        VideoTemplate? best = null; double bestD = double.MaxValue;
        foreach (var t in list)
        {
            double bpmD = p.Bpm > 0 ? Math.Min(Math.Abs(p.Bpm - t.TargetBpm), Math.Abs(p.Bpm * 2 - t.TargetBpm) * 1.3) / 60.0 : 0.3;
            double d = bpmD * 1.0 + Math.Abs(p.Energy - t.TargetEnergy) * 2.2 + Math.Abs(p.BassShare - t.TargetBassShare) * 1.0;
            if (d < bestD) { bestD = d; best = t; }
        }
        return (best!, Math.Max(0, 1 - bestD / 2.5));
    }
}

public sealed record YouTubeProfile(string Name, int Width, int Height, int Fps, int VideoKbps, int AudioKbps)
{
    public override string ToString() => Name;
}

public static class YouTubeProfiles
{
    // Bitrates follow YouTube's published recommendations for SDR uploads; audio is AAC-LC stereo.
    public static readonly IReadOnlyList<YouTubeProfile> All = new List<YouTubeProfile>
    {
        new("1080p · 30 fps (recommended)", 1920, 1080, 30, 8000, 384),
        new("1080p · 60 fps", 1920, 1080, 60, 12000, 384),
        new("1440p · 30 fps", 2560, 1440, 30, 16000, 384),
        new("4K · 30 fps (needs a strong GPU)", 3840, 2160, 30, 35000, 384),
        new("720p · 30 fps (smaller file)", 1280, 720, 30, 5000, 192),
    };
}
