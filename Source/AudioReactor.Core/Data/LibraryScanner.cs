using AudioReactor.Core.Models;
using Serilog;

namespace AudioReactor.Core.Data;

/// <summary>Scans files/folders, reads tags with TagLib and stores tracks. Corrupt files are logged and skipped.</summary>
public static class LibraryScanner
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".ogg", ".aac", ".m4a", ".wma", ".opus" };

    public static Track? ReadTrack(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path);
            var t = f.Tag;
            return new Track
            {
                Path = path,
                Title = string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(path) : t.Title.Trim(),
                Artist = t.FirstPerformer?.Trim() ?? "",
                Album = t.Album?.Trim() ?? "",
                Genre = t.FirstGenre?.Trim() ?? "",
                Year = (int)t.Year,
                DurationSec = f.Properties.Duration.TotalSeconds
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Skipping unreadable file {Path}", path);
            return null;
        }
    }

    public static byte[]? ReadArtwork(string path)
    {
        try { using var f = TagLib.File.Create(path); return f.Tag.Pictures.Length > 0 ? f.Tag.Pictures[0].Data.Data : null; }
        catch { return null; }
    }

    public static IEnumerable<string> EnumerateAudioFiles(string folder)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        return Directory.EnumerateFiles(folder, "*.*", opts).Where(p => Extensions.Contains(Path.GetExtension(p)));
    }

    /// <summary>Imports the given files/folders; returns the number of tracks added or updated.</summary>
    public static int Import(Database db, IEnumerable<string> paths, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        int n = 0;
        foreach (var p in paths)
        {
            var files = Directory.Exists(p) ? EnumerateAudioFiles(p) : (File.Exists(p) && Extensions.Contains(Path.GetExtension(p)) ? new[] { p } : Array.Empty<string>());
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var t = ReadTrack(file);
                if (t == null) continue;
                db.UpsertTrack(t); n++;
                progress?.Report(n);
            }
        }
        return n;
    }
}
