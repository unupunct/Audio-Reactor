using System.Text.Json;
using AudioReactor.Core.Video;

namespace AudioReactor.Core.Models;

/// <summary>A user-saved look: base visualizer preset + palette + effect adjustments. Stored as one JSON file per preset.</summary>
public sealed class CustomPreset
{
    public string Name { get; set; } = "My preset";
    public string BasePreset { get; set; } = "Spectrum Bars";
    public string Description { get; set; } = "";
    public bool Favorite { get; set; }
    public VideoPalette Palette { get; set; } = new("#00e5ff", "#a64dff", "#05060a", "#ff2bd6", "#ffffff", "#00e5ff");
    public Dictionary<string, double> Adjust { get; set; } = DefaultAdjust();
    public double Intensity { get; set; } = 1;
    public double Speed { get; set; } = 1;
    public DateTime Created { get; set; } = DateTime.UtcNow;

    public static Dictionary<string, double> DefaultAdjust() => new()
    {
        ["brightness"] = 1, ["contrast"] = 1, ["saturation"] = 1, ["glow"] = 1, ["particles"] = 1, ["particleSpeed"] = 1, ["rotation"] = 1,
        ["camera"] = 1, ["zoom"] = 1, ["depth"] = 1, ["motion"] = 1, ["sens"] = 1, ["bassSens"] = 1, ["trebleSens"] = 1
    };

    public CustomPreset Clone() => new()
    {
        Name = Name, BasePreset = BasePreset, Description = Description, Favorite = Favorite, Palette = Palette,
        Adjust = new Dictionary<string, double>(Adjust), Intensity = Intensity, Speed = Speed, Created = DateTime.UtcNow
    };
}

public static class PaletteLibrary
{
    public static readonly IReadOnlyDictionary<string, VideoPalette> Presets = new Dictionary<string, VideoPalette>
    {
        ["Cyberpunk"] = new("#00e5ff", "#ff2bd6", "#07060d", "#fcee09", "#ffffff", "#00e5ff"),
        ["Matrix Green"] = new("#00ff41", "#008f11", "#020a03", "#b6ffb0", "#c8ffc8", "#00ff41"),
        ["Purple Galaxy"] = new("#b06cff", "#4b1fa8", "#07040f", "#ff7be5", "#e8d8ff", "#9b5cff"),
        ["Electric Blue"] = new("#2e8bff", "#00e5ff", "#030814", "#ffffff", "#bfe0ff", "#2e8bff"),
        ["Neon Pink"] = new("#ff2bd6", "#ff6a00", "#0d0310", "#00f0ff", "#ffd0f5", "#ff2bd6"),
        ["Fire"] = new("#ff7a00", "#ff1f00", "#0c0402", "#ffe066", "#ffd199", "#ff5a00"),
        ["Ocean"] = new("#00c6ff", "#0052d4", "#020b14", "#7fffd4", "#d0f4ff", "#00c6ff"),
        ["Aurora"] = new("#3dffa0", "#7a5cff", "#030a10", "#ff6ad5", "#d9fff0", "#3dffa0"),
        ["Synthwave"] = new("#ff3cac", "#784ba0", "#0b0221", "#2b86c5", "#ffd6f0", "#ff3cac"),
        ["Rainbow"] = new("#ff3b30", "#5856d6", "#05060a", "#34c759", "#ffcc00", "#ffffff"),
        ["Monochrome"] = new("#ffffff", "#8a8a8a", "#050505", "#d0d0d0", "#ffffff", "#ffffff"),
    };

    /// <summary>A random, harmonious, dark-background palette (hue-based, so it always looks intentional).</summary>
    public static VideoPalette Random(Random? rng = null)
    {
        rng ??= new Random(); double h = rng.NextDouble() * 360;
        string Hx(double hue, double s, double v) { var c = Lighting.Rgb.FromHsv((float)hue, (float)s, (float)v); return c.ToHex().ToLowerInvariant(); }
        return new VideoPalette(Hx(h, 0.9, 1), Hx(h + 40 + rng.Next(80), 0.85, 0.9), Hx(h + 200, 0.5, 0.06), Hx(h + 150 + rng.Next(60), 0.9, 1), Hx(h, 0.1, 1), Hx(h, 0.8, 1));
    }
}

/// <summary>JSON file store for custom presets (one file per preset) in a folder; supports import/export.</summary>
public sealed class PresetStore
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public PresetStore(string dir) { _dir = dir; Directory.CreateDirectory(dir); }

    private static string Safe(string name) { foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_'); return name.Trim(); }
    private string PathFor(string name) => Path.Combine(_dir, Safe(name) + ".arpreset.json");

    public List<CustomPreset> List()
    {
        var l = new List<CustomPreset>();
        foreach (var f in Directory.EnumerateFiles(_dir, "*.arpreset.json"))
        { try { var p = JsonSerializer.Deserialize<CustomPreset>(File.ReadAllText(f)); if (p != null) l.Add(p); } catch { /* skip corrupt files */ } }
        return l.OrderByDescending(p => p.Favorite).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Save(CustomPreset p) { if (string.IsNullOrWhiteSpace(p.Name)) throw new ArgumentException("A preset needs a name."); File.WriteAllText(PathFor(p.Name), JsonSerializer.Serialize(p, Opts)); }
    public void Delete(string name) { var f = PathFor(name); if (File.Exists(f)) File.Delete(f); }
    public void Export(CustomPreset p, string path) => File.WriteAllText(path, JsonSerializer.Serialize(p, Opts));

    /// <summary>Imports a preset file; renames on collision ("Name (2)"). Returns the saved preset.</summary>
    public CustomPreset Import(string path)
    {
        var p = JsonSerializer.Deserialize<CustomPreset>(File.ReadAllText(path)) ?? throw new InvalidDataException("Not a valid Audio Reactor preset file.");
        if (string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.BasePreset)) throw new InvalidDataException("The preset file is missing its name or base visualizer.");
        var baseName = p.Name; int n = 2; while (File.Exists(PathFor(p.Name))) p.Name = $"{baseName} ({n++})";
        foreach (var kv in CustomPreset.DefaultAdjust()) p.Adjust.TryAdd(kv.Key, kv.Value);
        Save(p); return p;
    }
}
