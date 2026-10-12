using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yptv;

public sealed record CachedPlaylist(string Source, List<Channel> Channels);

// Parsed playlist as JSON in %LocalAppData%\\YPTV\lists\<playlist id>, so the next start is instant.
// Live is loaded at startup; movies and series each have their own file, read only when that scope opens.
// The cache files carry no source: the folder is the playlist.
static class PlaylistCache
{
    static readonly string Root = Path.Combine(AppPaths.Dir, "lists");

    public static string DirFor(string id) => Path.Combine(Root, id);

    public static string FileFor(ChannelKind kind, string id) =>
        Path.Combine(DirFor(id), kind switch { ChannelKind.Movie => "movies.json", ChannelKind.Series => "series.json", _ => "playlist.json" });

    // Null when missing or corrupt.
    public static Task<List<Channel>?> LoadAsync(ChannelKind kind, string id) => Task.Run(async () =>
    {
        try
        {
            await using var fs = new FileStream(FileFor(kind, id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
            var cached = await JsonSerializer.DeserializeAsync(fs, CacheJson.Default.CachedPlaylist);
            return cached?.Channels;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    });

    // Write to a temp file then swap, so a crash never leaves a half-written cache.
    public static Task SaveAsync(string id, List<Channel> channels) => Task.Run(async () =>
    {
        Directory.CreateDirectory(DirFor(id));
        var file = FileFor(ChannelKind.Live, id);
        var tmp = file + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            await JsonSerializer.SerializeAsync(fs, new CachedPlaylist("", channels), CacheJson.Default.CachedPlaylist);
        File.Move(tmp, file, overwrite: true);
    });

    // Forget everything cached for a removed playlist.
    public static void DeleteAll(string id)
    {
        try { Directory.Delete(DirFor(id), recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // Files written by versions with a single playlist sit directly in the data folder: move them under the playlist id.
    public static void MigrateLegacyFiles(string id)
    {
        try
        {
            Directory.CreateDirectory(DirFor(id));
            foreach (var name in new[] { "playlist.json", "movies.json", "series.json" })
            {
                var old = Path.Combine(AppPaths.Dir, name);
                if (File.Exists(old)) File.Move(old, Path.Combine(DirFor(id), name), overwrite: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

// Streams entries into a cache file one by one while the playlist is parsed; Commit swaps it in.
sealed class VodWriter : IDisposable
{
    readonly string _file, _tmp;
    readonly FileStream _stream;
    readonly Utf8JsonWriter _json;
    int _pending;
    bool _committed;

    public int Count { get; private set; }

    public VodWriter(ChannelKind kind, string id)
    {
        Directory.CreateDirectory(PlaylistCache.DirFor(id));
        _file = PlaylistCache.FileFor(kind, id);
        _tmp = _file + ".tmp";
        _stream = new FileStream(_tmp, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024);
        _json = new Utf8JsonWriter(_stream);
        _json.WriteStartObject();
        _json.WriteString("Source", "");
        _json.WriteStartArray("Channels");
    }

    public void Add(Channel channel)
    {
        JsonSerializer.Serialize(_json, channel, CacheJson.Default.Channel);
        Count++;
        if (++_pending >= 1000)
        {
            _json.Flush();
            _pending = 0;
        }
    }

    public void Commit()
    {
        _json.WriteEndArray();
        _json.WriteEndObject();
        _json.Dispose(); // flushes
        _stream.Dispose();
        File.Move(_tmp, _file, overwrite: true);
        _committed = true;
    }

    // Without Commit (cancelled or failed refresh) the temp file is dropped and the old cache stays.
    public void Dispose()
    {
        if (_committed) return;
        _json.Dispose();
        _stream.Dispose();
        try { File.Delete(_tmp); }
        catch (IOException) { }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(CachedPlaylist))]
[JsonSerializable(typeof(Channel))]
partial class CacheJson : JsonSerializerContext;
