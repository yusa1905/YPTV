using System.Windows;

namespace Yptv;

// Things handed to the window from outside: the command line, a second launch, files dropped on the window.
public partial class MainWindow
{
    void WireExternal()
    {
        foreach (var target in new UIElement[] { this, Overlay }) // the overlay is its own window over the video
        {
            target.AllowDrop = true;
            target.DragOver += OnDragOver;
            target.Drop += OnDrop;
        }
    }

    // Once the player exists: start listening for second launches and handle our own argument.
    void StartExternal()
    {
        SingleInstance.Listen(argument => Dispatcher.InvokeAsync(async () =>
        {
            BringToFront();
            if (argument.Length > 0) await OpenArgumentAsync(argument);
        }));
    }

    void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = _fullscreen ? WindowState.Maximized : WindowState.Normal;
        Activate();
    }

    // A channel list is added and opened, anything else is played as a stream.
    internal async Task OpenArgumentAsync(string argument)
    {
        argument = argument.Trim().Trim('"');
        if (argument.Length == 0) return;
        if (await PlaylistProbe.LooksLikePlaylistAsync(argument)) await AddAndOpenAsync(argument);
        else
        {
            _autoplayDone = true;
            PlayUrl(argument);
        }
    }

    static string? DroppedSource(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            return files.FirstOrDefault(f => f.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase));
        }
        if (e.Data.GetData(DataFormats.UnicodeText) is string text && text.Trim() is var url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        return null;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedSource(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedSource(e) is { } source) await AddAndOpenAsync(source);
    }
}
