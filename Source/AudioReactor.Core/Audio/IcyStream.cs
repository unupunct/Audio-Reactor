namespace AudioReactor.Core.Audio;

/// <summary>Wraps an HTTP stream, strips SHOUTcast/Icecast in-band metadata and raises <see cref="MetadataChanged"/>.</summary>
public sealed class IcyStream : Stream
{
    private readonly Stream _inner; private readonly int _metaInt; private int _untilMeta; private long _audioBytes;
    public event Action<string>? MetadataChanged;
    public IcyStream(Stream inner, int metaInt) { _inner = inner; _metaInt = metaInt; _untilMeta = metaInt; }

    /// <summary>
    /// Fills <paramref name="count"/> bytes whenever the stream allows (short reads only at end of stream). NAudio's MP3 frame reader treats a
    /// short read as "end of stream", but network streams and ICY metadata boundaries legitimately produce partial reads.
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            if (_metaInt > 0 && _untilMeta == 0)
            {
                int len = _inner.ReadByte();
                if (len < 0) break;
                if (len > 0)
                {
                    var meta = new byte[len * 16]; ReadFully(meta);
                    var t = ParseTitle(System.Text.Encoding.UTF8.GetString(meta).TrimEnd('\0'));
                    if (!string.IsNullOrWhiteSpace(t)) MetadataChanged?.Invoke(t);
                }
                _untilMeta = _metaInt;
            }
            int want = _metaInt > 0 ? Math.Min(count - total, _untilMeta) : count - total;
            int r = _inner.Read(buffer, offset + total, want);
            if (r <= 0) break;
            total += r; _audioBytes += r; if (_metaInt > 0) _untilMeta -= r;
        }
        return total;
    }

    private void ReadFully(byte[] b) { int o = 0; while (o < b.Length) { int r = _inner.Read(b, o, b.Length - o); if (r <= 0) break; o += r; } }

    public static string? ParseTitle(string meta)
    {
        const string key = "StreamTitle='";
        int i = meta.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        i += key.Length;
        int e = meta.IndexOf("';", i, StringComparison.Ordinal);
        if (e < 0) e = meta.LastIndexOf('\'');
        return e > i ? meta[i..e] : null;
    }

    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    /// <summary>Number of audio bytes read so far. The MP3 frame reader queries this; setting it is not supported (live streams cannot seek).</summary>
    public override long Position { get => _audioBytes; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    protected override void Dispose(bool d) { if (d) _inner.Dispose(); base.Dispose(d); }
}
