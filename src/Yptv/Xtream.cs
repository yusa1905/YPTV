using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Yptv;

// Credentials of an Xtream Codes style panel: found in the playlist link or in a stream URL, never stored on their own.
public sealed record XtreamLogin(string Scheme, string Host, int? Port, string User, string Pass)
{
    public string Base => $"{Scheme}://{Host}{(Port is { } p ? ":" + p : "")}";
    public string ApiUrl => $"{Base}/player_api.php?username={Uri.EscapeDataString(User)}&password={Uri.EscapeDataString(Pass)}";

    // The m3u_plus link. Output format: ts if the account allows it, else m3u8, else the first one listed.
    public string LinkFor(string[]? formats)
    {
        var output = formats is not { Length: > 0 } ? "ts"
            : formats.Contains("ts", StringComparer.OrdinalIgnoreCase) ? "ts"
            : formats.Contains("m3u8", StringComparer.OrdinalIgnoreCase) ? "m3u8"
            : formats[0];
        return $"{Base}/get.php?username={Uri.EscapeDataString(User)}&password={Uri.EscapeDataString(Pass)}&type=m3u_plus&output={Uri.EscapeDataString(output)}";
    }
}

// What the panel says about the account (player_api.php). Every field is optional; no login is kept here.
public sealed record AccountInfo(string? Status, long? Expires, bool? Trial, int? ActiveCons, int? MaxCons, long? Created,
    string? Timezone, string? ServerTime, string[] Formats, long Fetched);

static class Xtream
{
    static readonly Regex StreamShape = new(@"^(https?)://([^/:?#]+)(?::(\d+))?/(?:(?:live|movie|series)/)?([^/?#]+)/([^/?#]+)/[^/?#]+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // 1. get.php?username=..&password=.. in the playlist link, 2. the shape of a stream URL as fallback.
    public static XtreamLogin? Detect(string source, string? sampleStreamUrl)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            string? user = null, pass = null;
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq < 0) continue;
                var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                var key = pair[..eq];
                if (key.Equals("username", StringComparison.OrdinalIgnoreCase)) user = value;
                else if (key.Equals("password", StringComparison.OrdinalIgnoreCase)) pass = value;
            }
            if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
                return new XtreamLogin(uri.Scheme, uri.Host, uri.IsDefaultPort ? null : uri.Port, user, pass);
        }
        if (sampleStreamUrl != null && StreamShape.Match(sampleStreamUrl) is { Success: true } m)
            return new XtreamLogin(m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value,
                m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : null,
                Uri.UnescapeDataString(m.Groups[4].Value), Uri.UnescapeDataString(m.Groups[5].Value));
        return null;
    }

    // Logins found this session, by playlist id. Kept in memory only.
    static readonly Dictionary<string, XtreamLogin> Logins = [];

    public static void Remember(string id, XtreamLogin? login)
    {
        if (login != null) Logins[id] = login;
        else Logins.Remove(id);
    }

    public static XtreamLogin? Login(string id) => Logins.GetValueOrDefault(id);
    public static void Forget(string id) => Logins.Remove(id);

    // Login of any saved playlist: from its link, else from the first stream URL in its caches.
    public static async Task<XtreamLogin?> LoginAsync(PlaylistEntry entry)
    {
        if (Login(entry.Id) is { } known) return known;
        var login = Detect(entry.Source, null) ?? Detect(entry.Source, await M3uExport.FirstUrlAsync(entry.Id));
        Remember(entry.Id, login);
        return login;
    }

    // 5 s, silent on any failure: the caller keeps what it had. One request, no retries.
    public static async Task<AccountInfo?> FetchAsync(XtreamLogin login, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await Http.Client.GetAsync(login.ApiUrl, timeout.Token);
            if (!resp.IsSuccessStatusCode) return null;
            await using var body = await resp.Content.ReadAsStreamAsync(timeout.Token);
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return Parse(doc.RootElement);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    // Null if the answer has no user_info (not a panel). Numbers may arrive as strings.
    public static AccountInfo? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("user_info", out var user) || user.ValueKind != JsonValueKind.Object) return null;
        root.TryGetProperty("server_info", out var server);
        var status = Text(user, "status");
        if (status == null && Num(user, "auth") == 0) status = "Rejected";
        string[] formats = [];
        if (user.TryGetProperty("allowed_output_formats", out var list) && list.ValueKind == JsonValueKind.Array)
            formats = list.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.String).Select(f => f.GetString()!).ToArray();
        return new AccountInfo(status, Num(user, "exp_date"), Flag(user, "is_trial"), (int?)Num(user, "active_cons"),
            (int?)Num(user, "max_connections"), Num(user, "created_at"), Text(server, "timezone"), Text(server, "time_now"),
            formats, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    static string? Text(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v)) return null;
        var text = v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    static long? Num(JsonElement obj, string name) =>
        long.TryParse(Text(obj, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    static bool? Flag(JsonElement obj, string name) => Num(obj, name) is { } n ? n != 0 : null;

    // Status bar text and whether it should be drawn in the error color (expired, banned or under 7 days left).
    public static (string Text, bool Warn) ExpirySummary(AccountInfo info)
    {
        if (info.Status is "Banned" or "Disabled" or "Rejected") return (info.Status, true);
        if (info.Expires is not { } exp || exp <= 0) return ("Unlimited", false);
        var date = DateTimeOffset.FromUnixTimeSeconds(exp).ToLocalTime();
        int days = (int)Math.Floor((date - DateTimeOffset.Now).TotalDays);
        var shown = date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        return days < 0 || info.Status == "Expired" ? ($"Expired {shown}", true) : ($"Expires {shown} | {days} d", days < 7);
    }

    // ---- cached next to the playlist caches, so the status bar has something before the first fetch ----

    static string FileFor(string id) => Path.Combine(PlaylistCache.DirFor(id), "account.json");

    public static AccountInfo? LoadCached(string id)
    {
        try { return JsonSerializer.Deserialize(File.ReadAllBytes(FileFor(id)), AccountJson.Default.AccountInfo); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void SaveCached(string id, AccountInfo info)
    {
        try
        {
            Directory.CreateDirectory(PlaylistCache.DirFor(id));
            File.WriteAllBytes(FileFor(id), JsonSerializer.SerializeToUtf8Bytes(info, AccountJson.Default.AccountInfo));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

[JsonSerializable(typeof(AccountInfo))]
partial class AccountJson : JsonSerializerContext;
