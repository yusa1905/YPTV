using System.ComponentModel;
using System.Windows.Media;

namespace Yptv;

// One line of the channel list: a group header or a channel.
public abstract class Row;

public sealed class HeaderRow(string key, string title, int count, bool collapsed) : Row
{
    public string Key { get; } = key;
    public string Title { get; } = key == ChannelModel.FavKey ? "FAVORITES" : title.ToUpperInvariant();
    public string CountText { get; } = count.ToString("N0");
    public string Chevron { get; } = collapsed ? "" : "";
}

public sealed class ChannelRow : Row, INotifyPropertyChanged
{
    static readonly PropertyChangedEventArgs LogoChanged = new(nameof(Logo)), FavoriteChanged = new(nameof(IsFavorite));

    public event PropertyChangedEventHandler? PropertyChanged;

    public Channel Channel { get; }
    public int Number { get; } // position in the list of its scope, stable under filtering
    public string Lower { get; } // precomputed for search
    public string GroupKey { get; set; } = ""; // canonical group name, set by ChannelModel
    string? _key;

    // Identity independent of host and login: what favorites and the last channel are stored under.
    public string Key => _key ??= Channel.StableKey();

    public string Name { get; }

    public ChannelRow(Channel channel, int number)
    {
        Channel = channel;
        Number = number;
        Name = TextClean.Clean(channel.Name);
        Lower = Name.ToLowerInvariant();
    }

    public string NumberText => Number.ToString("D3");

    // Two letters shown until the logo has loaded.
    public string Monogram
    {
        get
        {
            Span<char> letters = stackalloc char[2];
            int n = 0;
            foreach (var c in Name)
            {
                if (!char.IsLetterOrDigit(c)) continue;
                letters[n++] = char.ToUpperInvariant(c);
                if (n == 2) break;
            }
            return new string(letters[..n]);
        }
    }

    public bool IsFavorite
    {
        get => Settings.Current.Favorites.Contains(Key);
        set
        {
            if (value == IsFavorite) return;
            if (value) Settings.Current.Favorites.Add(Key);
            else Settings.Current.Favorites.Remove(Key);
            Settings.SaveSoon();
            PropertyChanged?.Invoke(this, FavoriteChanged);
        }
    }

    // Read when the row is bound, i.e. only for rows that are on screen. Loads lazily, then re-notifies.
    public ImageSource? Logo
    {
        get
        {
            if (Channel.Logo is not { } url) return null;
            var image = LogoCache.TryGet(url);
            if (image == null) LogoCache.Request(url, () => PropertyChanged?.Invoke(this, LogoChanged));
            return image;
        }
    }
}


