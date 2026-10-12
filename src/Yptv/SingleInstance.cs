using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Yptv;

// One running window per Windows user. A second launch hands its argument (a playlist file/URL or a stream) to the
// first one through a named pipe and exits. The pipe is per user and refuses other accounts.
static class SingleInstance
{
    // A test data folder (IPTV_DATA_DIR) gets its own instance, so test runs never talk to the user's window.
    static readonly string Id = AppInfo.Name.Replace(' ', '-') + "-" + Environment.UserName
        + (Environment.GetEnvironmentVariable("IPTV_DATA_DIR") is { Length: > 0 } dir ? "-" + string.Concat(dir.Where(char.IsLetterOrDigit)) : "");
    static Mutex? _mutex; // held for the life of the process

    public static bool TryBecomePrimary()
    {
        _mutex = new Mutex(true, @"Local\" + Id, out bool created);
        return created;
    }

    // False if the running instance didn't answer; the caller then just starts normally.
    public static bool Forward(string argument)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Id, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.WriteLine(argument);
            writer.Flush();
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Listens for forwarded arguments on a background thread-pool task; onMessage runs on that thread.
    public static void Listen(Action<string> onMessage) => Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(Id, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server, Encoding.UTF8);
                onMessage(await reader.ReadLineAsync() ?? "");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(1000); // a client that vanished mid-message: keep listening
            }
        }
    });
}
