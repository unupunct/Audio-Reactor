namespace AudioReactor.Core.Analysis;

/// <summary>In-place radix-2 FFT with precomputed twiddles; allocation-free per call.</summary>
public sealed class Fft
{
    private readonly int _n;
    private readonly float[] _cos, _sin;
    private readonly int[] _rev;

    public int Size => _n;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("Size must be a power of two", nameof(size));
        _n = size;
        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            _cos[i] = (float)Math.Cos(-2 * Math.PI * i / size);
            _sin[i] = (float)Math.Sin(-2 * Math.PI * i / size);
        }
        _rev = new int[size];
        int bits = (int)Math.Log2(size);
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            _rev[i] = r;
        }
    }

    public void Transform(float[] re, float[] im)
    {
        for (int i = 0; i < _n; i++)
        {
            int j = _rev[i];
            if (j > i) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= _n; len <<= 1)
        {
            int half = len / 2, step = _n / len;
            for (int i = 0; i < _n; i += len)
                for (int k = 0; k < half; k++)
                {
                    float c = _cos[k * step], s = _sin[k * step];
                    int a = i + k, b = a + half;
                    float tr = re[b] * c - im[b] * s;
                    float ti = re[b] * s + im[b] * c;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                }
        }
    }
}
