using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace Yptv;

// Peeks at the first few KB of a file or URL: tells channel lists from single streams, and checks a source before it is saved.
// Error texts are written for people and never contain the address.
static class PlaylistProbe
{
    const int PeekBytes = 8192;

    static async Task<string> HeadAsync(string source, CancellationToken ct)
    {
        var buffer = new byte[PeekBytes];
        int length = 0;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            using var resp = await Http.Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            await using var body = await resp.Content.ReadAsStreamAsync(ct);
            while (length < buffer.Length)
            {
                int n = await body.ReadAsync(buffer.AsMemory(length), ct);
                if (n == 0) break;
                length += n;
            }
        }
        else
        {
            await using var file = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
            while (length < buffer.Length)
            {
                int n = await file.ReadAsync(buffer.AsMemory(length), ct);
                if (n == 0) break;
                length += n;
            }
        }
        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    static bool IsChannelList(string head) => head.Contains("#EXTINF", StringComparison.Ordinal) && !head.Contains("#EXT-X-", StringComparison.Ordinal);

    // Command line / forwarded argument: a channel list (add it) or a single stream (just play it)?
    public static async Task<bool> LooksLikePlaylistAsync(string arg)
    {
        bool isUrl = Uri.TryCreate(arg, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
        if (!isUrl && !(arg.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || arg.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))) return false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            return IsChannelList(await HeadAsync(arg, timeout.Token));
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Unreachable now: a provider link still belongs in the list, anything else is tried as a stream.
            return isUrl && uri!.AbsolutePath.EndsWith("get.php", StringComparison.OrdinalIgnoreCase);
        }
    }

    // Null when the source holds a channel list, otherwise what to tell the user.
    public static async Task<string?> CheckM3uAsync(string source, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var head = await HeadAsync(source, timeout.Token);
            if (IsChannelList(head)) return null;
            if (head.Contains("#EXT-X-", StringComparison.Ordinal)) return "This is a single stream, not a channel list. Play it by opening it from the command line.";
            return "This doesn't look like an M3U playlist.";
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or OperationCanceledException
                                      or UriFormatException or ArgumentException or NotSupportedException)
        {
            return Explain(e, ct.IsCancellationRequested);
        }
    }

    public static string Explain(Exception e, bool cancelledByUser = false)
    {
        switch (e)
        {
            case HttpRequestException { StatusCode: { } code }:
                return (int)code switch
                {
                    401 or 403 => $"The server refused the login (HTTP {(int)code}). Check username and password.",
                    404 => "The server has nothing at this address (HTTP 404).",
                    429 => "The server says too many requests (HTTP 429). Try again later.",
                    >= 500 => $"The server has a problem (HTTP {(int)code}). Try again later.",
                    _ => $"The server answered HTTP {(int)code}.",
                };
            case HttpRequestException { InnerException: SocketException socket }:
                return socket.SocketErrorCode switch
                {
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "Can't find that server. Check the address.",
                    SocketError.ConnectionRefused => "The server refused the connection. Check the address and port.",
                    _ => "Can't reach the server. Check the address and your connection.",
                };
            case HttpRequestException:
                return "Can't reach the server. Check the address and your connection.";
            case OperationCanceledException:
                return cancelledByUser ? "Cancelled." : "The server didn't answer in time.";
            case FileNotFoundException or DirectoryNotFoundException:
                return "That file doesn't exist.";
            case UnauthorizedAccessException or IOException:
                return "Can't read that file.";
            case UriFormatException or ArgumentException or NotSupportedException:
                return "That address isn't valid.";
            default:
                return "Couldn't read the playlist.";
        }
    }
}
