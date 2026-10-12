namespace Yptv;

public enum ChannelKind : byte { Live, Movie, Series }

// Kept lean: big playlists can hold 200k+ of these.
public sealed record Channel(string Name, string Url, string? Group = null, string? TvgId = null, string? TvgName = null, string? Logo = null, ChannelKind Kind = ChannelKind.Live)
{
    public const string KeyPrefix = "k:";

    // Identity for favorites / last channel that survives a changed host, user or password:
    // tvg-id + name + stream id (last path segment without extension).
    public string StableKey()
    {
        var path = Url;
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0) path = path[..cut];
        path = path.TrimEnd('/');
        var id = path[(path.LastIndexOf('/') + 1)..];
        int dot = id.LastIndexOf('.');
        if (dot > 0) id = id[..dot];
        return KeyPrefix + Settings.KeyOf($"{TvgId}|{Name}|{id}");
    }

    // Xtream-style panels put the class in the URL path.
    public static ChannelKind Classify(string url)
    {
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        var path = scheme < 0 ? url.AsSpan() : url.AsSpan(scheme + 3);
        int slash = path.IndexOf('/');
        if (slash < 0) return ChannelKind.Live;
        path = path[slash..];
        if (path.Contains("/movie/", StringComparison.OrdinalIgnoreCase)) return ChannelKind.Movie;
        if (path.Contains("/series/", StringComparison.OrdinalIgnoreCase)) return ChannelKind.Series;
        return ChannelKind.Live;
    }
}
