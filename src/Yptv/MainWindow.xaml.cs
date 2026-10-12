using System.Windows;

namespace Yptv;

public partial class MainWindow : Window
{
    Player? _player;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppInfo.Name;
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        WirePlayback();
        WirePlaylist();
        WireFullscreen();
        WireKeys();
        WireOptions();
        WireExternal();
        WireMini();
        ContentRendered += OnContentRendered;
        Closed += (_, _) =>
        {
            Log.Info("exit");
            Settings.Save();
            _player?.Dispose();
        };
    }

    // Show the window first, then load LibVLC in the background.
    async void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        var argument = App.StartArgument;
        if (argument != null) _autoplayDone = true; // an explicit playlist or stream beats the remembered channel
        if (_entry != null) _ = LoadPlaylistAsync(_entry, ShouldRefresh(_entry));

        _player = await Player.CreateAsync();
        Log.Info($"LibVLC {_player.Version} ready");
        Video.MediaPlayer = _player.MediaPlayer;
        WatchPlayer(_player.MediaPlayer);

        if (_queued != null)
        {
            var row = _queued;
            _queued = null;
            PlayChannel(row);
        }
        else if (argument != null) await OpenArgumentAsync(argument);
        StartExternal();
    }
}
