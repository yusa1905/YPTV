namespace Yptv;

public sealed record GroupItem(string? Key, string Label, int Count)
{
    public string CountText => Count.ToString("N0");
}

public sealed record GroupInfo(string Name, string Label, int Count);

// Builds the flat row list (group headers + channels) for a group / search / favorites filter.
// A flat model (movies, series) never shows headers: too many rows for collapsible groups.
sealed class ChannelModel
{
    public const string FavKey = "\0favorites"; // pinned group of favorite channels
    const string Ungrouped = "Ungrouped";

    sealed class Group(string name)
    {
        public string Name { get; } = name;
        public string Label { get; } = TextClean.Clean(name);
        public List<ChannelRow> Rows { get; } = [];
    }

    readonly List<Group> _groups = []; // first-seen order
    readonly Dictionary<string, Group> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly bool _flat;

    public ChannelRow[] All { get; }
    public List<GroupInfo> Infos { get; } = [];

    public ChannelModel(List<Channel> channels, bool flat = false)
    {
        _flat = flat;
        All = new ChannelRow[channels.Count];
        for (int i = 0; i < All.Length; i++)
        {
            var channel = channels[i];
            var row = All[i] = new ChannelRow(channel, i + 1);
            var name = channel.Group ?? Ungrouped;
            if (!_byName.TryGetValue(name, out var group))
            {
                _byName[name] = group = new Group(name);
                _groups.Add(group);
            }
            row.GroupKey = group.Name;
            group.Rows.Add(row);
        }
        MigrateKeys();
        foreach (var g in _groups) Infos.Add(new GroupInfo(g.Name, g.Label, g.Rows.Count));
    }
    // Favorites and the last channel saved by older versions are hashes of the full stream URL:
    // swap them for the stable key of the channel they match.
    void MigrateKeys()
    {
        var s = Settings.Current;
        var legacy = s.Favorites.Where(k => !k.StartsWith(Channel.KeyPrefix, StringComparison.Ordinal)).ToHashSet();
        bool lastOld = s.LastKey is { } l && !l.StartsWith(Channel.KeyPrefix, StringComparison.Ordinal);
        if (legacy.Count == 0 && !lastOld) return;
        foreach (var row in All)
        {
            var old = Settings.KeyOf(row.Channel.Url);
            if (legacy.Contains(old))
            {
                s.Favorites.Remove(old);
                s.Favorites.Add(row.Key);
            }
            if (lastOld && s.LastKey == old) s.LastKey = row.Key;
        }
        Settings.SaveSoon();
    }

    public ChannelRow? Find(string key) => All.FirstOrDefault(r => r.Key == key);

    // Group the channel is listed under (canonical name, as stored in prefs).
    public string GroupOf(ChannelRow row) => row.GroupKey;

    // "All groups" plus every group that is not hidden.
    public List<GroupItem> GroupItems(HashSet<string> hidden)
    {
        var items = new List<GroupItem>(_groups.Count + 1) { null! };
        int total = 0;
        foreach (var g in _groups)
        {
            if (hidden.Contains(g.Name)) continue;
            items.Add(new GroupItem(g.Name, g.Label, g.Rows.Count));
            total += g.Rows.Count;
        }
        items[0] = new GroupItem(null, "All groups", total);
        return items;
    }

    // Headers only appear when showing several groups; favorites are pinned on top as their own group.
    // A search lists matches under auto-expanded headers and leaves the remembered state alone.
    public List<Row> Build(string? group, string query, bool favoritesOnly, Func<string, bool> isOpen, HashSet<string> hidden)
    {
        var tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool searching = tokens.Length > 0;
        bool headers = !_flat && group == null && _groups.Count > 1;
        bool pinned = headers && Settings.Current.Favorites.Count > 0;
        var result = new List<Row>(_flat ? 1024 : All.Length + _groups.Count);
        var matches = new List<ChannelRow>();

        if (pinned)
        {
            foreach (var g in _groups)
            {
                if (hidden.Contains(g.Name)) continue;
                foreach (var row in g.Rows)
                    if (row.IsFavorite && Matches(row, tokens, false)) matches.Add(row);
            }
            AddGroup(result, FavKey, "Favorites", matches, searching || isOpen(FavKey), true);
        }

        foreach (var g in _groups)
        {
            if (hidden.Contains(g.Name)) continue;
            if (group != null && !string.Equals(group, g.Name, StringComparison.OrdinalIgnoreCase)) continue;
            matches.Clear();
            foreach (var row in g.Rows)
                if ((!pinned || !row.IsFavorite) && Matches(row, tokens, favoritesOnly)) matches.Add(row);
            AddGroup(result, g.Name, g.Label, matches, searching || isOpen(g.Name), headers);
        }
        return result;
    }

    static void AddGroup(List<Row> result, string key, string title, List<ChannelRow> matches, bool open, bool header)
    {
        if (matches.Count > 0)
        {
            if (header) result.Add(new HeaderRow(key, title, matches.Count, !open));
            if (open || !header) result.AddRange(matches);
        }
        if (key == FavKey) matches.Clear();
    }

    static bool Matches(ChannelRow row, string[] tokens, bool favoritesOnly)
    {
        if (favoritesOnly && !row.IsFavorite) return false;
        foreach (var token in tokens)
            if (!row.Lower.Contains(token, StringComparison.Ordinal)) return false;
        return true;
    }
}


