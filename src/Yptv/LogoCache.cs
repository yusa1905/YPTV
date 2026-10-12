using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yptv;

// Lazy logos: memory -> disk thumbnail (PNG, ~32 px) -> download. At most a few workers; the newest request
// goes first so fast scrolling never works through stale rows. Dictionaries below are UI-thread only.
static class LogoCache
{
    const int Size = 32, MaxWorkers = 4, MaxQueued = 48, MaxMemory = 512, MaxBytes = 2_000_000;

    static readonly string Dir = Path.Combine(AppPaths.Dir, "logos");
    static readonly Dictionary<string, ImageSource> Memory = [];
    static readonly Dictionary<string, List<Action>> Waiting = [];
    static readonly HashSet<string> Failed = [];
    static readonly LinkedList<string> Queue = new(); // newest first; guarded by Gate together with _workers
    static readonly object Gate = new();
    static int _workers;

    public static ImageSource? TryGet(string url) => Memory.GetValueOrDefault(url);

    // onLoaded runs on the UI thread once the logo is available (never if it can't be loaded).
    public static void Request(string url, Action onLoaded)
    {
        if (Failed.Contains(url) || !IsHttp(url)) return;
        if (Waiting.TryGetValue(url, out var callbacks))
        {
            callbacks.Add(onLoaded);
            return;
        }
        Waiting[url] = [onLoaded];
        bool startWorker;
        lock (Gate)
        {
            Queue.AddFirst(url);
            if (Queue.Count > MaxQueued)
            {
                Waiting.Remove(Queue.Last!.Value); // dropped; its row asks again when it is bound again
                Queue.RemoveLast();
            }
            startWorker = _workers < MaxWorkers;
            if (startWorker) _workers++;
        }
        if (startWorker) Task.Run(Work);
    }

    static async Task Work()
    {
        while (true)
        {
            string url;
            lock (Gate)
            {
                if (Queue.First == null)
                {
                    _workers--;
                    return;
                }
                url = Queue.First.Value;
                Queue.RemoveFirst();
            }
            ImageSource? image = null;
            try
            {
                image = await LoadAsync(url);
            }
            catch (Exception)
            {
                // Network, disk or decoder failure: any of them just means "no logo".
            }
            Application.Current?.Dispatcher.BeginInvoke(() => Done(url, image));
        }
    }

    static void Done(string url, ImageSource? image)
    {
        if (image == null)
        {
            Failed.Add(url);
            Waiting.Remove(url);
            return;
        }
        if (Memory.Count >= MaxMemory) Memory.Clear(); // crude eviction; the disk cache makes refills cheap
        Memory[url] = image;
        if (Waiting.Remove(url, out var callbacks))
            foreach (var callback in callbacks) callback();
    }

    static async Task<ImageSource?> LoadAsync(string url)
    {
        var file = Path.Combine(Dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url))) + ".png");
        if (File.Exists(file)) return Decode(await File.ReadAllBytesAsync(file));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await Http.Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxBytes) return null;
        var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        if (bytes.Length > MaxBytes) return null;

        var image = Decode(bytes);
        Directory.CreateDirectory(Dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        await using (var fs = File.Create(file + ".tmp")) encoder.Save(fs);
        File.Move(file + ".tmp", file, overwrite: true);
        return image;
    }

    // Decodes straight to ~32 px on the longer side, so a 2000 px logo never sits in memory at full size.
    static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None).Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        stream.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        if (width >= height) bitmap.DecodePixelWidth = Math.Min(Size, width);
        else bitmap.DecodePixelHeight = Math.Min(Size, height);
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    static bool IsHttp(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
