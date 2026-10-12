using System.Net;
using System.Net.Http;

namespace Yptv;

// One shared HttpClient. Many providers reject unknown user agents, so send VLC's (what LibVLC sends anyway).
static class Http
{
    public static readonly HttpClient Client = Create();

    static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VLC/3.0.21 LibVLC/3.0.21");
        return client;
    }
}
