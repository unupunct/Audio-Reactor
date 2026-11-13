using NAudio.Wave;
using Serilog;

namespace AudioReactor.Core.Audio;

public enum RadioState { Idle, Connecting, Buffering, Playing, Reconnecting, Failed }

/// <summary>
/// Streams an internet radio station on a background thread into a buffered provider, with automatic reconnect.
/// MP3 (Icecast/SHOUTcast) is decoded here; other codecs (AAC/HLS) fall back to Media Foundation in AudioEngine.
/// </summary>
public sealed class RadioSource : IDisposable
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout.InfiniteTimeSpan };
    private CancellationTokenSource? _cts;
    private BufferedWaveProvider? _buffer;
    private readonly string _url;
    private readonly int _bufferSeconds;
    private readonly TaskCompletionSource<WaveFormat> _formatReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RadioState State { get; private set; }
    public string? StationName { get; private set; }
    public string? Genre { get; private set; }
    public string? NowPlaying { get; private set; }
    public string? ContentType { get; private set; }
    public int Bitrate { get; private set; }
    public double BufferedSeconds => _buffer?.BufferedDuration.TotalSeconds ?? 0;
    public event Action<RadioState>? StateChanged;
    public event Action<string>? MetadataChanged;
    public event Action<string>? Error;

    public RadioSource(string url, int bufferSeconds = 8) { _url = url; _bufferSeconds = Math.Clamp(bufferSeconds, 2, 60); }

    /// <summary>Starts the connection; completes when the first audio format is known.</summary>
    public async Task<ISampleProvider> StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => RunAsync(_cts.Token));
        await _formatReady.Task.WaitAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        return _buffer!.ToSampleProvider();
    }

    private void Set(RadioState s) { State = s; StateChanged?.Invoke(s); }

    private async Task RunAsync(CancellationToken ct)
    {
        int attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Set(attempt == 0 ? RadioState.Connecting : RadioState.Reconnecting);
                await StreamOnce(ct).ConfigureAwait(false);
                attempt = 0; // server closed cleanly -> reconnect
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Radio stream error for {Url}", _url);
                Error?.Invoke(ex.Message);
                if (!_formatReady.Task.IsCompleted && attempt >= 1) { _formatReady.TrySetException(ex); Set(RadioState.Failed); return; }
            }
            attempt++;
            Set(RadioState.Reconnecting);
            try { await Task.Delay(Math.Min(15000, 1000 * attempt * attempt), ct).ConfigureAwait(false); } catch { break; }
        }
    }

    private async Task StreamOnce(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _url);
        req.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        req.Headers.TryAddWithoutValidation("User-Agent", "AudioReactor/1.0");
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        string Hdr(string n) => resp.Headers.TryGetValues(n, out var v) ? v.First() : "";
        StationName = Hdr("icy-name"); Genre = Hdr("icy-genre");
        int.TryParse(Hdr("icy-br"), out var br); Bitrate = br;
        ContentType = resp.Content.Headers.ContentType?.MediaType ?? "";
        int.TryParse(Hdr("icy-metaint"), out var metaInt);

        if (ContentType.Contains("aac") || ContentType.Contains("mpegurl") || ContentType.Contains("mp4") || ContentType.Contains("ogg"))
            throw new NotSupportedException($"Stream type {ContentType} needs the Media Foundation fallback.");

        await using var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var icy = new IcyStream(net, metaInt);
        icy.MetadataChanged += t => { NowPlaying = t; MetadataChanged?.Invoke(t); };
        IMp3FrameDecompressor? dec = null;
        try
        {
            var buf = new byte[16384 * 4];
            while (!ct.IsCancellationRequested)
            {
                Mp3Frame? frame;
                try { frame = Mp3Frame.LoadFromStream(icy); } catch (EndOfStreamException) { break; }
                if (frame == null) break;
                if (dec == null)
                {
                    var wf = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2, frame.FrameLength, frame.BitRate);
                    dec = new AcmMp3FrameDecompressor(wf);
                    if (_buffer == null)
                    {
                        _buffer = new BufferedWaveProvider(dec.OutputFormat, TimeSpan.FromSeconds(_bufferSeconds + 10)) { DiscardOnBufferOverflow = true };
                        _formatReady.TrySetResult(dec.OutputFormat);
                    }
                    Set(RadioState.Buffering);
                }
                int n = dec.DecompressFrame(frame, buf, 0);
                if (n > 0)
                {
                    // Back-pressure: don't read far ahead of playback.
                    while (_buffer!.BufferedDuration.TotalSeconds > _bufferSeconds + 4 && !ct.IsCancellationRequested) await Task.Delay(50, ct).ConfigureAwait(false);
                    _buffer!.AddSamples(buf, 0, n);
                    if (State != RadioState.Playing && _buffer.BufferedDuration.TotalSeconds >= Math.Min(_bufferSeconds, 3)) Set(RadioState.Playing);
                }
            }
        }
        finally { dec?.Dispose(); }
    }

    public void Dispose() { _cts?.Cancel(); _cts?.Dispose(); }
}
