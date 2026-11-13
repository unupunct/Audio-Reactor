using System.Diagnostics;
using System.Windows.Threading;
using AudioReactor.Core.Analysis;
using AudioReactor.Core.Lighting;

namespace AudioReactor.App.Services;

/// <summary>
/// Single analysis loop shared by the visualizer, lighting and recording. Analysis runs here on a UI timer,
/// never on the audio thread, so rendering cost cannot cause audio dropouts.
/// </summary>
public sealed class AnalysisService
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Stopwatch _cpu = Stopwatch.StartNew();
    private TimeSpan _lastCpu; private double _lastWall;

    public AudioFrame Frame { get; } = new();
    public LightingEngine Lighting { get; } = new();
    public double Time => _clock.Elapsed.TotalSeconds;
    /// <summary>Raised after every analysis tick (UI thread).</summary>
    public event Action<AudioFrame>? FrameReady;
    /// <summary>Approximate CPU usage of this process (0..100).</summary>
    public double CpuPercent { get; private set; }

    public void Start(AudioAnalyzer analyzer)
    {
        _lastCpu = Process.GetCurrentProcess().TotalProcessorTime; _lastWall = _cpu.Elapsed.TotalSeconds;
        int n = 0;
        _timer.Tick += (_, _) =>
        {
            analyzer.Analyze(Frame);
            Lighting.Update(Frame, Time);
            FrameReady?.Invoke(Frame);
            if (++n % 60 == 0)
            {
                var cpu = Process.GetCurrentProcess().TotalProcessorTime; double wall = _cpu.Elapsed.TotalSeconds;
                CpuPercent = (cpu - _lastCpu).TotalSeconds / Math.Max(0.001, wall - _lastWall) / Environment.ProcessorCount * 100; _lastCpu = cpu; _lastWall = wall;
            }
        };
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
