using System.IO;

namespace Yptv;

// Sizes of the three disk caches and clearing them (Settings dialog). Live = playlist.json of every playlist,
// VOD = movies.json + series.json, Logos = the thumbnail folder. Errors are ignored: a locked file stays.
static class Caches
{
    public enum Kind { Live, Vod, Logos }

    static IEnumerable<string> Files(Kind kind)
    {
        var lists = Path.Combine(AppPaths.Dir, "lists");
        if (kind == Kind.Logos)
        {
            var logos = Path.Combine(AppPaths.Dir, "logos");
            return Directory.Exists(logos) ? Directory.EnumerateFiles(logos, "*", SearchOption.AllDirectories) : [];
        }
        if (!Directory.Exists(lists)) return [];
        var names = kind == Kind.Live ? new[] { "playlist.json" } : ["movies.json", "series.json"];
        return Directory.EnumerateFiles(lists, "*.json", SearchOption.AllDirectories).Where(f => names.Contains(Path.GetFileName(f)));
    }

    public static long Size(Kind kind)
    {
        long total = 0;
        try
        {
            foreach (var file in Files(kind)) total += new FileInfo(file).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return total;
    }

    // Clearing a playlist cache also forgets when it was refreshed, so the next start loads it from the provider.
    public static void Clear(Kind kind)
    {
        try
        {
            foreach (var file in Files(kind).ToList())
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (kind != Kind.Logos)
        {
            foreach (var entry in Settings.Current.Sources) entry.RefreshedUtc = 0;
            Settings.SaveSoon();
        }
        Log.Info($"cache cleared: {kind}");
    }

    public static string Format(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0} KB" : $"{bytes / 1048576.0:0.0} MB";
}
