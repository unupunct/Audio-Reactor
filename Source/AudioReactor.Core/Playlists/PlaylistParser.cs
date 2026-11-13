using System.Text.RegularExpressions;

namespace AudioReactor.Core.Playlists;

public sealed record PlaylistEntry(string Url, string? Title);

/// <summary>Parses M3U/M3U8 and PLS playlists (local files or station lists).</summary>
public static class PlaylistParser
{
    public static List<PlaylistEntry> Parse(string text, string? fileNameHint = null)
    {
        var trimmed = text.TrimStart('﻿', ' ', '\r', '\n');
        if (trimmed.StartsWith("[playlist]", StringComparison.OrdinalIgnoreCase) ||
            (fileNameHint?.EndsWith(".pls", StringComparison.OrdinalIgnoreCase) ?? false))
            return ParsePls(text);
        return ParseM3u(text);
    }

    public static List<PlaylistEntry> ParseM3u(string text)
    {
        var result = new List<PlaylistEntry>();
        string? title = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t', '﻿');
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
            {
                int comma = line.IndexOf(',');
                title = comma >= 0 ? line[(comma + 1)..].Trim() : null;
            }
            else if (line.StartsWith('#')) continue;
            else { result.Add(new PlaylistEntry(line, string.IsNullOrWhiteSpace(title) ? null : title)); title = null; }
        }
        return result;
    }

    public static List<PlaylistEntry> ParsePls(string text)
    {
        var files = new SortedDictionary<int, string>();
        var titles = new Dictionary<int, string>();
        var rx = new Regex(@"^(File|Title)(\d+)\s*=\s*(.*)$", RegexOptions.IgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var m = rx.Match(raw.Trim('\r', ' ', '\t', '﻿'));
            if (!m.Success) continue;
            int n = int.Parse(m.Groups[2].Value);
            if (m.Groups[1].Value.Equals("File", StringComparison.OrdinalIgnoreCase)) files[n] = m.Groups[3].Value.Trim();
            else titles[n] = m.Groups[3].Value.Trim();
        }
        return files.Select(kv => new PlaylistEntry(kv.Value, titles.GetValueOrDefault(kv.Key))).ToList();
    }

    public static string ToM3u(IEnumerable<(string path, string title, int seconds)> items)
    {
        var sb = new System.Text.StringBuilder("#EXTM3U\n");
        foreach (var (p, t, s) in items) sb.Append("#EXTINF:").Append(s).Append(',').Append(t).Append('\n').Append(p).Append('\n');
        return sb.ToString();
    }
}
