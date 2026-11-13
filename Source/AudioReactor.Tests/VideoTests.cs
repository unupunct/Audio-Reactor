using AudioReactor.Core.Video;
using Xunit;
using Xunit.Abstractions;

namespace AudioReactor.Tests;

public class VideoTests
{
    private readonly ITestOutputHelper _o; public VideoTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Templates_HaveUniquePresetsNamesAndAllPresetsExistInVisualizer()
    {
        var names = VideoTemplates.All.Select(t => t.Name).ToList(); Assert.Equal(names.Count, names.Distinct().Count());
        var js = string.Join("\n", Directory.EnumerateFiles(FindVisualizerDir(), "presets*.js").Select(File.ReadAllText));
        foreach (var t in VideoTemplates.All) Assert.Contains($"register('{t.Preset}'", js);
    }

    private static string FindVisualizerDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null) { var p = Path.Combine(d.FullName, "Source", "AudioReactor.App", "Visualizer"); if (Directory.Exists(p)) return p; d = d.Parent; }
        throw new DirectoryNotFoundException();
    }

    [Theory]
    [InlineData(70, 0.15, 0.4, "Aurora Calm|Ocean Chill|Minimal Waveform|Deep Space")]
    [InlineData(150, 0.85, 0.6, "Particle Burst|Fire Beat")]
    [InlineData(128, 0.6, 0.7, "Cyberpunk Night|Digital Rain|Neon Spectrum|Pulse Circle")]
    [InlineData(105, 0.45, 0.5, "Synthwave Drive|Galaxy Spin|Neon Spectrum")]
    public void Matcher_PicksSuitableTemplate(double bpm, double energy, double bass, string acceptable)
    {
        var (t, score) = TemplateMatcher.Suggest(new AudioProfile(bpm, energy, bass, 1 - bass, 30));
        _o.WriteLine($"{bpm} bpm / {energy} -> {t.Name} ({score:0.00})");
        Assert.Contains(t.Name, acceptable.Split('|'));
    }

    [Fact]
    public void Profiler_MeasuresDemoTrack_As120BpmAndMatchesATemplate()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory); string? demo = null;
        while (d != null) { var p = Path.Combine(d.FullName, "Demo", "Neon Pulse (demo).wav"); if (File.Exists(p)) { demo = p; break; } d = d.Parent; }
        if (demo == null) return;
        var prof = AudioProfiler.Analyze(demo, 30, 0);
        _o.WriteLine(prof.ToString() + " | " + prof.Describe());
        Assert.InRange(prof.Bpm, 110, 130); Assert.True(prof.Energy > 0.1);
        var (t, _) = TemplateMatcher.Suggest(prof); _o.WriteLine("suggested: " + t.Name);
    }

    [Fact]
    public void YouTubeProfiles_UseSaneRecommendedBitrates()
    {
        var p = YouTubeProfiles.All[0]; Assert.Equal((1920, 1080, 30), (p.Width, p.Height, p.Fps)); Assert.InRange(p.VideoKbps, 8000, 12000);
        Assert.All(YouTubeProfiles.All, x => Assert.True(x.AudioKbps >= 192));
    }
}

public class PresetStoreTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "ar_presets_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveListFavoriteDeleteRoundTrip()
    {
        var dir = Tmp(); var s = new AudioReactor.Core.Models.PresetStore(dir);
        var a = new AudioReactor.Core.Models.CustomPreset { Name = "Zed", BasePreset = "Kaleidoscope", Intensity = 1.4 }; a.Adjust["glow"] = 2.5;
        var b = new AudioReactor.Core.Models.CustomPreset { Name = "Alpha", BasePreset = "Aurora", Favorite = true };
        s.Save(a); s.Save(b);
        var l = s.List(); Assert.Equal(new[] { "Alpha", "Zed" }, l.Select(x => x.Name));     // favourites first
        Assert.Equal(2.5, l[1].Adjust["glow"]); Assert.Equal(1.4, l[1].Intensity);
        s.Delete("Zed"); Assert.Single(s.List()); Directory.Delete(dir, true);
    }

    [Fact]
    public void ImportExport_RenamesOnCollisionAndRejectsGarbage()
    {
        var dir = Tmp(); var s = new AudioReactor.Core.Models.PresetStore(dir);
        var p = new AudioReactor.Core.Models.CustomPreset { Name = "Mine", BasePreset = "Starfield" }; s.Save(p);
        var file = Path.Combine(dir, "out.json"); s.Export(p, file);
        var imp = s.Import(file); Assert.Equal("Mine (2)", imp.Name); Assert.Equal(2, s.List().Count);
        File.WriteAllText(Path.Combine(dir, "bad.json"), "{ not json"); Assert.ThrowsAny<Exception>(() => s.Import(Path.Combine(dir, "bad.json")));
        File.WriteAllText(Path.Combine(dir, "corrupt.arpreset.json"), "garbage"); Assert.Equal(2, s.List().Count);   // corrupt files are skipped, not fatal
        Directory.Delete(dir, true);
    }

    [Fact]
    public void RandomPalette_IsDarkBackgroundAndValidHex()
    {
        for (int i = 0; i < 50; i++)
        {
            var p = AudioReactor.Core.Models.PaletteLibrary.Random(new Random(i));
            foreach (var c in new[] { p.Primary, p.Secondary, p.Background, p.Accent, p.Particle, p.Glow }) Assert.Matches("^#[0-9a-f]{6}$", c);
            var bg = AudioReactor.Core.Lighting.Rgb.FromHex(p.Background); Assert.True(bg.R + bg.G + bg.B < 200);
        }
        Assert.Equal(11, AudioReactor.Core.Models.PaletteLibrary.Presets.Count);
    }
}
