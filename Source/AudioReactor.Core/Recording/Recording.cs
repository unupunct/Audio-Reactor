using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Serilog;

namespace AudioReactor.Core.Recording;

/// <summary>
/// Writes the audio stream to a 16-bit WAV file on a background thread. The audio thread only copies samples into a bounded queue
/// (never blocks, drops data if the disk cannot keep up), so recording can not interrupt playback.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private readonly BlockingCollection<float[]> _q = new(512);
    private Thread? _thread; private FileStream? _fs; private int _channels, _rate; private long _bytes;
    private volatile bool _paused, _running;
    public long DroppedBlocks { get; private set; }
    public long BytesWritten => Interlocked.Read(ref _bytes);
    public bool IsRunning => _running;
    public bool Paused { get => _paused; set => _paused = value; }
    public TimeSpan Duration => _rate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(BytesWritten / (double)(_rate * _channels * 2));

    public void Start(string path, int sampleRate, int channels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _rate = sampleRate; _channels = channels; _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _fs.Write(new byte[44]); _bytes = 0; _running = true; _paused = false;
        _thread = new Thread(Loop) { IsBackground = true, Name = "AudioRecorder", Priority = ThreadPriority.BelowNormal }; _thread.Start();
    }

    /// <summary>Called from the audio thread. Interleaved float samples.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        if (!_running || _paused || samples.IsEmpty) return;
        var copy = new float[samples.Length]; samples.CopyTo(copy);
        if (!_q.TryAdd(copy)) DroppedBlocks++;
    }

    private void Loop()
    {
        var bytes = new byte[0];
        foreach (var block in _q.GetConsumingEnumerable())
        {
            if (bytes.Length < block.Length * 2) bytes = new byte[block.Length * 2];
            for (int i = 0; i < block.Length; i++) { short v = (short)Math.Clamp((int)(block[i] * 32767f), short.MinValue, short.MaxValue); bytes[i * 2] = (byte)v; bytes[i * 2 + 1] = (byte)(v >> 8); }
            _fs!.Write(bytes, 0, block.Length * 2); Interlocked.Add(ref _bytes, block.Length * 2);
        }
    }

    /// <summary>Finish the file (patches the WAV header).</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false; _q.CompleteAdding(); _thread?.Join(5000);
        if (_fs != null)
        {
            long dataLen = _bytes; _fs.Seek(0, SeekOrigin.Begin);
            var w = new BinaryWriter(_fs, System.Text.Encoding.ASCII, leaveOpen: true);
            w.Write("RIFF"u8.ToArray()); w.Write((uint)(36 + dataLen)); w.Write("WAVEfmt "u8.ToArray()); w.Write(16u); w.Write((ushort)1); w.Write((ushort)_channels);
            w.Write((uint)_rate); w.Write((uint)(_rate * _channels * 2)); w.Write((ushort)(_channels * 2)); w.Write((ushort)16); w.Write("data"u8.ToArray()); w.Write((uint)dataLen); w.Flush();
            _fs.Dispose(); _fs = null;
        }
    }

    public void Dispose() => Stop();
}

public enum RecordQuality { P720, P1080, P1440, P2160 }

public static class RecordProfiles
{
    public static (int W, int H, int VideoKbps) Get(RecordQuality q) => q switch
    {
        RecordQuality.P720 => (1280, 720, 4000), RecordQuality.P1080 => (1920, 1080, 8000), RecordQuality.P1440 => (2560, 1440, 16000), _ => (3840, 2160, 35000)
    };

    public const int AudioKbps = 192;

    /// <summary>Estimated final MP4 size in bytes for a duration (video + audio bitrate).</summary>
    public static long EstimateBytes(RecordQuality q, TimeSpan d, bool video = true) =>
        (long)((video ? Get(q).VideoKbps : 0) + AudioKbps) * 1000 / 8 * (long)d.TotalSeconds;
}

/// <summary>Locates and runs the bundled FFmpeg (LGPL build). Encoding runs in a separate process, off the audio and UI threads.</summary>
public static class Ffmpeg
{
    public static string? Find()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "ThirdParty", "ffmpeg", "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        { try { var p = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(p)) return p; } catch { } }
        return null;
    }

    private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Mux a recorded WebM video and a WAV into MP4 (H.264 via Media Foundation / AAC). <paramref name="videoDelaySec"/> aligns the video start to the audio start.</summary>
    public static List<string> MuxMp4Args(string video, string? wav, string output, int w, int h, int fps, int videoKbps, double videoDelaySec, string encoder = "h264_mf", int audioKbps = 0, bool youtube = false)
    {
        var a = new List<string> { "-y", "-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats" };
        if (Math.Abs(videoDelaySec) > 0.001) { a.Add("-itsoffset"); a.Add(Inv(videoDelaySec)); }
        a.AddRange(new[] { "-i", video });
        if (wav != null) a.AddRange(new[] { "-i", wav });
        a.AddRange(new[] {
            "-vf", $"scale={w}:{h}:force_original_aspect_ratio=decrease,pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,format=yuv420p", "-r", fps.ToString(),
            "-c:v", encoder, "-b:v", $"{videoKbps}k" });
        if (youtube)
        {   // YouTube-friendly: keyframe every 2 s, BT.709 tags; High profile where the encoder supports it (the Windows h264_mf encoder has no profile option).
            a.AddRange(new[] { "-g", (fps * 2).ToString() });
            if (encoder != "mpeg4") a.AddRange(new[] { "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709" });
            if (encoder is "h264_nvenc" or "h264_amf" or "h264_qsv") a.AddRange(new[] { "-profile:v", "high" });
        }
        if (wav != null) a.AddRange(new[] { "-c:a", "aac", "-b:a", $"{(audioKbps > 0 ? audioKbps : RecordProfiles.AudioKbps)}k", "-ar", "48000", "-shortest" }); else a.Add("-an");
        a.AddRange(new[] { "-movflags", "+faststart", output });
        return a;
    }

    public static List<string> AudioToMp3Args(string wav, string output) =>
        new() { "-y", "-hide_banner", "-loglevel", "error", "-i", wav, "-c:a", "libmp3lame", "-b:a", "256k", output };

    /// <summary>Runs ffmpeg and returns (exit code, stderr text).</summary>
    public static async Task<(int Code, string Err)> RunAsync(string exe, IEnumerable<string> args, IProgress<double>? progressSeconds = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var errTask = p.StandardError.ReadToEndAsync(ct);
        _ = Task.Run(async () =>
        {
            string? line; while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
                if (line.StartsWith("out_time_us=") && long.TryParse(line[12..], out var us) && us > 0) progressSeconds?.Report(us / 1e6);
        }, ct);
        await p.WaitForExitAsync(ct);
        var err = await errTask;
        if (p.ExitCode != 0) Log.Warning("ffmpeg failed ({Code}): {Err}", p.ExitCode, err);
        return (p.ExitCode, err);
    }
}
