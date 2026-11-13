using System.Text.Json;

namespace AudioReactor.Core.Models;

/// <summary>User settings persisted as JSON in %AppData%\AudioReactor. Survives upgrades (stored outside the install folder).</summary>
public sealed class AppSettings
{
    public string AccentColor { get; set; } = "#00E5FF";
    public string Theme { get; set; } = "Dark";
    public double Volume { get; set; } = 0.8;
    public string? OutputDeviceId { get; set; }
    public int LatencyMs { get; set; } = 100;
    public Audio.EffectsSettings Effects { get; set; } = new();
    public bool EqEnabled { get; set; } = true;
    public string EqPreset { get; set; } = "Flat";
    public float[] EqGains { get; set; } = new float[10];
    public int RadioBufferSeconds { get; set; } = 8;
    public bool Shuffle { get; set; }
    public string Repeat { get; set; } = "Off"; // Off | All | One
    public string QualityLevel { get; set; } = "High"; // Low | Medium | High | Ultra
    public int FrameRateLimit { get; set; } = 60;
    public double VisualIntensity { get; set; } = 1.0;
    public double AnimationSpeed { get; set; } = 1.0;
    public double BeatSensitivity { get; set; } = 1.0;
    public string VisualPreset { get; set; } = "Spectrum Bars";
    public bool NotifyTrack { get; set; } = true;
    public bool NotifyRadio { get; set; } = true;
    public bool NotifyErrors { get; set; } = true;
    public bool NotifyDevices { get; set; } = true;
    public bool NotifyRecording { get; set; } = true;
    public double PlaybackSpeed { get; set; } = 1.0;
    public bool MinimizeToTray { get; set; }
    public bool RestoreLastTrack { get; set; } = true;
    public double LastPositionSec { get; set; }
    public bool StartWithWindows { get; set; }
    public long LastTrackId { get; set; }
    public Lighting.LightingSettings Lighting { get; set; } = new();
    public bool LightingActive { get; set; }
    public string? HueBridgeIp { get; set; }
    public string? HueUsername { get; set; }
    public List<string> EnabledLightDevices { get; set; } = new();
    public List<string> LibraryFolders { get; set; } = new();
    public string RecordingFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Audio Reactor");

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioReactor");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load(string? path = null)
    {
        path ??= Path.Combine(DataDir, "settings.json");
        try { if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new(); }
        catch { /* corrupt settings: fall back to defaults */ }
        return new();
    }

    public void Save(string? path = null)
    {
        path ??= Path.Combine(DataDir, "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
        File.Move(tmp, path, true);
    }
}
