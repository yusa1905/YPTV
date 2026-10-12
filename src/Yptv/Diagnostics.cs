using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Yptv;

// The text behind "Copy diagnostics": versions, counts, settings without sources, the last log lines. Redacted twice
// (every log line already was): no address, login or absolute path leaves the machine.
static class Diagnostics
{
    public static string Version =>
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0").Split('+')[0];

    public static string Build(string? vlcVersion)
    {
        var s = Settings.Current;
        var text = new StringBuilder();
        text.AppendLine($"{AppInfo.Name} {Version}");
        text.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        text.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine($"LibVLC: {vlcVersion ?? "not loaded"}");
        text.AppendLine($"Playlists: {s.Sources.Count} ({s.Sources.Count(p => p.Xtream)} Xtream), active has {s.Active?.Count ?? 0:N0} entries");
        text.AppendLine($"Favorites in the active playlist: {s.Favorites.Count}, hidden groups: {s.ActivePrefs.Hidden.Count}");
        text.AppendLine($"Settings: theme={s.Theme} hw={s.HwDecoding} buffer={s.BufferMs}ms userAgent={(string.IsNullOrWhiteSpace(s.UserAgent) ? "default" : "custom")} "
            + $"startup={s.Startup} autoRefresh={s.AutoRefresh} aspect={s.Aspect} deinterlace={s.Deinterlace} mini={s.Mini} onTop={s.AlwaysOnTop} volume={s.Volume} muted={s.Muted}");
        text.AppendLine($"Caches: live {Caches.Format(Caches.Size(Caches.Kind.Live))}, VOD {Caches.Format(Caches.Size(Caches.Kind.Vod))}, logos {Caches.Format(Caches.Size(Caches.Kind.Logos))}");
        text.AppendLine();
        text.AppendLine("Last log lines:");
        text.Append(Log.LastLines());
        return Log.Redact(text.ToString());
    }
}
