using System.IO;
using System.Text.Json.Serialization;

namespace Yptv;

// One saved playlist. The source (URL, file path or Xtream link) is DPAPI-sealed in settings.json; Host is for display only.
// Id is stable and keys everything saved per playlist (prefs, favorites, last channel, caches).
public sealed class PlaylistEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "m3u"; // "m3u" or "xtream" (made from the Xtream login form)
    public string? Secret { get; set; }       // sealed source
    public string Host { get; set; } = "";    // host only, or the file name
    public int Count { get; set; }            // entries in the last refresh (live + movies + series)
    public long RefreshedUtc { get; set; }    // unix seconds, 0 = never
    public bool Xtream { get; set; }          // credentials found in the link or stream URLs: account info is available

    string? _source;

    [JsonIgnore] public string Source => _source ??= Dpapi.Open(Secret) ?? "";
    [JsonIgnore] public bool IsFile => !Uri.TryCreate(Source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https");
    [JsonIgnore] public string TypeText => Xtream ? "Xtream" : IsFile ? "File" : "M3U";

    public static PlaylistEntry Create(string source, string? name, string type)
    {
        var entry = new PlaylistEntry
        {
            Id = global::Yptv.Settings.KeyOf(source),
            Type = type,
            Secret = Dpapi.Seal(source),
            Xtream = type == "xtream",
            _source = source,
        };
        entry.Host = HostOf(source);
        entry.Name = string.IsNullOrWhiteSpace(name) ? entry.Host : name.Trim();
        return entry;
    }

    // "provider.example" for a URL, the file name for a path. Never the query string or the login.
    public static string HostOf(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri.Host;
        try { return Path.GetFileNameWithoutExtension(source); }
        catch (ArgumentException) { return source.Length > 24 ? source[..24] : source; }
    }
}
