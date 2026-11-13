using AudioReactor.Core.Analysis;
using NAudio.Wave;

namespace AudioReactor.Core.Audio;

/// <summary>Pass-through provider that applies volume and copies samples to the analyzer.</summary>
public sealed class TapProvider : ISampleProvider
{
    private readonly ISampleProvider _src; private readonly AudioAnalyzer _an;
    public float Volume { get; set; } = 1f;
    /// <summary>Optional recording sink; receives the pre-volume signal without blocking the audio thread.</summary>
    public volatile Recording.AudioRecorder? Recorder;
    public WaveFormat WaveFormat => _src.WaveFormat;
    public TapProvider(ISampleProvider src, AudioAnalyzer an) { _src = src; _an = an; _an.SampleRate = src.WaveFormat.SampleRate; }
    public int Read(Span<float> buffer)
    {
        int n = _src.Read(buffer);
        // Analyze the pre-volume signal so visuals don't vanish when the user turns volume down.
        _an.Write(buffer[..n], WaveFormat.Channels);
        Recorder?.Write(buffer[..n]);
        float v = Volume;
        for (int i = 0; i < n; i++) buffer[i] *= v;
        return n;
    }
}
