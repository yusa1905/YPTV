using System.IO;
using System.Net.Http;

namespace Yptv;

// Streams an M3U/M3U8 playlist line by line from a file path or HTTP(S) URL, off the UI thread.
// Live entries are returned; movies and series go to onVod as they are read, so they never pile up in RAM.
static class M3uParser
{
    public static Task<List<Channel>> LoadAsync(string source, Action<Channel> onVod, CancellationToken ct = default) => Task.Run(async () =>
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            using var resp = await Http.Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var http = new StreamReader(stream);
            return await ParseAsync(http, onVod, ct);
        }
        var options = new FileStreamOptions { BufferSize = 64 * 1024, Options = FileOptions.Asynchronous | FileOptions.SequentialScan };
        using var file = new StreamReader(source, options);
        return await ParseAsync(file, onVod, ct);
    }, ct);

    public static async Task<List<Channel>> ParseAsync(TextReader reader, Action<Channel> onVod, CancellationToken ct = default)
    {
        var channels = new List<Channel>();
        Channel? pending = null; // from #EXTINF, waiting for its URL line
        string? extGroup = null;
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                pending = ParseExtinf(line);
                extGroup = null;
            }
            else if (line.StartsWith("#EXTGRP:", StringComparison.OrdinalIgnoreCase)) extGroup = Str(line.AsSpan(8));
            else if (line[0] != '#')
            {
                var kind = Channel.Classify(line);
                var channel = pending == null
                    ? new Channel(line, line, extGroup, Kind: kind)
                    : pending with { Url = line, Name = pending.Name.Length > 0 ? pending.Name : line, Group = pending.Group ?? extGroup, Kind = kind };
                if (kind == ChannelKind.Live) channels.Add(channel);
                else onVod(channel);
                pending = null;
                extGroup = null;
            }
        }
        return channels;
    }

    // #EXTINF:-1 tvg-id="x" tvg-name="y" tvg-logo="z" group-title="g",Display name
    static Channel ParseExtinf(string line)
    {
        string? id = null, tvgName = null, logo = null, group = null;
        var s = line.AsSpan(8);
        int i = 0;
        while (i < s.Length && s[i] is not (' ' or '\t' or ',')) i++; // duration
        while (i < s.Length && s[i] != ',')
        {
            if (s[i] is ' ' or '\t') { i++; continue; }
            int k = i;
            while (i < s.Length && s[i] is not ('=' or ' ' or '\t' or ',')) i++;
            var key = s[k..i];
            if (i >= s.Length || s[i] != '=') continue;
            ReadOnlySpan<char> value;
            if (++i < s.Length && s[i] == '"')
            {
                int end = s[(i + 1)..].IndexOf('"');
                end = end < 0 ? s.Length : i + 1 + end;
                value = s[(i + 1)..end];
                i = Math.Min(end + 1, s.Length);
            }
            else
            {
                int v = i;
                while (i < s.Length && s[i] is not (' ' or '\t' or ',')) i++;
                value = s[v..i];
            }
            if (key.Equals("tvg-id", StringComparison.OrdinalIgnoreCase)) id = Str(value);
            else if (key.Equals("tvg-name", StringComparison.OrdinalIgnoreCase)) tvgName = Str(value);
            else if (key.Equals("tvg-logo", StringComparison.OrdinalIgnoreCase)) logo = Str(value);
            else if (key.Equals("group-title", StringComparison.OrdinalIgnoreCase)) group = Str(value);
        }
        var title = i < s.Length ? Str(s[(i + 1)..]) : null;
        return new Channel(title ?? tvgName ?? "", "", group, id, tvgName, logo);
    }

    static string? Str(ReadOnlySpan<char> s)
    {
        s = s.Trim();
        return s.IsEmpty ? null : s.ToString();
    }
}
