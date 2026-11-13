namespace AudioReactor.Core.Models;

public sealed class Track
{
    public long Id { get; set; }
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Genre { get; set; } = "";
    public int Year { get; set; }
    public double DurationSec { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime? LastPlayed { get; set; }
    public int PlayCount { get; set; }
    public bool Favorite { get; set; }

    public string DurationText => TimeSpan.FromSeconds(DurationSec).ToString(DurationSec >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    public string Display => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} – {Title}";
}

public sealed class Station
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Country { get; set; } = "";
    public string Logo { get; set; } = "";
    public string Website { get; set; } = "";
    public bool Favorite { get; set; }
    public DateTime? LastPlayed { get; set; }
}

public sealed class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public int TrackCount { get; set; }
    public double DurationSec { get; set; }
}
