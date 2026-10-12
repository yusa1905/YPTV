using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;

namespace Yptv;

// Playlists: the active one is shown from its cache at once and refreshed in the background (by the auto-refresh
// policy). Playlists are added, switched, removed and refreshed here; the dialogs call back into this file.
public partial class MainWindow
{
    CancellationTokenSource? _loadCts;
    bool _loading, _autoplayDone;
    int _channelCount;
    string? _loadError;
    PlaylistEntry? _entry; // the active playlist
    OneShot? _autoTimer;

    void WirePlaylist()
    {
        Channels.MenuButton.Click += (_, _) => ShowPlaylistMenu();
        Channels.HiddenChanged += Status.SetHidden;
        Channels.ScopeChanged += kind => _ = ShowScopeAsync(kind);
        Status.HiddenClicked += Channels.OpenGroupManager;
        Status.ExpiryClicked += () => { if (_entry != null) _ = ShowAccountAsync(_entry, this); };
        Overlay.OpenButton.Click += (_, _) => OpenPlaylistFile();
        Overlay.PasteButton.Click += (_, _) => AddPlaylistFlow(ClipboardUrl());
        _entry = Settings.Current.Active;
        _loading = _entry != null; // first frame shows "Loading…" instead of the empty state
        UpdateOverlay();
    }

    // ---- toolbar menu, manager, adding ----------------------------------------------------------------------------

    void ShowPlaylistMenu()
    {
        var items = Settings.Current.Sources
            .Select(entry => new PopupMenu.Item(entry.Name, entry.Id == _entry?.Id, () => ActivatePlaylist(entry)))
            .ToList();
        items.Add(new("Manage playlists…", false, ManagePlaylists));
        items.Add(new("Add playlist…", false, () => AddPlaylistFlow(null)));
        PopupMenu.Show(Channels.MenuButton, items, PlacementMode.Bottom);
    }

    void ManagePlaylists() => new PlaylistsDialog(this) { Owner = this }.ShowDialog();

    void AddPlaylistFlow(string? prefill)
    {
        var dialog = new AddPlaylistDialog(prefill) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } entry) ActivatePlaylist(entry);
    }

    void OpenPlaylistFile()
    {
        var dialog = new OpenFileDialog { Filter = "Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) _ = AddAndOpenAsync(dialog.FileName);
    }

    // Command line, drag and drop, second launch: check the source, save it, open it.
    async Task AddAndOpenAsync(string source)
    {
        var error = await PlaylistProbe.CheckM3uAsync(source, CancellationToken.None);
        if (error != null)
        {
            MessageBox.Show(this, error, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ActivatePlaylist(Settings.Current.AddSource(source, null, "m3u"));
    }

    static string? ClipboardUrl()
    {
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null;
            return text != null && text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : null;
        }
        catch (COMException)
        {
            return null; // clipboard locked by another app
        }
    }

    // ---- switching, removing, refreshing --------------------------------------------------------------------------

    internal void ActivatePlaylist(PlaylistEntry? entry)
    {
        _loadCts?.Cancel();
        _vodCts?.Cancel();
        _autoTimer?.Stop();
        _entry = entry;
        Settings.Current.ActiveId = entry?.Id;
        Settings.SaveSoon();
        _autoplayDone = true; // a switched playlist doesn't resume another one's channel
        _loadError = null;
        Status.SetExpiry(null);
        if (entry == null)
        {
            _loading = false;
            _channelCount = 0;
            Channels.Clear("");
            Status.SetCount(0);
            Status.SetSource("no playlist");
            UpdateOverlay();
            return;
        }
        Channels.Clear("Loading…");
        _ = LoadPlaylistAsync(entry, ShouldRefresh(entry));
    }

    internal void RemovePlaylist(PlaylistEntry entry)
    {
        bool wasActive = entry.Id == _entry?.Id;
        Xtream.Forget(entry.Id);
        Settings.Current.RemoveSource(entry);
        if (wasActive) ActivatePlaylist(Settings.Current.Sources.FirstOrDefault());
    }

    // Returns null on success, otherwise a message for the manager. The active playlist refreshes in the open list.
    internal async Task<string?> RefreshPlaylistAsync(PlaylistEntry entry)
    {
        if (entry.Id == _entry?.Id)
        {
            await LoadPlaylistAsync(entry, true);
            return _loadError;
        }
        try
        {
            await Playlist.LoadAsync(entry, true, (_, _) => { }, CancellationToken.None);
            return null;
        }
        catch (Exception e)
        {
            return PlaylistProbe.Explain(e);
        }
    }

    // Auto-refresh: Off never (unless there is no cache), On start every start, N hours when the cache is older than that.
    static bool ShouldRefresh(PlaylistEntry entry)
    {
        var mode = Settings.Current.AutoRefresh;
        if (entry.RefreshedUtc == 0 || mode == "Start") return true;
        if (mode == "Off") return false;
        return int.TryParse(mode, out var hours) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - entry.RefreshedUtc >= hours * 3600L;
    }

    // One timer set for the moment the next refresh is due; nothing runs in between.
    internal void ScheduleAutoRefresh()
    {
        _autoTimer?.Stop();
        if (_entry == null || !int.TryParse(Settings.Current.AutoRefresh, out var hours)) return;
        var due = _entry.RefreshedUtc + hours * 3600L - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var delay = TimeSpan.FromSeconds(Math.Clamp(due, 60, 24 * 3600));
        _autoTimer ??= new OneShot(delay, () => { if (_entry != null) _ = LoadPlaylistAsync(_entry, true); });
        _autoTimer.Delay = delay;
        _autoTimer.Restart();
    }

    // ---- loading ---------------------------------------------------------------------------------------------------

    // Movies / Series are read from their cache file only when the scope opens.
    async Task ShowScopeAsync(ChannelKind kind)
    {
        _vodCts?.Cancel();
        Status.SetCount(Channels.TotalCount);
        if (kind == ChannelKind.Live || _entry is not { } entry) return;
        var cts = _vodCts = new CancellationTokenSource();
        var channels = await Playlist.LoadVodAsync(kind, entry);
        if (cts.IsCancellationRequested) return;
        // No cache yet: the running refresh writes it, and ends by calling this again.
        var empty = _loading ? "Loading…" : kind == ChannelKind.Movie ? "No movies in this playlist" : "No series in this playlist";
        Channels.SetVod(kind, channels, empty);
        Status.SetCount(Channels.TotalCount);
    }

    CancellationTokenSource? _vodCts;

    // Cached list shows immediately, then (if refresh) the fresh one replaces it.
    async Task LoadPlaylistAsync(PlaylistEntry entry, bool refresh)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        _loading = true;
        _loadError = null;
        Status.SetSource(refresh ? "loading…" : "cached");
        UpdateOverlay();
        try
        {
            await Playlist.LoadAsync(entry, refresh, (channels, fromCache) =>
            {
                if (cts.IsCancellationRequested) return;
                _channelCount = channels.Count;
                Log.Info($"playlist {(fromCache ? "from cache" : "refreshed")}: {channels.Count:N0} live channels");
                Channels.SetChannels(channels, entry.Id);
                // Resume the last channel on start; the cached list is enough, the fresh one is a second chance.
                if (!_autoplayDone && Settings.Current.Startup == "None") _autoplayDone = true;
                else if (!_autoplayDone && Settings.Current.LastKey is { } last
                    && Channels.SelectKey(last, Settings.Current.Startup == "Resume")) _autoplayDone = true;
                Status.SetCount(Channels.TotalCount);
                Status.SetSource(!fromCache ? "up to date" : refresh ? "cached, refreshing…" : "cached");
                UpdateOverlay();
                FindAccount(entry, channels);
            }, cts.Token);
            if (Channels.Scope != ChannelKind.Live && !cts.IsCancellationRequested)
            {
                _loading = false;
                await ShowScopeAsync(Channels.Scope); // the refresh just wrote fresh VOD caches
            }
            if (refresh && !cts.IsCancellationRequested) _ = RefreshAccountAsync(entry); // once per refresh
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (cts.IsCancellationRequested) return;
            _loadError = PlaylistProbe.Explain(e); // never the address
            Log.Warn($"playlist load failed: {_loadError}");
            Status.SetSource("refresh failed");
        }
        finally
        {
            if (_loadCts == cts)
            {
                _loading = false;
                UpdateOverlay();
                ScheduleAutoRefresh();
                // Parsing a big playlist leaves tens of MB of garbage on the heap: give it back once, at the end of a load.
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                _ = Task.Run(() => GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true));
            }
        }
    }
}
