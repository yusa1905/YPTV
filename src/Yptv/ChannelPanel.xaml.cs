using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yptv;

// Search, group filter and the virtualized channel list. The list is flat: group headers are ordinary rows.
public partial class ChannelPanel : UserControl
{
    public event Action<ChannelRow>? ChannelChosen;
    public event Action<int>? HiddenChanged; // number of hidden groups
    public event Action<ChannelKind>? ScopeChanged; // Live / Movies / Series picked by the user

    ChannelModel? _model; // the model of the current scope
    ChannelModel? _live;  // kept while a VOD scope is open
    ChannelKind _scope = ChannelKind.Live;
    HashSet<string> _hidden = [];                    // hidden groups of the current model, adult ones included when they are hidden
    string? _note;                                   // shown instead of an empty list ("Loading…")
    List<Row> _view = [];
    string? _group;      // null = all groups
    string? _currentUrl; // playing channel; survives filtering and playlist refreshes
    bool _suppress;      // ignore selection events caused by our own updates
    ScrollViewer? _scroll;
    PlaylistPrefs _prefs = new();                    // expanded + hidden groups of the current playlist
    readonly HashSet<string> _forced = [];           // opened to reveal a channel; not remembered
    string? _cursor;                                 // group the Left/Right keys act on
    readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) }; // idle unless typing

    public ChannelPanel()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };
        SearchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || SearchBox.Text.Length == 0) return;
            ClearSearch();
            e.Handled = true;
        };
        List.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
        List.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Up or Key.Down:
                    Zap(e.Key == Key.Down ? 1 : -1);
                    break;
                case Key.Left or Key.Right:
                    SetCursorGroupOpen(e.Key == Key.Right);
                    break;
                default:
                    return;
            }
            e.Handled = true;
        };
    }

    public ChannelKind Scope => _scope;
    public int TotalCount => _model?.All.Length ?? 0;

    // Live channels of a (new or refreshed) playlist.
    public void SetChannels(List<Channel> channels, string playlistId)
    {
        _prefs = Settings.Current.PrefsFor(playlistId);
        _note = null;
        _live = new ChannelModel(channels);
        if (_scope != ChannelKind.Live) return; // shown when the user switches back
        _model = _live;
        RebuildGroups();
    }

    // No playlist (note empty) or a playlist still loading: an empty list with a note.
    public void Clear(string note)
    {
        _model = _live = null;
        _group = null;
        _currentUrl = null;
        _note = note;
        if (_scope != ChannelKind.Live)
        {
            _scope = ChannelKind.Live;
            LiveTab.IsChecked = true; // a new playlist starts in Live
        }
        _prefs = new PlaylistPrefs();
        RebuildGroups();
    }

    // Movies or series for the open VOD scope; null while they are not available yet. Stale kinds are ignored.
    public void SetVod(ChannelKind kind, List<Channel>? channels, string note)
    {
        if (kind != _scope || kind == ChannelKind.Live) return;
        _model = channels == null ? null : new ChannelModel(channels, flat: true);
        _note = channels is { Count: > 0 } ? null : note;
        RebuildGroups();
    }

    // Switches the scope in place: live comes back from memory, VOD scopes start empty until SetVod.
    public void ShowScope(ChannelKind kind)
    {
        if (kind == _scope) return;
        bool leavingVod = _scope != ChannelKind.Live;
        _scope = kind;
        _group = null;
        _model = kind == ChannelKind.Live ? _live : null;
        _note = kind == ChannelKind.Live ? null : "Loading…";
        if (leavingVod) GC.Collect(); // hand the big VOD model back right away; this is a user action, not idle
        RebuildGroups();
    }

    // Group combo + list after a new playlist or a change of hidden groups.
    void RebuildGroups()
    {
        if (_model == null)
        {
            _hidden = [];
            GroupBox.ItemsSource = null;
            ShowList([]);
            HiddenChanged?.Invoke(0);
            return;
        }
        _hidden = new HashSet<string>(_prefs.Hidden);
        var items = _model.GroupItems(_hidden);
        var keep = items.FirstOrDefault(g => g.Key == _group) ?? items[0];
        _group = keep.Key;
        _suppress = true;
        GroupBox.ItemsSource = items;
        GroupBox.SelectedItem = keep;
        _suppress = false;
        Refresh();
        HiddenChanged?.Invoke(_model.Infos.Count(i => _hidden.Contains(i.Name)));
    }

    void Scope_Checked(object sender, RoutedEventArgs e)
    {
        var kind = sender == MoviesTab ? ChannelKind.Movie : sender == SeriesTab ? ChannelKind.Series : ChannelKind.Live;
        if (kind == _scope) return;
        ShowScope(kind);
        ScopeChanged?.Invoke(kind);
    }

    public void OpenGroupManager()
    {
        if (_model == null) return;
        var dialog = new GroupsDialog(_model.Infos, _prefs.Hidden) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        _prefs.Hidden.Clear();
        _prefs.Hidden.UnionWith(dialog.Hidden);
        Settings.SaveSoon();
        RebuildGroups();
    }

    bool IsOpen(string key) => _prefs.Expanded.Contains(key) || _forced.Contains(key);

    void SetOpen(string key, bool open)
    {
        if (open) { if (_prefs.Expanded.Add(key)) Settings.SaveSoon(); }
        else if (_prefs.Expanded.Remove(key) | _forced.Remove(key)) Settings.SaveSoon();
    }

    // Left/Right: group of the selected channel, or the last one touched when the selection is hidden by a collapse.
    void SetCursorGroupOpen(bool open)
    {
        if (_group != null || SearchBox.Text.Length > 0) return;
        for (int i = List.SelectedIndex; i >= 0; i--)
            if (_view[i] is HeaderRow header)
            {
                _cursor = header.Key;
                break;
            }
        if (_cursor == null || IsOpen(_cursor) == open) return;
        SetOpen(_cursor, open);
        Refresh(keepScroll: true);
    }

    void CollapseAll_Click(object sender, RoutedEventArgs e)
    {
        if (_prefs.Expanded.Count == 0 && _forced.Count == 0) return;
        _prefs.Expanded.Clear();
        _forced.Clear();
        Settings.SaveSoon();
        Refresh();
    }

    void ExpandAll_Click(object sender, RoutedEventArgs e)
    {
        if (_model == null) return;
        _prefs.Expanded.Add(ChannelModel.FavKey);
        foreach (var info in _model.Infos)
            if (!_hidden.Contains(info.Name)) _prefs.Expanded.Add(info.Name);
        Settings.SaveSoon();
        Refresh();
    }

    void Groups_Click(object sender, RoutedEventArgs e) => OpenGroupManager();

    void Fav_Click(object sender, RoutedEventArgs e) => Refresh(keepScroll: true); // moves the channel in or out of Favorites

    // Plays the channel stored under this key (hash of its URL) if it exists in the list. Returns false otherwise.
    // play = false only highlights the row and scrolls to it (startup mode "show the list").
    public bool SelectKey(string key, bool play = true)
    {
        var row = _model?.Find(key);
        if (row == null) return false;
        return Select(row, play);
    }

    // Jumps to a channel by its number, clearing filters that would hide it.
    public bool SelectNumber(int number)
    {
        if (_model == null || number < 1 || number > _model.All.Length) return false;
        return Select(_model.All[number - 1]);
    }

    bool Select(ChannelRow row, bool play = true)
    {
        if (_model == null || _hidden.Contains(_model.GroupOf(row))) return false;
        if (!_view.Contains(row))
        {
            SearchBox.Clear();
            FavToggle.IsChecked = false;
            FavToggle.Content = "";
            _group = null;
            _suppress = true;
            GroupBox.SelectedIndex = 0;
            _suppress = false;
            var key = row.IsFavorite ? ChannelModel.FavKey : _model.GroupOf(row);
            if (!IsOpen(key)) _forced.Add(key);
            Refresh();
            if (!_view.Contains(row)) return false;
        }
        _suppress = !play;
        List.SelectedItem = row; // raises ChannelChosen via SelectionChanged
        _suppress = false;
        List.ScrollIntoView(row);
        return true;
    }

    // Previous/next channel, skipping group headers; wraps around at both ends.
    public void Zap(int delta)
    {
        int n = _view.Count;
        int i = List.SelectedIndex;
        for (int step = 0; step < n; step++)
        {
            i = i < 0 ? (delta > 0 ? 0 : n - 1) : (i + delta + n) % n;
            if (_view[i] is not ChannelRow row) continue;
            List.SelectedItem = row;
            List.ScrollIntoView(row);
            return;
        }
    }

    void Refresh(bool keepScroll = false)
    {
        if (_model == null) return;
        var offset = keepScroll ? Scroll()?.VerticalOffset ?? 0 : 0;
        var view = _model.Build(_group, SearchBox.Text, FavToggle.IsChecked == true, IsOpen, _hidden);
        var current = ShowList(view);
        if (keepScroll)
        {
            List.UpdateLayout();
            Scroll()?.ScrollToVerticalOffset(offset);
        }
        else if (current != null && SearchBox.Text.Length == 0) List.ScrollIntoView(current);
    }

    // Puts a built view into the list and keeps the playing channel selected. Returns that channel if it is listed.
    ChannelRow? ShowList(List<Row> view)
    {
        _view = view;
        var current = FindCurrent();
        _suppress = true;
        List.ItemsSource = _view;
        List.SelectedItem = current;
        _suppress = false;
        if (view.Count > 0) Note.Visibility = Visibility.Collapsed;
        else
        {
            Note.Text = _note ?? (_model == null ? "" : SearchBox.Text.Length > 0 ? "No matches" : "Nothing to show");
            Note.Visibility = Note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return current;
    }

    ChannelRow? FindCurrent() =>
        _currentUrl == null ? null : _view.OfType<ChannelRow>().FirstOrDefault(r => r.Channel.Url == _currentUrl);

    void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    void Clear_Click(object sender, RoutedEventArgs e) => ClearSearch();

    void ClearSearch()
    {
        SearchBox.Clear();
        _debounce.Stop();
        Refresh();
    }

    void FavToggle_Click(object sender, RoutedEventArgs e)
    {
        FavToggle.Content = FavToggle.IsChecked == true ? "" : "";
        Refresh();
    }

    void GroupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || GroupBox.SelectedItem is not GroupItem item) return;
        _group = item.Key;
        Refresh();
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        switch (List.SelectedItem)
        {
            case ChannelRow row:
                _currentUrl = row.Channel.Url;
                ChannelChosen?.Invoke(row);
                break;
            case HeaderRow: // not playable: put the selection back
                _suppress = true;
                List.SelectedItem = FindCurrent();
                _suppress = false;
                break;
        }
    }

    // Clicking a group header collapses/expands it instead of selecting it.
    void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        if (ItemsControl.ContainerFromElement(List, source) is not ListBoxItem { DataContext: HeaderRow header }) return;
        e.Handled = true;
        if (SearchBox.Text.Length > 0) return; // a search shows every header open
        _cursor = header.Key;
        SetOpen(header.Key, !IsOpen(header.Key));
        Refresh(keepScroll: true);
    }

    ScrollViewer? Scroll() => _scroll ??= FindDescendant<ScrollViewer>(List);

    static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}

