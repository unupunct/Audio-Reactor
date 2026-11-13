using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AudioReactor.App.Views;
using AudioReactor.Core.Audio;
using AudioReactor.Core.Models;
using AudioReactor.Core.Recording;
using AudioReactor.Core.Video;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace AudioReactor.App.Services;

public enum RecordMode { VideoAudio, VideoOnly, AudioWav, AudioMp3 }

/// <summary>
/// Records the visualizer (WebView canvas at the chosen resolution) and/or the audio stream.
/// Audio goes to a WAV via a background writer thread, video chunks stream from the page, and FFmpeg muxes both after stopping,
/// in a separate process, so encoding never touches the real-time audio engine.
/// </summary>
public sealed partial class RecordingService : ObservableObject
{
    private VisualizerView? _viz; private AudioRecorder? _audio; private FileStream? _video;
    private readonly Stopwatch _sw = new(); private TimeSpan _elapsedBase;
    private string _tmp = "", _outFolder = ""; private RecordMode _mode; private RecordQuality _q; private int _fps;
    private TaskCompletionSource<double>? _started; private TaskCompletionSource? _stopped;
    private double _audioStartMs;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isEncoding;
    [ObservableProperty] private string _status = "Ready";
    public event Action<string>? Completed;
    public event Action<string>? Failed;

    public TimeSpan Elapsed => IsRecording ? _elapsedBase + (_sw.IsRunning ? _sw.Elapsed : TimeSpan.Zero) : _elapsedBase;
    public long EstimatedBytes => _mode switch
    {
        RecordMode.VideoAudio => RecordProfiles.EstimateBytes(_q, Elapsed, true),
        RecordMode.VideoOnly => RecordProfiles.EstimateBytes(_q, Elapsed, true) - Elapsed.Ticks / TimeSpan.TicksPerSecond * RecordProfiles.AudioKbps * 1000 / 8,
        RecordMode.AudioWav => _audio?.BytesWritten ?? 0,
        _ => (long)Elapsed.TotalSeconds * 256_000 / 8
    };

    public static bool FfmpegAvailable => Ffmpeg.Find() != null;

    public async Task StartAsync(RecordMode mode, RecordQuality q, int fps, string folder, VisualizerView? viz, YouTubeProfile? yt = null, Action? onStarted = null)
    {
        if (IsRecording || IsEncoding) return;
        var eng = App.Player.Engine;
        bool video = mode is RecordMode.VideoAudio or RecordMode.VideoOnly;
        if (mode != RecordMode.VideoOnly && eng.State == PlaybackState2.Stopped) throw new InvalidOperationException("Start playing something first — there is no audio to record.");
        if (mode != RecordMode.AudioWav && !FfmpegAvailable) throw new InvalidOperationException("FFmpeg was not found. Reinstall Audio Reactor or place ffmpeg.exe in the ThirdParty\\ffmpeg folder.");
        if (video && (viz == null || !viz.WebReady)) throw new InvalidOperationException("Open Visualizer Studio first so there is something to record.");

        _mode = mode; _q = q; _fps = fps; _outFolder = folder; _viz = viz; _yt = yt;
        Directory.CreateDirectory(folder);
        _tmp = Path.Combine(AppSettings.DataDir, "tmp", "rec-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(_tmp);
        _elapsedBase = TimeSpan.Zero;

        void StartAudio()
        {
            if (mode == RecordMode.VideoOnly) return;
            _audio = new AudioRecorder(); _audio.Start(Path.Combine(_tmp, "audio.wav"), AudioEngine.InternalRate, 2);
            _audioStartMs = _clock.Elapsed.TotalMilliseconds; eng.Recorder = _audio;
        }
        if (!video) StartAudio();
        if (video)
        {
            var (w, h, kbps) = yt != null ? (yt.Width, yt.Height, yt.VideoKbps) : RecordProfiles.Get(q);
            _video = new FileStream(Path.Combine(_tmp, "video.webm"), FileMode.Create, FileAccess.Write, FileShare.Read);
            viz!.RecordingMessage += OnVizMessage;
            viz.SendConfig(new { recordSize = new { w, h } });
            await Task.Delay(400);   // let the canvas resize to the exact recording resolution
            _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            viz.Post(new { type = "rec-start", fps, kbps, w, h });
            var done = await Task.WhenAny(_started.Task, Task.Delay(6000));
            if (done != _started.Task) { await AbortAsync("The visualizer did not start recording (WebView2 may not support MediaRecorder here)."); throw new InvalidOperationException("Video recording could not start."); }
            // The video recorder is running: start audio capture (and playback, if requested) now so both begin together with no dead air.
            StartAudio(); _videoDelaySec = 0; onStarted?.Invoke();
        }
        IsRecording = true; IsPaused = false; _sw.Restart(); Status = "Recording…";
    }

    private double _videoDelaySec;
    private YouTubeProfile? _yt;
    public string? LastOutput { get; private set; }
    public string? EncoderUsed { get; private set; }

    private void OnVizMessage(JsonElement m)
    {
        switch (m.GetProperty("type").GetString())
        {
            case "rec-started": _videoDelaySec = Math.Max(0, (_clock.Elapsed.TotalMilliseconds - _audioStartMs) / 1000.0); _started?.TrySetResult(_videoDelaySec); break;
            case "rec-chunk": try { var b = Convert.FromBase64String(m.GetProperty("data").GetString()!); _video?.Write(b, 0, b.Length); } catch (Exception ex) { Log.Warning(ex, "Video chunk write failed"); } break;
            case "rec-stopped": _stopped?.TrySetResult(); break;
            case "rec-error": Log.Warning("Recording error: {M}", m.GetProperty("message").GetString()); Failed?.Invoke(m.GetProperty("message").GetString() ?? "Recording error"); _started?.TrySetResult(0); _stopped?.TrySetResult(); break;
        }
    }

    public void Pause()
    {
        if (!IsRecording || IsPaused) return;
        if (_audio != null) _audio.Paused = true; _viz?.Post(new { type = "rec-pause" });
        _elapsedBase += _sw.Elapsed; _sw.Reset(); IsPaused = true; Status = "Paused";
    }

    public void Resume()
    {
        if (!IsRecording || !IsPaused) return;
        if (_audio != null) _audio.Paused = false; _viz?.Post(new { type = "rec-resume" });
        _sw.Restart(); IsPaused = false; Status = "Recording…";
    }

    private async Task AbortAsync(string why)
    {
        Log.Warning("Recording aborted: {Why}", why);
        App.Player.Engine.Recorder = null; _audio?.Stop(); _audio = null;
        if (_viz != null) { _viz.RecordingMessage -= OnVizMessage; _viz.SendConfig(new { recordSize = new { w = 0, h = 0 } }); }
        _video?.Dispose(); _video = null; IsRecording = false; Status = "Ready";
        try { Directory.Delete(_tmp, true); } catch { }
        await Task.CompletedTask;
    }

    /// <summary>Stops capture and encodes. Returns the output path (or null on failure).</summary>
    public async Task<string?> StopAsync()
    {
        if (!IsRecording) return null;
        _elapsedBase = Elapsed; _sw.Stop(); IsRecording = false; IsPaused = false; IsEncoding = true; Status = "Finishing…";
        var eng = App.Player.Engine; eng.Recorder = null; _audio?.Stop();
        bool video = _mode is RecordMode.VideoAudio or RecordMode.VideoOnly;
        if (video && _viz != null)
        {
            _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously); _viz.Post(new { type = "rec-stop" });
            await Task.WhenAny(_stopped.Task, Task.Delay(8000));
            _viz.RecordingMessage -= OnVizMessage; _viz.SendConfig(new { recordSize = new { w = 0, h = 0 } });
            _video?.Dispose(); _video = null;
        }
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss"), output = "";
        try
        {
            var wav = Path.Combine(_tmp, "audio.wav");
            if (_mode == RecordMode.AudioWav)
            {
                output = Path.Combine(_outFolder, $"AudioReactor_{stamp}.wav"); File.Move(wav, output, true);
            }
            else
            {
                var ff = Ffmpeg.Find()!; var prog = new Progress<double>(sec => Status = $"Encoding… {TimeSpan.FromSeconds(sec):mm\\:ss}");
                if (_mode == RecordMode.AudioMp3)
                {
                    output = Path.Combine(_outFolder, $"AudioReactor_{stamp}.mp3");
                    var (c, e) = await Ffmpeg.RunAsync(ff, Ffmpeg.AudioToMp3Args(wav, output), prog); if (c != 0) throw new IOException("MP3 encoding failed: " + e);
                }
                else
                {
                    output = Path.Combine(_outFolder, $"AudioReactor_{stamp}.mp4");
                    var (w, h, kbps) = _yt != null ? (_yt.Width, _yt.Height, _yt.VideoKbps) : RecordProfiles.Get(_q);
                    int akbps = _yt?.AudioKbps ?? 0; bool isYt = _yt != null;
                    bool hasAudio = _mode == RecordMode.VideoAudio && File.Exists(wav) && new FileInfo(wav).Length > 20000;
                    // Try the best available H.264 encoder first (GPU encoders give High profile), then the Windows encoder, then MPEG-4 so a recording is never lost.
                    var encoders = isYt ? new[] { "h264_nvenc", "h264_amf", "h264_qsv", "h264_mf", "mpeg4" } : new[] { "h264_mf", "mpeg4" };
                    int c = -1; string e = "";
                    foreach (var enc in encoders)
                    {
                        File.Delete(output);
                        (c, e) = await Ffmpeg.RunAsync(ff, Ffmpeg.MuxMp4Args(Path.Combine(_tmp, "video.webm"), hasAudio ? wav : null, output, w, h, _fps, kbps, _videoDelaySec, enc, akbps, isYt), prog);
                        if (c == 0) { EncoderUsed = enc; Log.Information("Encoded with {Encoder}", enc); break; }
                        Log.Warning("Encoder {Encoder} failed, trying the next: {Err}", enc, e.Trim());
                    }
                    if (c != 0) throw new IOException("Video encoding failed: " + e);
                }
            }
            LastOutput = output; Status = "Saved"; Completed?.Invoke(output);
            return output;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recording finalisation failed"); Status = "Failed"; Failed?.Invoke(ex.Message);
            // keep the raw files so nothing is lost
            var keep = Path.Combine(_outFolder, "unfinished_" + stamp); try { Directory.Move(_tmp, keep); Failed?.Invoke("Raw recording kept in " + keep); } catch { }
            return null;
        }
        finally { IsEncoding = false; try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); } catch { } }
    }
}
