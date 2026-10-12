using System.Net.NetworkInformation;

namespace Yptv;

// Live streams that drop (error, or end without the user stopping) are retried on the same URL with a growing delay.
// Movies never reconnect. Log lines carry counts and reasons only, never addresses.
public partial class MainWindow
{
    static readonly int[] Backoff = [1, 2, 4, 8, 15]; // seconds before each try
    const long StableMs = 15_000; // a stream that played this long starts the count again

    Uri? _lastUri;
    int _retry;
    bool _reconnecting, _retryPending;
    long _playingSince;
    OneShot? _retryTimer, _stallTimer, _networkTimer, _attemptTimer;
    bool _stallArmed, _waitingForNetwork;
    long _lastBytes = -1, _waitStarted;
    int _quietTicks;
    const int AttemptSeconds = 15; // one reconnect attempt may take this long to start playing
    const int StallSeconds = 10;

    void OnDropped(string? detail)
    {
        if (_retryPending) return; // error and end can arrive together
        if (_stopRequested || _isVod || _lastUri == null || _player == null)
        {
            Fail(detail);
            return;
        }
        if (_playingSince > 0 && Environment.TickCount64 - _playingSince >= StableMs) _retry = 0;
        _playingSince = 0;
        if (_retry >= Backoff.Length)
        {
            _reconnecting = false;
            Fail($"Lost the stream, gave up after {Backoff.Length} tries");
            WaitForNetwork();
            return;
        }
        _reconnecting = true;
        _retryPending = true;
        var delay = TimeSpan.FromSeconds(Backoff[_retry]);
        _retry++;
        Log.Warn($"stream dropped ({detail ?? "error"}), retry {_retry}/{Backoff.Length} in {delay.TotalSeconds:0} s");
        SetState(PlayState.Reconnecting);
        _retryTimer ??= new OneShot(delay, Retry);
        _retryTimer.Delay = delay;
        _retryTimer.Restart();
    }

    void Retry()
    {
        _retryPending = false;
        if (_stopRequested || _lastUri == null || _player == null) return;
        _videoKnown = false;
        _player.Play(_lastUri);
        // With the cable out a connect can hang for minutes without an error: count a slow attempt as failed.
        (_attemptTimer ??= new OneShot(TimeSpan.FromSeconds(AttemptSeconds), OnAttemptTimeout)).Restart();
    }

    void OnAttemptTimeout()
    {
        if (!_reconnecting || _stopRequested || _player == null) return;
        _player.Stop();
        OnDropped("Stream lost");
    }

    // The user picked something else or stopped: forget any retry in flight.
    void CancelReconnect()
    {
        StopWaitingForNetwork();
        _stallTimer?.Stop();
        _attemptTimer?.Stop();
        _stallArmed = false;
        _lastBytes = -1;
        _quietTicks = 0;
        _retryTimer?.Stop();
        _retryPending = _reconnecting = false;
        _retry = 0;
        _playingSince = 0;
    }

    // A pulled cable raises neither an error nor an end: LibVLC just sits in Buffering, or keeps "playing" with
    // nothing arriving. Both count as a drop for live streams.
    void UpdateStallWatch(PlayState state)
    {
        bool buffering = state == PlayState.Buffering && !_isVod && _lastUri != null && !_stopRequested;
        if (buffering && !_stallArmed)
        {
            _stallArmed = true;
            (_stallTimer ??= new OneShot(TimeSpan.FromSeconds(StallSeconds), OnStall)).Restart();
        }
        else if (!buffering && _stallArmed)
        {
            _stallArmed = false;
            _stallTimer?.Stop();
        }
        if (state != PlayState.Playing) _lastBytes = -1;
    }

    void OnStall()
    {
        _stallArmed = false;
        if (_state == PlayState.Buffering) OnDropped("Stream stalled");
    }

    // Called once a second while playing: no new bytes for StallSeconds in a row on a live stream = stalled.
    void CheckStall()
    {
        if (_isVod || _lastUri == null || _player == null) return;
        var bytes = _player.ReadBytes();
        if (bytes != _lastBytes) { _lastBytes = bytes; _quietTicks = 0; return; }
        if (++_quietTicks < StallSeconds) return;
        _quietTicks = 0;
        _player.Stop(); // drop the dead connection; Play in Retry opens a fresh one
        OnDropped("Stream stalled");
    }

    // After the last try the user would have to press play. Instead, check every 10 s whether Windows reports a
    // network again and run another round of tries. A plugged-in cable is not usable until DHCP finishes, so this
    // polls rather than trusting one "network up" event. Gives up for good after 10 minutes.
    void WaitForNetwork()
    {
        if (_waitingForNetwork) return;
        _waitingForNetwork = true;
        if (_waitStarted == 0) _waitStarted = Environment.TickCount64;
        (_networkTimer ??= new OneShot(TimeSpan.FromSeconds(10), CheckNetwork)).Restart();
    }

    void StopWaitingForNetwork()
    {
        _waitStarted = 0;
        _waitingForNetwork = false;
        _networkTimer?.Stop();
    }

    void CheckNetwork()
    {
        if (!_waitingForNetwork) return; // cleared by playing, stop or a new channel
        if (Environment.TickCount64 - _waitStarted > 600_000)
        {
            StopWaitingForNetwork();
            return;
        }
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            _networkTimer!.Restart();
            return;
        }
        _waitingForNetwork = false; // OnDropped arms it again (same 10 min window) if this round fails too
        _retry = 0;
        OnDropped("Stream lost");
    }
}