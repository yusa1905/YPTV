using System.Windows;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace Yptv;

enum PlayState { Idle, Opening, Buffering, Playing, Paused, Stopped, Error, Reconnecting }

// Transport, player events, status bar. LibVLC events arrive on its own threads: only marshal to the UI here.
public partial class MainWindow
{
    PlayState _state = PlayState.Idle;
    ChannelRow? _current;
    ChannelRow? _queued; // chosen before LibVLC finished loading
    string? _errorDetail, _videoText;
    bool _stopRequested, _videoKnown, _videoLive, _muted;
    volatile bool _isVod; // the playing entry is a movie or episode: seekable, ends normally
    float _buffer;
    readonly DispatcherTimer _statsTimer = new() { Interval = TimeSpan.FromSeconds(1) }; // runs only while playing

    void WirePlayback()
    {
        Deck.Volume.Value = Settings.Current.Volume;
        _muted = Settings.Current.Muted;
        Deck.SetMuted(_muted);

        Deck.Prev.Click += (_, _) => Channels.Zap(-1);
        Deck.Next.Click += (_, _) => Channels.Zap(1);
        Deck.PlayPause.Click += (_, _) => TogglePause();
        Deck.Stop.Click += (_, _) => StopPlayback();
        Deck.Mute.Click += (_, _) => ToggleMute();
        Deck.Volume.ValueChanged += (_, e) => SetVolume((int)e.NewValue);
        Overlay.RetryButton.Click += (_, _) => { if (_current != null) PlayChannel(_current); };
        Deck.SeekRequested += SeekTo;
        Channels.ChannelChosen += PlayChannel;
        _statsTimer.Tick += (_, _) => UpdateStats();
        StateChanged += (_, _) => UpdateStatsTimer();
        SetState(PlayState.Idle);
    }

    void WatchPlayer(MediaPlayer mp)
    {
        mp.Opening += (_, _) => Ui(() => { if (!_stopRequested && !_reconnecting) SetState(PlayState.Opening); });
        mp.Buffering += (_, e) => { var pct = e.Cache; Ui(() => OnBuffering(pct)); };
        mp.Playing += (_, _) => Ui(OnPlaying);
        mp.Paused += (_, _) => Ui(() => SetState(PlayState.Paused));
        mp.Stopped += (_, _) => Ui(() => { if (_stopRequested) SetState(PlayState.Stopped); });
        mp.EncounteredError += (_, _) => Ui(() => OnDropped(null));
        mp.ESAdded += (_, e) => { if (e.Type == TrackType.Text) Ui(AutoSubtitle); };
        mp.EndReached += (_, _) => Ui(OnEndReached);
        // VOD progress, a few times a second; live streams skip the marshaling entirely.
        mp.TimeChanged += (_, e) => { if (_isVod) { var time = e.Time; Ui(() => Deck.SetTime(time, _player?.MediaPlayer.Length ?? 0)); } };
        mp.LengthChanged += (_, e) => { if (_isVod) { var length = e.Length; Ui(() => Deck.SetTime(_player?.MediaPlayer.Time ?? 0, length)); } };
    }

    void OnEndReached()
    {
        if (_state == PlayState.Error) return;
        if (!_isVod) OnDropped("Stream ended");
        else
        {
            _stopRequested = true; // a finished movie is not an error
            SetState(PlayState.Stopped);
        }
    }

    // J / L: jump back or forward in a movie or episode.
    void SeekBy(int milliseconds)
    {
        if (_player is not { } player || !_isVod || !player.MediaPlayer.IsSeekable) return;
        var mp = player.MediaPlayer;
        mp.Time = Math.Clamp(mp.Time + milliseconds, 0, Math.Max(0, mp.Length - 1000));
    }

    void SeekTo(float position)
    {
        if (_player is { } player && _isVod && player.MediaPlayer.IsSeekable) player.MediaPlayer.Position = position;
    }

    void Ui(Action action) => Dispatcher.InvokeAsync(action);

    void Fail(string? detail)
    {
        _errorDetail = detail;
        SetState(PlayState.Error);
    }

    void PlayChannel(ChannelRow row)
    {
        _current = row;
        CancelReconnect();
        _videoText = null;
        _errorDetail = null;
        Deck.SetChannel(row.Number, row.Name, row.Channel.Group);
        Title = $"{row.Name} - {AppInfo.Name}";
        ShowOsd(row);
        _isVod = row.Channel.Kind != ChannelKind.Live;
        Log.Info($"play #{row.Number} ({row.Channel.Kind})");
        Deck.SetVod(_isVod);
        Settings.Current.LastKey = row.Key;
        Settings.SaveSoon();
        if (_player == null)
        {
            _queued = row;
            SetState(PlayState.Opening);
            return;
        }
        if (!Uri.TryCreate(row.Channel.Url, UriKind.Absolute, out var uri))
        {
            Fail("Invalid URL");
            return;
        }
        _stopRequested = false;
        _videoKnown = false;
        _lastUri = uri;
        SetState(PlayState.Opening);
        _player.Play(uri);
    }

    // Stream URL from the command line. Shows the host only: full URLs can carry credentials.
    void PlayUrl(string text)
    {
        if (_player == null || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return;
        _current = null;
        CancelReconnect();
        _isVod = false;
        Deck.SetVod(false);
        _videoText = null;
        _errorDetail = null;
        _stopRequested = false;
        _videoKnown = false;
        Deck.SetChannel(0, uri.Host.Length > 0 ? uri.Host : "stream", null);
        _lastUri = uri;
        SetState(PlayState.Opening);
        _player.Play(uri);
    }

    void TogglePause()
    {
        if (_player == null) return;
        if (_state is PlayState.Idle or PlayState.Stopped or PlayState.Error)
        {
            if (_current != null) PlayChannel(_current);
            return;
        }
        _player.TogglePause();
    }

    void StopPlayback()
    {
        if (_player == null) return;
        _stopRequested = true;
        CancelReconnect();
        _player.Stop();
        SetState(PlayState.Stopped);
    }

    void ToggleMute()
    {
        _muted = !_muted;
        Deck.SetMuted(_muted);
        if (_player != null) _player.MediaPlayer.Mute = _muted;
        Settings.Current.Muted = _muted;
        Settings.SaveSoon();
    }

    void SetVolume(int volume)
    {
        if (_player != null) _player.MediaPlayer.Volume = volume;
        Settings.Current.Volume = volume;
        Settings.SaveSoon();
    }

    // LibVLC can drop volume/mute when a new stream opens, so reapply on every Playing.
    void OnPlaying()
    {
        if (_stopRequested) return;
        _reconnecting = false;
        StopWaitingForNetwork();
        _attemptTimer?.Stop();
        _playingSince = Environment.TickCount64;
        SetState(PlayState.Playing);
        if (_player == null) return;
        _player.MediaPlayer.Volume = (int)Deck.Volume.Value;
        _player.MediaPlayer.Mute = _muted;
        ApplyVideoOptions(_player.MediaPlayer);
    }

    void OnBuffering(float percent)
    {
        if (_stopRequested || _reconnecting) return;
        _buffer = percent;
        Status.SetBuffer($"buf {percent:0}%");
        if (percent < 100) SetState(PlayState.Buffering);
        else if (_state == PlayState.Buffering) SetState(PlayState.Playing);
    }

    void SetState(PlayState state)
    {
        if (state != _state) Log.Info(state == PlayState.Error ? $"state Error: {_errorDetail}" : $"state {state}");
        _state = state;
        var (text, dot) = state switch
        {
            PlayState.Opening => ("Opening…", "Warn"),
            PlayState.Buffering => ($"Buffering {_buffer:0}%", "Warn"),
            PlayState.Playing => ("Playing", "Fg"),
            PlayState.Paused => ("Paused", "Fg3"),
            PlayState.Stopped => ("Stopped", "Fg3"),
            PlayState.Error => (_errorDetail ?? "Can't play", "Err"),
            PlayState.Reconnecting => ($"Reconnecting {_retry}/{Backoff.Length}", "Warn"),
            _ => ("Ready", "Fg3"),
        };
        Status.SetState(text, dot);
        Deck.SetPlaying(state is PlayState.Opening or PlayState.Buffering or PlayState.Playing or PlayState.Reconnecting);
        if (state is PlayState.Idle or PlayState.Stopped or PlayState.Error)
        {
            Status.SetVideo(null);
            Status.SetRate(null);
            Status.SetBuffer(null);
            Deck.SetTime(0, 0);
            if (state != PlayState.Idle) Title = AppInfo.Name;
        }
        // Black until the first frame is live, so the video window's gray never shows.
        if (state is PlayState.Idle or PlayState.Opening or PlayState.Stopped or PlayState.Error or PlayState.Reconnecting) _videoLive = false;
        else if (state == PlayState.Playing) _videoLive = true;
        UpdateStallWatch(state);
        Overlay.SetBackdrop(!_videoLive);
        UpdateOverlay();
        UpdateStatsTimer();
    }

    // The center message over the video: first run, loading, idle hint or can't-play.
    void UpdateOverlay()
    {
        if (_state == PlayState.Error) Overlay.ShowError(_current?.Name);
        else if (_channelCount == 0)
        {
            if (_loading) Overlay.ShowLoading();
            else Overlay.ShowEmpty(_loadError == null ? null : "Couldn't load the playlist", _loadError);
        }
        else if (_state is PlayState.Idle or PlayState.Stopped) Overlay.ShowHint("Select a channel");
        else Overlay.Clear();
    }

    // Polled once a second, only while playing and not minimized.
    void UpdateStatsTimer() =>
        _statsTimer.IsEnabled = _state == PlayState.Playing && WindowState != WindowState.Minimized && IsVisible;

    void UpdateStats()
    {
        if (_player == null) return;
        if (!_videoKnown && _player.ReadVideo() is { } video)
        {
            _videoKnown = true;
            _videoText = $"{video.Codec} {video.Width}×{video.Height}";
            Status.SetVideo(_videoText);
            Deck.SetMeta(string.Join(" · ", new[] { _current?.Channel.Group, _videoText }.Where(s => !string.IsNullOrEmpty(s))));
        }
        Status.SetRate($"{_player.ReadMbps():0.0} Mb/s");
        CheckStall();
    }
}
