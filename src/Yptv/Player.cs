using System.IO;
using LibVLCSharp.Shared;

namespace Yptv;

// One LibVLC + one MediaPlayer for the whole app lifetime.
sealed class Player : IDisposable
{
    readonly LibVLC _vlc;
    readonly object _gate = new();
    volatile Uri? _latest;

    public MediaPlayer MediaPlayer { get; }
    public string Version => _vlc.Version;

    Player(LibVLC vlc)
    {
        _vlc = vlc;
        MediaPlayer = new MediaPlayer(vlc);
    }

    public static Task<Player> CreateAsync() => Task.Run(() =>
    {
        Core.Initialize();
        // Hardware decoding, caching and user agent are also set per media in Play, so changing them in Settings
        // needs no re-init: they apply from the next channel.
        // Without plugins.dat LibVLC loads every plugin on each start (+26 MB). It never writes the file on its own, so
        // the first run on a machine asks for it; the cache stays valid next to the plugins it was built from.
        var cache = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64", "plugins", "plugins.dat");
        var args = new List<string> { "--avcodec-hw=any", "--network-caching=300", "--no-video-title-show", "--verbose=1" };
        if (!File.Exists(cache)) args.Add("--reset-plugins-cache");
        var vlc = new LibVLC(args.ToArray());
        vlc.Log += (_, e) =>
        {
            if (e.Level is LogLevel.Warning or LogLevel.Error) Log.Warn($"vlc {e.Module}: {e.Message}");
        };
        // Debug aid: set IPTV_VLC_LOG=<file> to capture LibVLC's log.
        var log = Environment.GetEnvironmentVariable("IPTV_VLC_LOG");
        if (!string.IsNullOrEmpty(log)) vlc.SetLogFile(log);
        return new Player(vlc);
    });

    // Switching media stops the old one, which can block, so do it off the UI thread.
    // Rapid requests collapse: only the latest one plays.
    public void Play(Uri uri)
    {
        _latest = uri;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_gate)
            {
                if (uri != _latest) return;
                using var media = new Media(_vlc, uri);
                var settings = Settings.Current;
                media.AddOption($":network-caching={Math.Clamp(settings.BufferMs, 100, 5000)}");
                media.AddOption(settings.HwDecoding ? ":avcodec-hw=any" : ":avcodec-hw=none");
                if (!string.IsNullOrWhiteSpace(settings.UserAgent)) media.AddOption($":http-user-agent={settings.UserAgent.Trim()}");
                MediaPlayer.Play(media);
            }
        });
    }

    public void Stop()
    {
        _latest = null;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (_gate) MediaPlayer.Stop();
        });
    }

    public void TogglePause() => MediaPlayer.Pause();

    // Codec and size of the playing video track; null until the demuxer has published tracks.
    public (string Codec, int Width, int Height)? ReadVideo()
    {
        using var media = MediaPlayer.Media;
        if (media == null) return null;
        foreach (var track in media.Tracks)
            if (track.TrackType == TrackType.Video)
                return (CodecName(track.Codec), (int)track.Data.Video.Width, (int)track.Data.Video.Height);
        return null;
    }

    // LibVLC reports input bitrate in bytes per microsecond.
    public double ReadMbps()
    {
        using var media = MediaPlayer.Media;
        return media == null ? 0 : media.Statistics.InputBitrate * 8;
    }

    // Total bytes read from the network so far; a live stream whose count stops growing has stalled.
    public long ReadBytes()
    {
        using var media = MediaPlayer.Media;
        return media == null ? 0 : (long)media.Statistics.ReadBytes;
    }

    static string CodecName(uint fourcc)
    {
        var s = new string(new[] { (char)(fourcc & 0xFF), (char)((fourcc >> 8) & 0xFF), (char)((fourcc >> 16) & 0xFF), (char)((fourcc >> 24) & 0xFF) }).Trim();
        return s.ToLowerInvariant() switch
        {
            "h264" or "avc1" => "H.264",
            "hevc" or "h265" => "HEVC",
            "mpgv" or "mp2v" => "MPEG-2",
            "mp4v" => "MPEG-4",
            "vp80" => "VP8",
            "vp90" => "VP9",
            "av01" => "AV1",
            _ => s.ToUpperInvariant(),
        };
    }

    public void Dispose()
    {
        MediaPlayer.Dispose();
        _vlc.Dispose();
    }
}
