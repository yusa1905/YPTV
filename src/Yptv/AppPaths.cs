using System.IO;

namespace Yptv;

// %LocalAppData%\YPTV. IPTV_DATA_DIR overrides it (used by tests so they don't touch the real cache).
// The pre-rename folder (IptvPlayer) is moved over once, if the new one doesn't exist yet.
static class AppPaths
{
    public static readonly string Dir = Resolve();

    static string Resolve()
    {
        if (Environment.GetEnvironmentVariable("IPTV_DATA_DIR") is { Length: > 0 } custom) return custom;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(root, AppInfo.Name);
        var old = Path.Combine(root, "IptvPlayer");
        try { if (!Directory.Exists(dir) && Directory.Exists(old)) Directory.Move(old, dir); } catch { }
        return dir;
    }
}
