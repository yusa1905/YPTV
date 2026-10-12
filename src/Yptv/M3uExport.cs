using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Yptv;

// Writes a playlist back out as .m3u, straight from the cache files (no network). One entry in memory at a time.
// Kept fields: name, tvg-id, tvg-name, tvg-logo, group-title, URL. Other attributes of the original are not cached and are dropped.
static class M3uExport
{
    // Returns the number of channels written per kind that exist in the cache.
    public static async Task<int> WriteAsync(string id, IReadOnlyCollection<ChannelKind> kinds, HashSet<string>? hidden, string path, CancellationToken ct)
    {
        int count = 0;
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await using var writer = new StreamWriter(file, new UTF8Encoding(false), 64 * 1024);
        await writer.WriteLineAsync("#EXTM3U");
        foreach (var kind in kinds)
        {
            await foreach (var channel in StreamChannels(PlaylistCache.FileFor(kind, id), ct))
            {
                if (hidden != null && hidden.Contains(channel.Group ?? "Ungrouped")) continue;
                await writer.WriteLineAsync(Entry(channel));
                await writer.WriteLineAsync(channel.Url);
                count++;
            }
        }
        return count;
    }

    // First stream URL of a playlist (live, else movies, else series): enough to read an Xtream login from.
    public static async Task<string?> FirstUrlAsync(string id)
    {
        foreach (var kind in new[] { ChannelKind.Live, ChannelKind.Movie, ChannelKind.Series })
            await foreach (var channel in StreamChannels(PlaylistCache.FileFor(kind, id), CancellationToken.None))
                return channel.Url;
        return null;
    }

    static string Entry(Channel c)
    {
        var sb = new StringBuilder("#EXTINF:-1");
        Attr(sb, "tvg-id", c.TvgId);
        Attr(sb, "tvg-name", c.TvgName);
        Attr(sb, "tvg-logo", c.Logo);
        Attr(sb, "group-title", c.Group);
        sb.Append(',').Append(OneLine(c.Name));
        return sb.ToString();
    }

    static void Attr(StringBuilder sb, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value)) sb.Append(' ').Append(key).Append("=\"").Append(OneLine(value).Replace('"', '\'')).Append('"');
    }

    static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');

    // The cache is { "Source": "", "Channels": [ ... ] }. Read it in pieces so a 180k-row file never sits in memory.
    static async IAsyncEnumerable<Channel> StreamChannels(string path, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!File.Exists(path)) yield break;
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);
        var buffer = new byte[128 * 1024];
        int length = 0;
        var state = new JsonReaderState();
        bool inArray = false;
        while (true)
        {
            if (length == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2); // one entry larger than the buffer
            int read = await fs.ReadAsync(buffer.AsMemory(length), ct);
            length += read;
            var batch = Parse(buffer.AsSpan(0, length), read == 0, ref state, ref inArray, out int consumed);
            foreach (var channel in batch) yield return channel;
            Buffer.BlockCopy(buffer, consumed, buffer, 0, length - consumed);
            length -= consumed;
            if (read == 0) yield break;
        }
    }

    // Parses as many complete entries as the buffer holds; consumed is where to continue.
    static List<Channel> Parse(ReadOnlySpan<byte> data, bool final, ref JsonReaderState state, ref bool inArray, out int consumed)
    {
        var batch = new List<Channel>();
        var reader = new Utf8JsonReader(data, final, state);
        long goodBytes = 0;
        var goodState = state;
        bool goodInArray = inArray;
        while (reader.Read())
        {
            if (!goodInArray)
            {
                if (reader.TokenType != JsonTokenType.StartArray) continue; // "Source" and its value come first
                goodInArray = true;
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                var probe = reader;
                if (!probe.TrySkip()) break; // entry not complete yet
                var channel = JsonSerializer.Deserialize(ref reader, CacheJson.Default.Channel);
                if (channel != null) batch.Add(channel);
            }
            else if (reader.TokenType == JsonTokenType.EndArray) goodInArray = false;
            goodBytes = reader.BytesConsumed;
            goodState = reader.CurrentState;
        }
        consumed = (int)goodBytes;
        state = goodState;
        inArray = goodInArray;
        return batch;
    }
}
