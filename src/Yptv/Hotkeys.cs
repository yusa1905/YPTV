namespace Yptv;

// The one hotkey table: the F1 cheat sheet reads it, and the README table is copied from it.
// Dispatch lives in MainWindow.Keys.cs (and ChannelPanel for the list keys); keep both in step when a key changes.
static class Hotkeys
{
    public static readonly (string Keys, string Action)[] All =
    [
        ("Up / Down", "Previous / next channel"),
        ("0-9, Enter", "Jump to a channel number"),
        ("Left / Right", "Collapse / expand the group in the list"),
        ("Space", "Play / pause"),
        ("M", "Mute"),
        ("J / L", "Seek -10 s / +10 s (movies, series)"),
        ("B", "Next audio track"),
        ("V", "Next subtitle track, then off"),
        ("A", "Next aspect mode"),
        ("D", "Deinterlace on / off"),
        ("F11", "Fullscreen"),
        ("Esc", "Leave fullscreen, cancel number entry"),
        ("Ctrl+M", "Mini mode"),
        ("Ctrl+T", "Always on top"),
        ("Ctrl+,", "Settings"),
        ("F1", "This list"),
    ];

    // For the README.
    public static string Markdown() =>
        "| Key | Action |\n| --- | --- |\n" + string.Join("\n", All.Select(h => $"| {h.Keys} | {h.Action} |"));
}
