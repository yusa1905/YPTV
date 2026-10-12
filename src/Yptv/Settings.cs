using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yptv;

// Per playlist (keyed by playlist id): groups the user expanded or hid, favorites and the last channel (stable channel keys, never URLs).
public sealed class PlaylistPrefs
{
    public HashSet<string> Expanded { get; set; } = [];
    public HashSet<string> Hidden { get; set; } = [];
    public HashSet<string> Favorites { get; set; } = [];
    public string? LastKey { get; set; }
}

// settings.json in the data dir: saved playlists (sources sealed with DPAPI), per-playlist prefs, volume, theme.
public sealed class Settings
{
    public List<PlaylistEntry> Sources { get; set; } = [];
    public string? ActiveId { get; set; }
    public string AutoRefresh { get; set; } = "Start"; // Off, Start, 6, 12 or 24 (hours)
    public string Theme { get; set; } = "Dark"; // Dark, Light, Amoled or System
    public string Aspect { get; set; } = "Auto"; // Auto, 16:9, 4:3, Fill, Original
    public bool Deinterlace { get; set; }
    public string? SubLanguage { get; set; } // last subtitle language; "" = subtitles off
    public bool HwDecoding { get; set; } = true;
    public int BufferMs { get; set; } = 300; // 100-5000
    public string? UserAgent { get; set; } // null or empty = LibVLC's own
    public string Startup { get; set; } = "Resume"; // Resume (play the last channel), List (select it, don't play), None
    public bool Mini { get; set; }
    public bool AlwaysOnTop { get; set; }
    public double MiniWidth { get; set; } = 480;
    public double MiniHeight { get; set; } = 300;
    public int Volume { get; set; } = 80;
    public bool Muted { get; set; }
    public Dictionary<string, PlaylistPrefs> Playlists { get; set; } = []; // keyed by playlist id

    // Older files kept one source, one favorites set and one last channel globally. Read once, moved into the
    // playlist list on load, never written back (null is skipped).
    [JsonPropertyName("Source")] public string? LegacySource { get; set; }
    [JsonPropertyName("Favorites")] public HashSet<string>? LegacyFavorites { get; set; }
    [JsonPropertyName("LastKey")] public string? LegacyLastKey { get; set; }
    // Even older: the stream URL itself, read as a hash.
    public string? LastUrl
    {
        get => null;
        set { if (value != null) LegacyLastKey = KeyOf(value); }
    }

    [JsonIgnore] public PlaylistEntry? Active => ActiveId == null ? null : Sources.FirstOrDefault(s => s.Id == ActiveId);
    [JsonIgnore] public PlaylistPrefs ActivePrefs => ActiveId == null ? _none : PrefsFor(ActiveId);
    [JsonIgnore] public HashSet<string> Favorites => ActivePrefs.Favorites;
    [JsonIgnore]
    public string? LastKey
    {
        get => ActivePrefs.LastKey;
        set => ActivePrefs.LastKey = value;
    }

    readonly PlaylistPrefs _none = new(); // no playlist open: state goes nowhere

    // Stable 64-bit id of a string (stream URL, playlist source) for persisted state.
    public static string KeyOf(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 8));

    public PlaylistPrefs PrefsFor(string id)
    {
        if (!Playlists.TryGetValue(id, out var prefs)) Playlists[id] = prefs = new();
        return prefs;
    }

    // Adds a playlist, or returns the saved one with the same source.
    public PlaylistEntry AddSource(string source, string? name, string type)
    {
        var entry = PlaylistEntry.Create(source, name, type);
        var existing = Sources.FirstOrDefault(s => s.Id == entry.Id);
        if (existing != null) return existing;
        Sources.Add(entry);
        SaveSoon();
        return entry;
    }

    // Forgets a playlist with its prefs and caches.
    public void RemoveSource(PlaylistEntry entry)
    {
        Sources.Remove(entry);
        Playlists.Remove(entry.Id);
        if (ActiveId == entry.Id) ActiveId = null;
        PlaylistCache.DeleteAll(entry.Id);
        SaveSoon();
    }

    static readonly string FilePath = Path.Combine(AppPaths.Dir, "settings.json");
    static readonly object Gate = new();
    // Dormant (no due time) until something changes, so nothing ticks while idle.
    static readonly Timer SaveTimer = new(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);

    public static Settings Current { get; } = Load();

    static Settings Load()
    {
        Settings settings;
        try
        {
            settings = JsonSerializer.Deserialize(File.ReadAllBytes(FilePath), SettingsJson.Default.Settings) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(); // missing or corrupt
        }
        if (settings.Migrate()) SaveSoon();
        return settings;
    }

    // Moves the old single-playlist state into the list. Returns true if anything changed.
    bool Migrate()
    {
        bool changed = false;
        if (LegacySource is { Length: > 0 } source && Sources.Count == 0)
        {
            var entry = PlaylistEntry.Create(source, null, "m3u"); // id = hash of the source = the key the old prefs used
            Sources.Add(entry);
            ActiveId = entry.Id;
            var prefs = PrefsFor(entry.Id);
            // favorites saved as URLs by older versions become URL hashes (ChannelModel swaps those for stable keys)
            if (LegacyFavorites != null) prefs.Favorites.UnionWith(LegacyFavorites.Select(f => f.Contains("://") ? KeyOf(f) : f));
            prefs.LastKey ??= LegacyLastKey;
            PlaylistCache.MigrateLegacyFiles(entry.Id);
            changed = true;
        }
        if (LegacySource != null || LegacyFavorites != null || LegacyLastKey != null) changed = true;
        LegacySource = null;
        LegacyFavorites = null;
        LegacyLastKey = null;
        if (ActiveId != null && Active == null) ActiveId = Sources.FirstOrDefault()?.Id;
        return changed;
    }

    // Coalesces bursts (volume drags, zapping) into one write.
    public static void SaveSoon() => SaveTimer.Change(1000, Timeout.Infinite);

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Dir);
                var tmp = FilePath + ".tmp";
                File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(Current, SettingsJson.Default.Settings));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
partial class SettingsJson : JsonSerializerContext;
