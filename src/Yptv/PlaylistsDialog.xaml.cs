using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace Yptv;

public sealed class PlaylistRow(PlaylistEntry entry, bool active, bool busy)
{
    public PlaylistEntry Entry { get; } = entry;
    public string Marker { get; } = active ? "\uE768" : ""; // play glyph on the active playlist
    public string Name => Entry.Name;
    public string Host => Entry.Host; // host or file name only, never the full address
    public string TypeText => Entry.TypeText;
    public string CountText => Entry.Count > 0 ? Entry.Count.ToString("N0") : "-";
    public string RefreshedText { get; } = busy ? "Refreshing…" : Ago(entry.RefreshedUtc);

    static string Ago(long unix)
    {
        if (unix <= 0) return "never";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} min ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} h ago"
            : $"{(int)span.TotalDays} d ago";
    }
}

// The playlist list: add, rename, remove, refresh, set active, account, export, copy link, auto-refresh.
public partial class PlaylistsDialog : Window
{
    internal static readonly (string Value, string Text)[] AutoChoices =
        [("Off", "Off"), ("Start", "On start"), ("6", "Every 6 hours"), ("12", "Every 12 hours"), ("24", "Every 24 hours")];

    readonly MainWindow _host;
    readonly HashSet<string> _busy = [];
    bool _loading;

    public PlaylistsDialog(MainWindow host)
    {
        InitializeComponent();
        Title = $"Playlists - {AppInfo.Name}";
        SourceInitialized += (_, _) => TitleBar.Apply(this);
        _host = host;
        _loading = true;
        AutoBox.ItemsSource = AutoChoices.Select(c => c.Text).ToList();
        AutoBox.SelectedIndex = Math.Max(0, Array.FindIndex(AutoChoices, c => c.Value == Settings.Current.AutoRefresh));
        _loading = false;
        Rebuild(Settings.Current.ActiveId);
    }

    PlaylistEntry? Selected => (List.SelectedItem as PlaylistRow)?.Entry;

    void Rebuild(string? select)
    {
        var settings = Settings.Current;
        List.ItemsSource = settings.Sources.Select(s => new PlaylistRow(s, s.Id == settings.ActiveId, _busy.Contains(s.Id))).ToList();
        List.SelectedItem = ((List<PlaylistRow>)List.ItemsSource).FirstOrDefault(r => r.Entry.Id == select) ?? ((List<PlaylistRow>)List.ItemsSource).FirstOrDefault();
        UpdateButtons();
    }

    void UpdateButtons()
    {
        var entry = Selected;
        bool any = entry != null;
        bool busy = any && _busy.Contains(entry!.Id);
        ActiveButton.IsEnabled = any && entry!.Id != Settings.Current.ActiveId;
        RefreshButton.IsEnabled = any && !busy;
        RenameButton.IsEnabled = any;
        RemoveButton.IsEnabled = any;
        AccountButton.Visibility = any && entry!.Xtream ? Visibility.Visible : Visibility.Collapsed; // Xtream playlists only
        ExportButton.IsEnabled = any && entry!.Count > 0;
        CopyButton.IsEnabled = any && !entry!.IsFile;
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    void List_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Selected is { } entry && entry.Id != Settings.Current.ActiveId) Activate(entry);
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddPlaylistDialog(null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } entry) return;
        Activate(entry);
    }

    void Active_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) Activate(entry);
    }

    void Activate(PlaylistEntry entry)
    {
        _host.ActivatePlaylist(entry);
        Rebuild(entry.Id);
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        _busy.Add(entry.Id);
        Message.Text = "";
        Rebuild(entry.Id);
        var error = await _host.RefreshPlaylistAsync(entry);
        _busy.Remove(entry.Id);
        Message.Text = error == null ? "" : $"Refresh failed: {error}";
        Rebuild(entry.Id);
    }

    void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var prompt = new PromptDialog("Rename", "Name", entry.Name) { Owner = this };
        if (prompt.ShowDialog() != true) return;
        entry.Name = prompt.Value;
        Settings.SaveSoon();
        Rebuild(entry.Id);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        var answer = MessageBox.Show(this, $"Remove \"{entry.Name}\"?\n\nIts favorites, hidden groups and cached lists are deleted too.",
            AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        _host.RemovePlaylist(entry);
        Rebuild(Settings.Current.ActiveId);
    }

    async void Account_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) await _host.ShowAccountAsync(entry, this);
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } entry) new ExportDialog(entry) { Owner = this }.ShowDialog();
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } entry) return;
        try
        {
            Clipboard.SetText(entry.Source);
            Message.Text = "Link copied. It may contain your login.";
        }
        catch (COMException)
        {
            Message.Text = "The clipboard is busy. Try again.";
        }
    }

    void Auto_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AutoBox.SelectedIndex < 0) return;
        Settings.Current.AutoRefresh = AutoChoices[AutoBox.SelectedIndex].Value;
        Settings.SaveSoon();
        _host.ScheduleAutoRefresh();
    }
}
