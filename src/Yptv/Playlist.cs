namespace Yptv;

// Cache first (instant), then refresh from the source in the background.
static class Playlist
{
    // Calls onChannels with the cached live list (if any, fromCache = true), then, when refresh is true or there is
    // no cache, with the fresh one. Movies and series are streamed into their cache files while parsing and not kept in memory.
    // Throws if the refresh fails; whatever was delivered from the cache stays valid.
    public static async Task LoadAsync(PlaylistEntry entry, bool refresh, Action<List<Channel>, bool> onChannels, CancellationToken ct = default)
    {
        var id = entry.Id;
        var cached = await PlaylistCache.LoadAsync(ChannelKind.Live, id);
        if (cached != null) onChannels(cached, true);
        if (cached != null && !refresh) return;

        using var movies = new VodWriter(ChannelKind.Movie, id);
        using var series = new VodWriter(ChannelKind.Series, id);
        var fresh = await M3uParser.LoadAsync(entry.Source, c => (c.Kind == ChannelKind.Movie ? movies : series).Add(c), ct);
        onChannels(fresh, false);
        await PlaylistCache.SaveAsync(id, fresh);
        movies.Commit();
        series.Commit();
        entry.Count = fresh.Count + movies.Count + series.Count;
        entry.RefreshedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Settings.SaveSoon();
    }

    public static Task<List<Channel>?> LoadVodAsync(ChannelKind kind, PlaylistEntry entry) => PlaylistCache.LoadAsync(kind, entry.Id);
}
