using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioReactor.Core.Analysis;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace AudioReactor.App.Views;

/// <summary>
/// Hosts the WebView2 visualizer. Analysis runs here on the UI dispatcher timer (not on the audio thread),
/// and each frame is posted to the page as JSON.
/// </summary>
public partial class VisualizerView : UserControl
{
    private readonly StringBuilder _sb = new(4096);
    private bool _ready;
    public List<(string Name, string Category, string Description)> Presets { get; private set; } = new();
    public event Action? PresetsLoaded;
    public event Action<int>? FpsReported;
    public string? PendingPreset { get; set; }
    /// <summary>Raised for recording-related messages (rec-*). Payload is a cloned JSON element.</summary>
    public event Action<JsonElement>? RecordingMessage;
    public bool WebReady => Web.CoreWebView2 != null;

    public VisualizerView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitAsync();
        Unloaded += (_, _) => App.Analysis.FrameReady -= Tick;
    }

    private async Task InitAsync()
    {
        if (_ready) { App.Analysis.FrameReady += Tick; return; }
        try
        {
            var userData = Path.Combine(Core.Models.AppSettings.DataDir, "webview");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await Web.EnsureCoreWebView2Async(env);
            var folder = Path.Combine(AppContext.BaseDirectory, "Visualizer");
            Web.CoreWebView2.SetVirtualHostNameToFolderMapping("audioreactor.local", folder, CoreWebView2HostResourceAccessKind.Allow);
            Web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.WebMessageReceived += OnMessage;
            Web.CoreWebView2.Navigate("https://audioreactor.local/index.html");
            App.Analysis.FrameReady += Tick;
            _ready = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 init failed");
            ErrorText.Text = "The visualizer needs the Microsoft WebView2 Runtime (included with Windows 11 and current Windows 10).\n" + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }

    private void OnMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var r = doc.RootElement;
            switch (r.GetProperty("type").GetString())
            {
                case "ready":
                case "presets":
                    var arr = r.TryGetProperty("presets", out var p) ? p : r.GetProperty("list");
                    Presets = arr.EnumerateArray().Select(x => (x.GetProperty("name").GetString()!, x.GetProperty("category").GetString()!, x.GetProperty("description").GetString()!)).ToList();
                    SendConfig(new { preset = PendingPreset ?? App.Settings.VisualPreset });
                    PresetsLoaded?.Invoke();
                    break;
                case "fps": FpsReported?.Invoke(r.GetProperty("fps").GetInt32()); break;
                case "rec-started": case "rec-chunk": case "rec-stopped": case "rec-error": RecordingMessage?.Invoke(r.Clone()); break;
                case "error": Log.Warning("Visualizer script error: {M}", r.GetProperty("message").GetString()); break;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Bad visualizer message"); }
    }

    public void SendConfig(object cfg)
    {
        if (Web.CoreWebView2 == null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "config", cfg }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    public void Post(object message)
    {
        if (Web.CoreWebView2 == null) return;
        Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    public void SetPreset(string name) { PendingPreset = name; App.Settings.VisualPreset = name; SendConfig(new { preset = name }); }

    public void ApplySettings()
    {
        var s = App.Settings;
        var a = App.Player.Engine.Analyzer; a.BeatSensitivity = (float)s.BeatSensitivity;
        SendConfig(new { intensity = s.VisualIntensity, speed = s.AnimationSpeed, quality = s.QualityLevel, fpsLimit = s.FrameRateLimit });
    }

    private static void Arr(StringBuilder sb, float[] a, int dec = 3)
    {
        sb.Append('[');
        for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Math.Round(a[i], dec).ToString(CultureInfo.InvariantCulture)); }
        sb.Append(']');
    }

    private static double Prog() { var e = App.Player.Engine; var d = e.Duration.TotalSeconds; return d > 0 ? Math.Clamp(e.Position.TotalSeconds / d, 0, 1) : 0; }
    private int _skip;
    private void Tick(AudioFrame f)
    {
        if (Web.CoreWebView2 == null || !IsVisible) return;
        // Honour the frame-rate limit by skipping analysis ticks (the analysis loop itself runs at ~60 Hz for lighting).
        int every = App.Settings.FrameRateLimit >= 60 ? 1 : App.Settings.FrameRateLimit >= 30 ? 2 : 4;
        if (++_skip % every != 0 && !f.Beat) return;
        var sb = _sb; sb.Clear();
        string N(float v) => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture);
        sb.Append("{\"type\":\"frame\",\"bass\":").Append(N(f.Bass)).Append(",\"lowMid\":").Append(N(f.LowMid)).Append(",\"mid\":").Append(N(f.Mid))
          .Append(",\"highMid\":").Append(N(f.HighMid)).Append(",\"treble\":").Append(N(f.Treble)).Append(",\"energy\":").Append(N(f.Energy))
          .Append(",\"volume\":").Append(N(f.Volume)).Append(",\"beat\":").Append(f.Beat ? "true" : "false").Append(",\"beatI\":").Append(N(f.BeatIntensity))
          .Append(",\"bpm\":").Append(Math.Round(f.Bpm, 1).ToString(CultureInfo.InvariantCulture)).Append(",\"prog\":").Append(Prog().ToString("0.####", CultureInfo.InvariantCulture)).Append(",\"spec\":");
        Arr(sb, f.Spectrum); sb.Append(",\"wave\":"); Arr(sb, f.Waveform); sb.Append(",\"waveL\":"); Arr(sb, f.WaveLeft); sb.Append(",\"waveR\":"); Arr(sb, f.WaveRight); sb.Append('}');
        Web.CoreWebView2.PostWebMessageAsJson(sb.ToString());
    }
}
