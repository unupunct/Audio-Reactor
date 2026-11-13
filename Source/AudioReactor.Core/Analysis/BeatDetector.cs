namespace AudioReactor.Core.Analysis;

/// <summary>
/// Onset/beat detector using bass-band energy against an adaptive threshold, with a refractory period
/// (limits flicker) and BPM estimation from a histogram of inter-beat intervals.
/// </summary>
public sealed class BeatDetector
{
    private readonly Queue<float> _history = new();
    private readonly List<double> _intervals = new();
    private double _lastBeatTime = -10;
    private float _intensity;

    /// <summary>1 = default; higher = more sensitive (0.2..3).</summary>
    public float Sensitivity { get; set; } = 1f;
    public double Bpm { get; private set; }
    public float Intensity => _intensity;
    public int HistoryLength { get; set; } = 60; // ~1s at 60 analysis frames/s

    /// <summary>Feed one analysis frame. <paramref name="time"/> is seconds on a monotonic clock.</summary>
    public bool Process(float bassEnergy, double time)
    {
        _intensity *= 0.9f;
        float avg = 0, var = 0;
        foreach (var v in _history) avg += v;
        if (_history.Count > 0) avg /= _history.Count;
        foreach (var v in _history) var += (v - avg) * (v - avg);
        if (_history.Count > 0) var /= _history.Count;

        _history.Enqueue(bassEnergy);
        while (_history.Count > HistoryLength) _history.Dequeue();

        // Sensitivity lowers the required excess over the moving average.
        // Adaptive threshold: mean + k*stddev of the recent onset signal; sensitivity lowers k.
        float std = MathF.Sqrt(var);
        float k = Math.Max(0.5f, 1.6f - 0.4f * Math.Clamp(Sensitivity, 0.2f, 3f));
        float threshold = avg + k * std + 0.03f / Math.Max(0.2f, Sensitivity);
        double since = time - _lastBeatTime;
        bool beat = bassEnergy > threshold && since > 0.25; // max ~240 BPM / limited flicker

        if (beat)
        {
            if (since < 2.0) { _intervals.Add(since); if (_intervals.Count > 48) _intervals.RemoveAt(0); EstimateBpm(); }
            _lastBeatTime = time;
            _intensity = Math.Clamp((bassEnergy - avg) / Math.Max(0.05f, bassEnergy), 0.3f, 1f);
        }
        if (time - _lastBeatTime > 4) { Bpm = 0; _intervals.Clear(); }
        return beat;
    }

    private void EstimateBpm()
    {
        if (_intervals.Count < 4) return;
        // histogram of BPM, folded into 70..180 range
        var bins = new double[221];
        foreach (var iv in _intervals)
        {
            double bpm = 60.0 / iv;
            while (bpm < 70) bpm *= 2;
            while (bpm > 180) bpm /= 2;
            int b = (int)Math.Round(bpm);
            for (int d = -1; d <= 1; d++) if (b + d >= 0 && b + d < bins.Length) bins[b + d] += d == 0 ? 1 : 0.5;
        }
        int best = 0;
        for (int i = 0; i < bins.Length; i++) if (bins[i] > bins[best]) best = i;
        Bpm = Bpm == 0 ? best : Bpm * 0.8 + best * 0.2;
    }
}
