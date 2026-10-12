using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;

namespace Yptv;

// Playback options: audio track, subtitles, aspect, deinterlace. Track lists are read from LibVLC when the menu opens
// (or a hotkey is pressed), never polled. Aspect and deinterlace persist; the last subtitle language is remembered.
public partial class MainWindow
{
    static readonly string[] AspectModes = ["Auto", "16:9", "4:3", "Fill", "Original"];

    void WireOptions()
    {
        Deck.OptionsButton.Click += (_, _) => ShowOptionsMenu();
        Video.SizeChanged += (_, _) => { if (Settings.Current.Aspect == "Fill" && _player != null) ApplyAspect(_player.MediaPlayer); };
    }

    // Real tracks only: LibVLC lists "Disable" as id -1.
    static TrackDescription[] Real(TrackDescription[]? tracks) => tracks?.Where(t => t.Id >= 0).ToArray() ?? [];

    void ShowOptionsMenu()
    {
        if (_player == null) return;
        var mp = _player.MediaPlayer;
        var settings = Settings.Current;
        var items = new List<PopupMenu.Item>();

        var audio = Real(mp.AudioTrackDescription);
        if (audio.Length > 1)
        {
            items.Add(new("Audio track", false, null));
            foreach (var track in audio)
            {
                var id = track.Id;
                items.Add(new(track.Name, mp.AudioTrack == id, () => mp.SetAudioTrack(id)));
            }
        }

        var subs = Real(mp.SpuDescription);
        if (subs.Length > 0)
        {
            items.Add(new("Subtitles", false, null));
            items.Add(new("Off", mp.Spu < 0, () => ChooseSubtitle(mp, -1, "")));
            foreach (var track in subs)
            {
                var id = track.Id;
                var name = track.Name;
                items.Add(new(name, mp.Spu == id, () => ChooseSubtitle(mp, id, LanguageOf(name))));
            }
        }

        items.Add(new("Aspect", false, null));
        foreach (var mode in AspectModes)
        {
            var chosen = mode;
            items.Add(new(mode, settings.Aspect == mode, () => SetAspect(chosen)));
        }

        items.Add(new("Deinterlace", false, null));
        items.Add(new("Off", !settings.Deinterlace, () => SetDeinterlace(false)));
        items.Add(new("On", settings.Deinterlace, () => SetDeinterlace(true)));

        items.Add(new("Window", false, null));
        items.Add(new("Mini mode (Ctrl+M)", _mini, ToggleMini));
        items.Add(new("Always on top (Ctrl+T)", settings.AlwaysOnTop, () => SetAlwaysOnTop(!settings.AlwaysOnTop)));

        PopupMenu.Show(Deck.OptionsButton, items);
    }

    // Track names look like "Track 2 - [English]": the bracket is the language.
    static string LanguageOf(string name) =>
        Regex.Match(name, @"\[([^\]]+)\]\s*$") is { Success: true } m ? m.Groups[1].Value : name;

    void ChooseSubtitle(MediaPlayer mp, int id, string language)
    {
        mp.SetSpu(id);
        Settings.Current.SubLanguage = language; // "" = the user turned subtitles off
        Settings.SaveSoon();
    }

    // A new stream announces its subtitle tracks after Playing: pick the remembered language, once.
    void AutoSubtitle()
    {
        if (_player is not { } player || string.IsNullOrEmpty(Settings.Current.SubLanguage)) return;
        var mp = player.MediaPlayer;
        if (mp.Spu >= 0) return;
        foreach (var track in Real(mp.SpuDescription))
        {
            if (!string.Equals(LanguageOf(track.Name), Settings.Current.SubLanguage, StringComparison.OrdinalIgnoreCase)) continue;
            mp.SetSpu(track.Id);
            return;
        }
    }

    void SetAspect(string mode)
    {
        Settings.Current.Aspect = mode;
        Settings.SaveSoon();
        if (_player != null) ApplyAspect(_player.MediaPlayer);
    }

    void ApplyAspect(MediaPlayer mp)
    {
        var mode = Settings.Current.Aspect;
        mp.Scale = mode == "Original" ? 1f : 0f;
        mp.AspectRatio = mode is "16:9" or "4:3" ? mode : null;
        // Fill = crop the picture to the window's own shape; recalculated when the window is resized.
        mp.CropGeometry = mode == "Fill" && Video.ActualWidth >= 1 && Video.ActualHeight >= 1
            ? $"{(int)Video.ActualWidth}:{(int)Video.ActualHeight}"
            : null;
    }

    void SetDeinterlace(bool on)
    {
        Settings.Current.Deinterlace = on;
        Settings.SaveSoon();
        _player?.MediaPlayer.SetDeinterlace(on ? "yadif" : null);
    }

    // The video output is rebuilt for every stream, so the persisted choices are applied again on Playing.
    void ApplyVideoOptions(MediaPlayer mp)
    {
        if (Settings.Current.Aspect != "Auto") ApplyAspect(mp);
        if (Settings.Current.Deinterlace) mp.SetDeinterlace("yadif");
    }

    // Hotkeys: B audio, V subtitles, A aspect, D deinterlace.
    void CycleAudio()
    {
        if (_player is not { } player) return;
        var mp = player.MediaPlayer;
        var tracks = Real(mp.AudioTrackDescription);
        if (tracks.Length < 2)
        {
            Note("NO OTHER AUDIO TRACK");
            return;
        }
        var next = tracks[(Math.Max(0, Array.FindIndex(tracks, t => t.Id == mp.AudioTrack)) + 1) % tracks.Length];
        mp.SetAudioTrack(next.Id);
        Note("AUDIO " + next.Name);
    }

    void CycleSubtitle()
    {
        if (_player is not { } player) return;
        var mp = player.MediaPlayer;
        var tracks = Real(mp.SpuDescription);
        if (tracks.Length == 0)
        {
            Note("NO SUBTITLES");
            return;
        }
        var index = Array.FindIndex(tracks, t => t.Id == mp.Spu) + 1; // 0 = off, so "off" comes after the last track
        if (index >= tracks.Length)
        {
            ChooseSubtitle(mp, -1, "");
            Note("SUBTITLES OFF");
            return;
        }
        var track = tracks[index];
        ChooseSubtitle(mp, track.Id, LanguageOf(track.Name));
        Note("SUBTITLES " + LanguageOf(track.Name));
    }

    void CycleAspect()
    {
        var next = AspectModes[(Array.IndexOf(AspectModes, Settings.Current.Aspect) + 1) % AspectModes.Length];
        SetAspect(next);
        Note("ASPECT " + next.ToUpperInvariant());
    }

    void ToggleDeinterlace()
    {
        SetDeinterlace(!Settings.Current.Deinterlace);
        Note(Settings.Current.Deinterlace ? "DEINTERLACE ON" : "DEINTERLACE OFF");
    }

    // Short message in the OSD well, windowed or fullscreen.
    void Note(string text)
    {
        Overlay.ShowOsd(0, text);
        (_hideOsd ??= new OneShot(TimeSpan.FromSeconds(3), Overlay.HideOsd)).Restart();
    }
}
