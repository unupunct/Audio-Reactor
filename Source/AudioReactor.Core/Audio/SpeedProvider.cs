using NAudio.Wave;

namespace AudioReactor.Core.Audio;

/// <summary>Variable-speed playback by linear-interpolated resampling (speed and pitch change together, like a turntable).</summary>
public sealed class SpeedProvider : ISampleProvider
{
    private readonly ISampleProvider _src; private readonly int _ch;
    private float[] _buf = new float[8192]; private int _have; private double _pos; // _pos in frames within _buf
    private bool _eof;
    /// <summary>0.5 .. 2.0; 1.0 is a pure pass-through.</summary>
    public float Speed { get; set; } = 1f;
    public WaveFormat WaveFormat => _src.WaveFormat;
    public SpeedProvider(ISampleProvider src) { _src = src; _ch = src.WaveFormat.Channels; }

    public int Read(Span<float> buffer)
    {
        float speed = Math.Clamp(Speed, 0.5f, 2f);
        if (Math.Abs(speed - 1f) < 0.001f && _have == 0) return _src.Read(buffer);
        int frames = buffer.Length / _ch, written = 0;
        while (written < frames)
        {
            int needFrames = (int)_pos + 2;
            while (_have / _ch < needFrames && !_eof)
            {
                if (_have + 4096 * _ch > _buf.Length) Array.Resize(ref _buf, _buf.Length * 2);
                int r = _src.Read(_buf.AsSpan(_have, 4096 * _ch)); if (r <= 0) { _eof = true; break; }
                _have += r;
            }
            int availFrames = _have / _ch;
            if ((int)_pos + 1 >= availFrames) break;
            int i0 = (int)_pos; float f = (float)(_pos - i0);
            for (int c = 0; c < _ch; c++) buffer[written * _ch + c] = _buf[i0 * _ch + c] * (1 - f) + _buf[(i0 + 1) * _ch + c] * f;
            written++; _pos += speed;
            // discard consumed frames to keep the buffer small
            int drop = (int)_pos; if (drop > 2048) { Buffer.BlockCopy(_buf, drop * _ch * 4, _buf, 0, (_have - drop * _ch) * 4); _have -= drop * _ch; _pos -= drop; }
        }
        return written * _ch;
    }
}
