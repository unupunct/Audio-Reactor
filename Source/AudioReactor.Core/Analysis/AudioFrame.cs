namespace AudioReactor.Core.Analysis;

/// <summary>Snapshot of analysis results consumed by visualizers and lighting. All levels are 0..1.</summary>
public sealed class AudioFrame
{
    public float Bass, LowMid, Mid, HighMid, Treble, Energy, Volume, BeatIntensity;
    public bool Beat;
    public double Bpm;
    public float[] Spectrum = new float[64];   // log-spaced, 0..1
    public float[] Waveform = new float[256];  // mono, -1..1
    public float[] WaveLeft = new float[128];
    public float[] WaveRight = new float[128];
}
